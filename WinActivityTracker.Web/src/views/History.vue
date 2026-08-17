<!--
  History view — visual timeline with time wheel picker
  Combines timeline visualization with aggregated stats
-->
<template>
  <div class="history-page">
    <!-- Time range picker with wheels -->
    <TimeRangePicker
      :start-date="startDate"
      :end-date="endDate"
      :earliest-date="earliestDate"
      :disabled="dataTooShort"
      @change="handleTimeChange"
      class="mb-3"
    />

    <!-- Error -->
    <div v-if="error" class="error-banner mb-3">
      {{ error }}
      <button class="close-btn" @click="error = ''" :aria-label="t('common.close')"><X :size="16" /></button>
    </div>

    <!-- Visual timeline -->
    <MemphisCard class="timeline-card mb-3">
      <h3 class="card-title">{{ t('history.card.visualTimeline') }}</h3>
      <!-- Easter egg: invalid time range -->
      <div v-if="!isTimeValid" class="easter-egg-chart">
        <div class="easter-egg-chart-text">{{ timeEasterEgg }}</div>
      </div>
      <!-- Data too short (< 2 min) -->
      <div v-else-if="dataTooShort" class="easter-egg-chart">
        <div class="easter-egg-chart-text">{{ t('history.tooShort') }}</div>
      </div>
      <template v-else>
        <MemphisSkeleton v-if="loading" :lines="6" />
        <div v-else>
          <div class="timeline-chart-wrap">
            <div ref="timelineChartRef" class="timeline-chart"></div>
            <!-- Hover overlay: a canvas layer painted with the background color
                 over every bar EXCEPT the hovered process's ones, which stay
                 full-color. No chart re-render — the ECharts canvas never
                 changes, so there is no flicker.
                 NOTE: must stay a SIBLING of the chart container —
                 echarts.init() wipes the container's existing children. -->
            <canvas ref="hoverDimmerRef" class="hover-dimmer"
                    :class="{ visible: !!hoveredProcess }"></canvas>
          </div>
          <div class="timeline-legend">
            <span class="legend-item">
              <span class="legend-box focus"></span>
              {{ t('history.legend.focus') }}
            </span>
            <span class="legend-item">
              <span class="legend-box visible"></span>
              {{ t('history.legend.visible') }}
            </span>
            <span class="legend-item">
              <span class="legend-box idle"></span>
              {{ t('history.legend.idle') }}
            </span>
            <span class="legend-item">
              <span class="legend-box offline"></span>
              {{ t('history.legend.offline') }}
            </span>
          </div>
        </div>
      </template>
    </MemphisCard>

  </div>
</template>

<script setup>
import { ref, inject, onMounted, onUnmounted, nextTick, watch } from 'vue'
import { toLocalTime, parseUtcTs, toLocalDatetimeString, fmtShortDur } from '../utils/time.js'
import { mergeByProcessName } from '../utils/process.js'
import { useI18n } from '../i18n/index.js'
import { useTheme } from '../composables/useTheme.js'
import { echarts } from '../utils/echartsInit.js'
import MemphisCard from '../components/MemphisCard.vue'
import MemphisSkeleton from '../components/MemphisSkeleton.vue'
import TimeRangePicker from '../components/TimeRangePicker.vue'
import { X } from '@lucide/vue'

const apiBase = inject('apiBase')
const { t } = useI18n()
const { isDark } = useTheme()

// Aborted on unmount so in-flight fetches don't keep running after the
// view is gone. Per-instance (re-created each time the view is mounted).
const abortController = new AbortController()

const THREE_HOURS_MS = 3 * 60 * 60 * 1000
const TWO_MIN_MS = 2 * 60 * 1000

const startDate = ref(new Date(Date.now() - THREE_HOURS_MS))
const endDate = ref(new Date())
const earliestDate = ref(null)

const data = ref([])
const timeline = ref([])
const windowSessions = ref([])
const systemEvents = ref([])
const mergeSameProcess = ref(true)
const totalSleepSeconds = ref(0)
const loading = ref(true)
const error = ref('')
const isTimeValid = ref(true)
const timeEasterEgg = ref('')
const dataTooShort = ref(false)

const timelineChartRef = ref(null)
let timelineChart = null

// Hover-dim state: while hovering a process, a canvas layer is painted with
// the background color over every bar EXCEPT that process's bars, which stay
// full-color. The ECharts canvas never re-renders, so there is no flicker.
const hoveredProcess = ref(null)
const hoverDimmerRef = ref(null)
// Bars of the latest render grouped by chart row: each row has its center Y,
// the focused-bar band height, and the bars' x-intervals ({proc, x, w}).
// Computed once per render/resize with convertToPixel; hover just reads this —
// calling convertToPixel per hover was slow (thousands of matrix transforms
// per mouse move).
let allBarsPx = [] // rows: [{ centerY, barH, bars: [{ proc, x, w, focused }] }]
let focusedWindowsData = []
let backgroundWindowsData = []
let lastRowCount = 1
// Focused bar height as actually rendered (api.size * 0.6), recorded by
// renderBar. Dimmer rects must match it exactly — deriving row height from
// convertToPixel breaks with few rows (single-row category bands are taller
// than the axis-label-free estimate).
let lastFocusedBarH = 0
// The bar height the current allBarsPx was built with (lastFocusedBarH may
// get recorded only after a progressive render starts — 'finished' rebuilds).
let lastUsedBarH = 0
// Chart background color (--surface-card), used to paint the dim layer.
let surfaceCard = ''

// Counter to cancel stale loadData calls — each call increments the ID;
// only the call whose ID still matches when it reaches renderTimeline() proceeds.
let loadId = 0

// Cache process colors keyed by `${processName}|${atTime}` so re-renders
// (e.g. theme toggle, resize) don't re-fetch /api/icons for the same range.
const colorCache = new Map()

// Handle time change from picker
function handleTimeChange({ start, end, valid, easterEgg }) {
  if (dataTooShort.value) return
  startDate.value = start
  endDate.value = end
  isTimeValid.value = valid
  timeEasterEgg.value = easterEgg || ''
  if (valid) {
    loadData()
  }
}

onMounted(async () => {
  try {
    const r = await fetch(`${apiBase}/api/settings`, { signal: abortController.signal })
    if (r.ok) {
      const s = await r.json()
      mergeSameProcess.value = s.mergeSameProcessSwitches ?? true
    }
  } catch (e) {
    console.error('Failed to load settings:', e)
  }

  // Fetch oldest record to constrain time picker (async, non-blocking)
  try {
    const r = await fetch(`${apiBase}/api/db/stats`, { signal: abortController.signal })
    if (r.ok) {
      const stats = await r.json()
      if (stats.oldestRecord) {
        const oldest = new Date(stats.oldestRecord.endsWith('Z') ? stats.oldestRecord : stats.oldestRecord + 'Z')
        earliestDate.value = oldest
        if ((Date.now() - oldest.getTime()) < THREE_HOURS_MS) {
          startDate.value = oldest
        }
        // Check if total data span is less than 2 minutes
        dataTooShort.value = (Date.now() - oldest.getTime()) < TWO_MIN_MS
      } else {
        dataTooShort.value = true
      }
    }
  } catch {
    // Fall back to 3h default on failure
  }

  await loadData()

  // Add window resize listener for chart responsiveness
  window.addEventListener('resize', handleResize)
})

// Watch for theme changes and re-render timeline
watch(isDark, () => {
  if (timeline.value && timeline.value.length > 0) {
    renderTimeline()
  }
})

// The picker can flip to an invalid range (easter egg replaces the chart div)
// while a load is in flight; the load's renderTimeline then bails on the
// missing div and the chart stays dead. Re-render as soon as the range is
// valid again.
watch(isTimeValid, (valid) => {
  if (valid && timeline.value && timeline.value.length > 0) {
    renderTimeline()
  }
})

onUnmounted(() => {
  window.removeEventListener('resize', handleResize)
  abortController.abort()

  if (timelineChart && !timelineChart.isDisposed()) {
    try {
      timelineChart.dispose()
    } catch (e) {
      console.warn('Error disposing chart on unmount:', e)
    }
  }
  timelineChart = null
  // Cancel pending loads
  loadId++
})

// Debounced resize handler
let resizeTimer = null
function handleResize() {
  if (resizeTimer) clearTimeout(resizeTimer)
  resizeTimer = setTimeout(() => {
    if (timelineChart && !timelineChart.isDisposed()) {
      timelineChart.resize()
      // Pixel positions shifted with the new size — refresh the overlay
      buildAllBarsPx()
      paintDimmer()
    }
  }, 200)
}


async function loadData() {
  const myLoadId = ++loadId
  loading.value = true
  error.value = ''

  if (dataTooShort.value) {
    loading.value = false
    return
  }

  // Dispose chart before skeleton replaces the DOM container,
  // so renderTimeline() will init a fresh instance on the new div.
  if (timelineChart && !timelineChart.isDisposed()) {
    timelineChart.dispose()
    timelineChart = null
  }
  // dispose() leaves the old canvases in the DOM; stacked stale canvases
  // above the live one swallow mouse events, breaking hover detection.
  timelineChartRef.value?.querySelectorAll('canvas').forEach(c => c.remove())

  try {
    const fromStr = toLocalDatetimeString(startDate.value)
    const toStr = toLocalDatetimeString(endDate.value)
    console.log('Loading data from', fromStr, 'to', toStr)

    // Build all URLs upfront so the four independent endpoints fire in parallel
    const summaryUrl = `${apiBase}/api/summary/range?from=${fromStr}&to=${toStr}`

    // Adjust limit based on date range to get good coverage
    const rangeInMs = endDate.value - startDate.value
    const rangeInHours = rangeInMs / (1000 * 60 * 60)
    const rangeInDays = rangeInMs / (1000 * 60 * 60 * 24)

    // Scale limit based on time range - smaller ranges need fewer records
    let timelineLimit
    if (rangeInHours <= 6) {
      timelineLimit = 1000  // 6 hours or less
    } else if (rangeInDays <= 1) {
      timelineLimit = 3000  // Up to 1 day
    } else if (rangeInDays <= 3) {
      timelineLimit = 10000  // Up to 3 days
    } else if (rangeInDays <= 7) {
      timelineLimit = 20000  // Up to 1 week
    } else {
      timelineLimit = 50000  // More than 1 week
    }

    const timelineUrl = `${apiBase}/api/windows/timeline?from=${fromStr}&to=${toStr}&limit=${timelineLimit}`
    const eventsUrl = `${apiBase}/api/system/events?from=${fromStr}&to=${toStr}`
    const sessionsUrl = `${apiBase}/api/windows/sessions?from=${fromStr}&to=${toStr}&limit=10000`

    console.log('Loading data from', fromStr, 'to', toStr, `(range: ${rangeInHours.toFixed(1)} hours / ${rangeInDays.toFixed(1)} days, limit: ${timelineLimit})`)
    const [r1, r2, r3, r4] = await Promise.all([
      fetch(summaryUrl, { signal: abortController.signal }),
      fetch(timelineUrl, { signal: abortController.signal }),
      fetch(eventsUrl, { signal: abortController.signal }),
      fetch(sessionsUrl, { signal: abortController.signal }),
    ])

    // Summary
    if (!r1.ok) throw new Error(`Summary API ${r1.status}`)
    const res = await r1.json()
    const rawData = Array.isArray(res) ? res : res.items || []

    // Merge by normalized process name to handle inconsistent .exe suffixes
    const mergedData = mergeByProcessName(rawData, (item, acc) => {
      acc.totalSeconds += item.totalSeconds
      acc.switchCount += item.switchCount
      if (item.adjustedSwitchCount !== undefined) {
        acc.adjustedSwitchCount = (acc.adjustedSwitchCount || 0) + item.adjustedSwitchCount
      }
    })

    // Sort after merge — merging can change totalSeconds and disrupt backend order
    mergedData.sort((a, b) => b.totalSeconds - a.totalSeconds)
    data.value = mergedData
    totalSleepSeconds.value = Array.isArray(res) ? 0 : res.totalSleepSeconds || 0

    // Timeline for visualization
    if (!r2.ok) throw new Error(`Timeline API ${r2.status}`)
    const timelineRes = await r2.json()
    const timelineData = timelineRes.data || timelineRes
    const timelineTotal = timelineRes.total || timelineData.length
    const sampled = timelineRes.sampled === true
    console.log('Timeline API returned', timelineData.length, 'records (total available:', timelineTotal, ')', sampled ? '(server-sampled)' : '')

    if (!sampled && timelineTotal > timelineData.length) {
      console.warn(`⚠️ Timeline data truncated: showing ${timelineData.length} of ${timelineTotal} records`)
    }

    // Systematic time-based sampling: keep every Nth point sorted by time,
    // so the chart spans the full range evenly instead of clustering on long-duration items.
    // Wide ranges are already sampled server-side (sampled=true) — skip re-sampling.
    let sampledData = timelineData
    const MAX_POINTS = 8000

    if (!sampled && timelineData.length > MAX_POINTS) {
      // Sort chronologically first
      const sorted = [...timelineData].sort((a, b) => new Date(a.timestamp) - new Date(b.timestamp))
      const step = Math.ceil(sorted.length / MAX_POINTS)
      sampledData = sorted.filter((_, i) => i % step === 0)
      console.log(`📊 Systematic sampling: ${sampledData.length} records from ${timelineData.length} (every ${step}th point)`)
    }

    timeline.value = sampledData
    console.log('✅ timeline.value set to:', timeline.value.length, 'records')

    // System events (sleep/shutdown/idle periods)
    if (r3.ok) {
      systemEvents.value = await r3.json()
      console.log('System events:', systemEvents.value.length, 'events')
    } else {
      systemEvents.value = []
    }

    // Window sessions (for background running apps)
    if (r4.ok) {
      windowSessions.value = await r4.json()
      console.log('Window sessions:', windowSessions.value.length)
    } else {
      windowSessions.value = []
    }

    loading.value = false
    // Only render if no newer loadData() call has started
    if (myLoadId !== loadId) {
      console.log(`Stale loadData call #${myLoadId} — current is #${loadId}, skipping render`)
      return
    }
    await nextTick()
    await renderTimeline(myLoadId)
  } catch (e) {
    // If this isn't the latest call, don't show the error
    if (myLoadId !== loadId) return
    console.error('Load error:', e)
    error.value = t('history.error.loadDataFailed', { message: e.message })
    loading.value = false
  }
}

let clearTimer = null

function onTimelineMouseOver(params) {
  if (params.seriesName !== 'windows') return
  const proc = params.data && params.data.processName
  if (!proc) return
  if (clearTimer) {
    clearTimeout(clearTimer)
    clearTimer = null
  }
  if (hoveredProcess.value === proc) return
  hoveredProcess.value = proc
  paintDimmer()
}

function onTimelineMouseOut(params) {
  // Leaving a bar: seriesName is set. Leaving all elements (blank chart area):
  // ECharts emits a global mouseout without seriesName — clear hover then too.
  if (params.seriesName && params.seriesName !== 'windows') return
  if (clearTimer) return
  // 50ms buffer: a quick move to another bar cancels this and switches
  // directly, so the dim/restore flicker is skipped.
  clearTimer = setTimeout(() => {
    clearTimer = null
    hoveredProcess.value = null
    paintDimmer()
  }, 50)
}

function onTimelineMouseLeave() {
  if (clearTimer) {
    clearTimeout(clearTimer)
    clearTimer = null
  }
  hoveredProcess.value = null
  paintDimmer()
}

// Precompute pixel rects for every bar (focused + background lines) once per
// render/resize, so hover never calls convertToPixel again. Bars are grouped
// by row center Y — focused bars and background lines of the same chart row
// map to the same center (both use the same rowIdx), so exact equality is
// reliable. The rects match the shapes drawn by renderBar exactly.
function buildAllBarsPx() {
  allBarsPx = []
  const chart = timelineChart
  if (!chart || chart.isDisposed()) return
  const ts = focusedWindowsData[0] && focusedWindowsData[0]._ts
  if (ts === undefined || ts === null) return
  // Use the bar height recorded by renderBar (api.size * 0.6) — an exact
  // match with what is drawn, for any number of rows. Large datasets render
  // progressively, so right after setOption renderBar may not have run yet;
  // then derive the row spacing from convertToPixel (the same geometry
  // api.size uses) and, only for the single-row case where spacing is
  // unmeasurable (row 0 and row 1 are the same pixel), fall back to a
  // container fraction. The 'finished' listener rebuilds once renderBar has
  // recorded the true height.
  const px = { xAxisIndex: 0, yAxisIndex: 0 }
  let barH = lastFocusedBarH
  if (!(barH > 0) || !Number.isFinite(barH)) {
    if (lastRowCount > 1) {
      const p0 = chart.convertToPixel(px, [ts, 0])
      const p1 = chart.convertToPixel(px, [ts, 1])
      const rowH = p0 && p1 ? p1[1] - p0[1] : NaN
      if (rowH > 0 && Number.isFinite(rowH)) barH = rowH * 0.6
    }
  }
  if (!(barH > 0) || !Number.isFinite(barH)) {
    barH = ((timelineChartRef.value?.offsetHeight || 440) / 20) * 0.6
  }
  lastUsedBarH = barH
  const rows = new Map()
  const push = (proc, x, w, centerY, focused) => {
    let row = rows.get(centerY)
    if (!row) {
      row = { centerY, barH, bars: [] }
      rows.set(centerY, row)
    }
    row.bars.push({ proc, x, w, focused })
  }
  for (const item of focusedWindowsData) {
    const a = chart.convertToPixel(px, [item._ts, item._rowIdx])
    const b = chart.convertToPixel(px, [item._end, item._rowIdx])
    if (!a || !b) continue
    push(item.processName, a[0], Math.max(b[0] - a[0], 2), a[1], true)
  }
  for (const item of backgroundWindowsData) {
    const a = chart.convertToPixel(px, [item.value[1], item.value[0]])
    const b = chart.convertToPixel(px, [item.value[2], item.value[0]])
    if (!a || !b) continue
    push(item._bgSession.processName, a[0], Math.max(b[0] - a[0], 2), a[1], false)
  }
  allBarsPx = [...rows.values()]
}

// Merge overlapping/touching [x0, x1] intervals into disjoint ones.
function mergeIntervals(ints) {
  if (ints.length === 0) return []
  ints.sort((a, b) => a[0] - b[0])
  const out = []
  let cur = ints[0]
  for (let i = 1; i < ints.length; i++) {
    const iv = ints[i]
    if (iv[0] <= cur[1]) {
      if (iv[1] > cur[1]) cur[1] = iv[1]
    } else {
      out.push(cur)
      cur = iv
    }
  }
  out.push(cur)
  return out
}

// Paint the dim layer: background-color rectangles over every bar except the
// hovered process's. Clearing it (no hover) restores the full-color chart.
// Runs on hover transitions only, never per mousemove. Intervals are unioned
// before painting so every pixel is painted exactly once — per-bar rects
// stacked alpha on overlaps (0.7 twice → 0.91) and left dark seams where a
// background line crossed a focused bar. Focused bars (barH-tall bands) and
// background lines (1px at the center) are painted separately so a band never
// dims empty space above/below a thin line.
function paintDimmer() {
  const canvas = hoverDimmerRef.value
  if (!canvas) return
  const el = timelineChartRef.value
  const dpr = window.devicePixelRatio || 1
  const cw = el ? el.offsetWidth : canvas.offsetWidth
  const ch = el ? el.offsetHeight : canvas.offsetHeight
  if (canvas.width !== cw * dpr || canvas.height !== ch * dpr) {
    canvas.width = cw * dpr
    canvas.height = ch * dpr
  }
  const ctx = canvas.getContext('2d')
  ctx.setTransform(dpr, 0, 0, dpr, 0, 0)
  ctx.clearRect(0, 0, cw, ch)
  const proc = hoveredProcess.value
  if (!proc || allBarsPx.length === 0) return
  ctx.fillStyle = withAlpha(surfaceCard, 0.7)
  for (const row of allBarsPx) {
    const focusedInts = []
    const bgInts = []
    for (const bar of row.bars) {
      if (bar.proc === proc) continue
      const iv = [bar.x, bar.x + bar.w]
      if (bar.focused) focusedInts.push(iv)
      else bgInts.push(iv)
    }
    const focused = mergeIntervals(focusedInts)
    // Focused bars: barH-tall band centered on the row.
    const y = row.centerY - row.barH / 2
    for (const [x0, x1] of focused) ctx.fillRect(x0, y, x1 - x0, row.barH)
    // Background lines: 1px at the center, clipped out of the focused band so
    // the crossing region is painted only once.
    for (const [b0, b1] of mergeIntervals(bgInts)) {
      let start = b0
      for (const [f0, f1] of focused) {
        if (f1 <= start) continue
        if (f0 >= b1) break
        if (f0 > start) ctx.fillRect(start, row.centerY, f0 - start, 1)
        start = Math.max(start, f1)
        if (start >= b1) break
      }
      if (start < b1) ctx.fillRect(start, row.centerY, b1 - start, 1)
    }
  }
}

// Append alpha to a CSS color string (#rgb/#rrggbb/rgb(...)/rgba(...)).
function withAlpha(color, alpha) {
  if (!color) return `rgba(128, 128, 128, ${alpha})`
  const m = color.trim().match(/^#([0-9a-f]{3}|[0-9a-f]{6})$/i)
  if (m) {
    const hex = m[1].length === 3 ? m[1].split('').map(c => c + c).join('') : m[1]
    const n = parseInt(hex, 16)
    return `rgba(${(n >> 16) & 255}, ${(n >> 8) & 255}, ${n & 255}, ${alpha})`
  }
  const rgb = color.match(/(\d+)\s*,\s*(\d+)\s*,\s*(\d+)/)
  if (rgb) return `rgba(${rgb[1]}, ${rgb[2]}, ${rgb[3]}, ${alpha})`
  return `rgba(128, 128, 128, ${alpha})`
}

async function renderTimeline(myLoadId) {
  console.log('=== renderTimeline called ===')
  console.log('timelineChartRef.value:', !!timelineChartRef.value)
  console.log('timeline.value.length:', timeline.value.length)

  if (!timelineChartRef.value) {
    // The chart div can be missing when the picker briefly showed an invalid
    // range (the easter egg replaces the div) while this load was in flight.
    // Wait for it to reappear instead of giving up — a permanent bail leaves
    // the chart area dead (no canvases, no adaptive height) until the next
    // range change.
    for (let i = 0; i < 40 && !timelineChartRef.value; i++) {
      await new Promise(r => setTimeout(r, 50))
    }
    if (!timelineChartRef.value) {
      console.warn('Timeline chart ref not ready')
      return
    }
  }

  if (!timeline.value.length) {
    console.warn('No timeline data to render')
    if (timelineChart && !timelineChart.isDisposed()) {
      timelineChart.clear()
    }
    return
  }

  // Bail if a newer load has started
  if (myLoadId !== undefined && myLoadId !== loadId) {
    console.log(`Stale renderTimeline call #${myLoadId} — current is #${loadId}, skipping`)
    return
  }

  console.log('Rendering timeline with', timeline.value.length, 'records')

  // Step 1: Group by day (4 AM boundary), compute top 20 per day
  const DAY_START = 4 * 60 * 60 * 1000
  const getDayKey = (tsMs) => {
    const d = new Date(tsMs - DAY_START)
    return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
  }

  const dayStats = {}
  timeline.value.forEach(item => {
    const tsMs = parseUtcTs(item.timestamp).getTime()
    const dk = getDayKey(tsMs)
    if (!dayStats[dk]) dayStats[dk] = {}
    dayStats[dk][item.processName] = (dayStats[dk][item.processName] || 0) + item.durationSeconds
  })

  // Step 2: Top 20 per day, build day→process set and process→days map
  const dayTopSet = {}
  const processDays = {}
  const allProcs = new Set()

  for (const [dk, stats] of Object.entries(dayStats)) {
    const top20 = Object.entries(stats).sort((a, b) => b[1] - a[1]).slice(0, 20).map(e => e[0])
    dayTopSet[dk] = new Set(top20)
    for (const p of top20) {
      allProcs.add(p)
      if (!processDays[p]) processDays[p] = new Set()
      processDays[p].add(dk)
    }
  }

  // Step 3: Greedy row assignment — most frequent first, minimize rows
  const sortedProcs = [...allProcs].sort((a, b) => processDays[b].size - processDays[a].size)
  const rowProcs = []
  const processRow = new Map()

  for (const proc of sortedProcs) {
    const procDaySet = processDays[proc]
    let row = -1
    for (let r = 0; r < rowProcs.length; r++) {
      let conflict = false
      for (const existing of rowProcs[r]) {
        for (const d of procDaySet) {
          if (processDays[existing].has(d)) { conflict = true; break }
        }
        if (conflict) break
      }
      if (!conflict) { row = r; break }
    }
    if (row === -1) {
      row = rowProcs.length
      rowProcs.push([])
    }
    rowProcs[row].push(proc)
    processRow.set(proc, row)
  }

  // Row labels (axis labels hidden — used only for ECharts category mapping)
  const processList = rowProcs.map((_, i) => String(i + 1))
  const allProcessNames = [...allProcs]

  console.log(`${allProcessNames.length} processes across ${Object.keys(dayStats).length} days, ${rowProcs.length} rows`)

  // Chart height adapts to the row count: a fixed 440px container would
  // stretch a few process rows into giant bars. The hover overlay follows
  // via inset: 0, so it adapts automatically. Clamp to the original 440px max.
  const ROW_H = 40
  const GRID_PAD = 80 // grid top + bottom
  timelineChartRef.value.style.height =
    `${Math.max(120, Math.min(440, rowProcs.length * ROW_H + GRID_PAD))}px`

  // Step 3.5: Fetch colors for all processes (time-aware — use range start date).
  // Cached by process+atTime so re-renders (theme toggle, resize) don't re-fetch.
  const atTime = startDate.value.toISOString()
  const colorPromises = allProcessNames.map(async (processName) => {
    const cacheKey = `${processName}|${atTime}`
    const cached = colorCache.get(cacheKey)
    if (cached) return cached
    try {
      const response = await fetch(`${apiBase}/api/icons/${encodeURIComponent(processName)}?at=${encodeURIComponent(atTime)}`, { signal: abortController.signal })
      if (response.ok) {
        const iconData = await response.json()
        const color = iconData.colorPrimary || '#6B7FD7'
        colorCache.set(cacheKey, color)
        return color
      }
    } catch (e) {
      console.warn(`Failed to fetch color for ${processName}:`, e)
    }
    colorCache.set(cacheKey, '#6B7FD7')
    return '#6B7FD7'
  })

  const processColors = await Promise.all(colorPromises)
  const colorMap = {}
  allProcessNames.forEach((name, idx) => {
    colorMap[name] = processColors[idx]
  })

  // Step 4: Pre-parse timestamps, filter by per-day top-20
  const parsedTimeline = []
  const processSet = new Set(allProcessNames)
  for (const item of timeline.value) {
    if (!processSet.has(item.processName)) continue
    const tsMs = parseUtcTs(item.timestamp).getTime()
    const dk = getDayKey(tsMs)
    const topSet = dayTopSet[dk]
    if (!topSet || !topSet.has(item.processName)) continue
    parsedTimeline.push({
      ...item,
      _ts: tsMs,
    })
  }

  console.log('Rendering', parsedTimeline.length, 'records (filtered by daily top-20)')

  // Step 3.5: Build sleep period map for quick lookup
  const sleepPeriods = systemEvents.value.map(e => ({
    start: parseUtcTs(e.timestamp).getTime(),
    end: parseUtcTs(e.timestamp).getTime() + e.durationSeconds * 1000
  }))

  // Sorted sleep periods enable early-exit in the inner check
  sleepPeriods.sort((a, b) => a.start - b.start)

  // Early-exit scan over sorted sleep periods
  function isDuringSleep(tsMs) {
    for (const p of sleepPeriods) {
      if (tsMs < p.start) return false  // sorted → no later period can match
      if (tsMs < p.end) return true
    }
    return false
  }

  console.log('Sleep periods:', sleepPeriods.length)

  // Calculate X-axis range from selected dates
  const xAxisMin = startDate.value.getTime()
  const xAxisMax = endDate.value.getTime()
  const rangeInDays = (xAxisMax - xAxisMin) / (1000 * 60 * 60 * 24)

  // Build idle areas from backend Idle SystemEvents (new data, precise)
  const backendIdleAreas = systemEvents.value
    .filter(e => e.eventType === 'Idle' && e.durationSeconds >= 10)
    .map(e => {
      const start = parseUtcTs(e.timestamp).getTime()
      const end = start + e.durationSeconds * 1000
      if (end <= xAxisMin || start >= xAxisMax) return null
      return {
        value: [Math.max(start, xAxisMin), Math.min(end, xAxisMax), 0],
        durationMs: e.durationSeconds * 1000,
        areaType: 'idle',
      }
    })
    .filter(Boolean)

  console.log('Idle events from backend:', backendIdleAreas.length)

  // Step 3.6: Detect idle periods from gaps in the FULL timeline data (before the per-day top-20 filter).
  // Fallback for old data before backend Idle events existed.
  // Each entry has a start (timestamp) and end (timestamp + duration). A real idle gap
  // is when the END of one entry is far from the START of the next — NOT when two
  // consecutive starts are far apart (which is normal for long-duration entries like games).
  const IDLE_GAP_MS = 2 * 60 * 1000  // 2 minutes
  const MIN_IDLE_MS = 10 * 1000       // ignore sub-10s idle fragments

  const fullSorted = [...timeline.value]
    .map(item => ({
      start: parseUtcTs(item.timestamp).getTime(),
      end: parseUtcTs(item.timestamp).getTime() + (item.durationSeconds ?? item.duration ?? 0) * 1000,
    }))
    .filter(item => !isDuringSleep(item.start))
    .sort((a, b) => a.start - b.start)

  const gapIdleAreas = []
  if (fullSorted.length > 1) {
    for (let i = 1; i < fullSorted.length; i++) {
      const gap = fullSorted[i].start - fullSorted[i - 1].end
      if (gap >= IDLE_GAP_MS) {
        const idleStart = fullSorted[i - 1].end
        const idleEnd = fullSorted[i].start
        if (idleEnd - idleStart >= MIN_IDLE_MS && idleEnd > xAxisMin && idleStart < xAxisMax) {
          gapIdleAreas.push({
            value: [Math.max(idleStart, xAxisMin), Math.min(idleEnd, xAxisMax), 0],
            durationMs: idleEnd - idleStart,
            areaType: 'idle',
          })
        }
      }
    }
  }

  console.log('Idle areas from gaps (fallback):', gapIdleAreas.length)

  // Merge both sources: backend events + gap fallback for old data without Idle events
  let idleAreas = [...backendIdleAreas, ...gapIdleAreas]

  // Step 3.7: Subtract sleep/shutdown periods from idle areas so idle never overlaps with sleep.
  // Both backend Idle events and gap-detected idle can span into sleep when the
  // system auto-suspends while the user is away. Sleep takes precedence in the display.
  {
    const sleepPeriods = systemEvents.value
      .filter(e => (e.eventType === 'Sleep' || e.eventType === 'Shutdown') && e.durationSeconds > 3)
      .map(e => ({
        start: parseUtcTs(e.timestamp).getTime(),
        end: parseUtcTs(e.timestamp).getTime() + e.durationSeconds * 1000,
      }))
      .filter(s => s.end > xAxisMin && s.start < xAxisMax)
      .sort((a, b) => a.start - b.start)

    if (sleepPeriods.length > 0) {
      const clipped = []
      for (const idle of idleAreas) {
        let segStart = idle.value[0]
        const segEnd = idle.value[1]
        for (const s of sleepPeriods) {
          if (s.end <= segStart) continue       // sleep before segment
          if (s.start >= segEnd) break           // sleep after segment (sorted, no more overlap)
          if (s.start > segStart) {
            // Non-overlapping portion before the sleep
            clipped.push({
              value: [segStart, s.start, 0],
              durationMs: s.start - segStart,
              areaType: 'idle',
            })
          }
          segStart = Math.max(segStart, s.end)   // advance past this sleep
          if (segStart >= segEnd) break          // nothing left
        }
        if (segStart < segEnd) {
          // Remaining portion after all sleep periods
          clipped.push({
            value: [segStart, segEnd, 0],
            durationMs: segEnd - segStart,
            areaType: 'idle',
          })
        }
      }
      idleAreas = clipped
    }
  }

  // Build focusedWindows + activityPeriods in a single pass
  const focusedWindows = []
  const activityPeriods = []

  for (const item of parsedTimeline) {
    const rowIdx = processRow.get(item.processName)
    if (rowIdx === undefined) continue

    if (isDuringSleep(item._ts)) continue

    const end = item._ts + item.durationSeconds * 1000
    const color = colorMap[item.processName]

    const focusStyle = { color, borderColor: color, borderWidth: 0 }
    focusedWindows.push({
      name: item.processName,
      value: [rowIdx, item._ts, end, item.durationSeconds],
      itemStyle: focusStyle,
      processName: item.processName,
      windowTitle: item.windowTitle,
      timestamp: item.timestamp,
      durationSeconds: item.durationSeconds,
      duringsSleep: false,
      itemColor: color,
      _ts: item._ts,
      _end: end,
      _rowIdx: rowIdx,
    })

    activityPeriods.push({ start: item._ts, end })
  }

  console.log('Created', focusedWindows.length, 'focused window chart items')

  // Merge overlapping activity periods and sort
  activityPeriods.sort((a, b) => a.start - b.start)
  const mergedActivity = []
  for (const period of activityPeriods) {
    const last = mergedActivity[mergedActivity.length - 1]
    if (last && period.start <= last.end) {
      if (period.end > last.end) last.end = period.end
    } else {
      mergedActivity.push({ start: period.start, end: period.end })
    }
  }

  console.log('Merged activity periods:', mergedActivity.length)

  // Step 4: Add background running windows (thin lines)
  const backgroundWindows = []
  if (mergedActivity.length > 0) {
    const MAX_BACKGROUND = 3000
    let bgSkipped = 0
    const actMin = mergedActivity[0].start
    const actMax = mergedActivity[mergedActivity.length - 1].end

    for (const session of windowSessions.value) {
      if (backgroundWindows.length >= MAX_BACKGROUND) break

      const rowIdx = processRow.get(session.processName)
      if (rowIdx === undefined) continue

      const color = colorMap[session.processName]
      const sessionOpenTime = parseUtcTs(session.openTime).getTime()
      const sessionCloseTime = session.closeTime ? parseUtcTs(session.closeTime).getTime() : xAxisMax

      // Quick-reject: session entirely outside activity range
      if (sessionCloseTime <= actMin || sessionOpenTime >= actMax) continue

      // Binary-search the first activity period whose end > sessionOpenTime
      let lo = 0, hi = mergedActivity.length
      while (lo < hi) {
        const mid = (lo + hi) >> 1
        if (mergedActivity[mid].end <= sessionOpenTime) lo = mid + 1
        else hi = mid
      }

      for (let i = lo; i < mergedActivity.length; i++) {
        if (backgroundWindows.length >= MAX_BACKGROUND) break
        const ap = mergedActivity[i]
        if (ap.start >= sessionCloseTime) break  // past the session

        const segStart = Math.max(sessionOpenTime, ap.start)
        const segEnd = Math.min(sessionCloseTime, ap.end)
        if (segStart >= segEnd) continue

        // Quick sleep check with early exit (sleep periods are sorted)
        let inSleep = false
        for (const s of sleepPeriods) {
          if (segStart < s.start) break
          if (segStart < s.end) { inSleep = true; break }
        }
        if (inSleep) continue

        if (rangeInDays > 1 && (segEnd - segStart) < 60 * 1000) {
          bgSkipped++
          continue
        }

        // Store raw data for shared tooltip (no per-item closure)
        const bgStyle = {
          color: 'transparent',
          borderColor: color,
          borderWidth: 1,
          opacity: 0.3,
        }
        backgroundWindows.push({
          name: session.processName,
          value: [rowIdx, segStart, segEnd, 0],
          itemStyle: bgStyle,
          _bgSession: session,
          itemColor: color,
        })
      }
    }

    if (bgSkipped > 0) console.log(`Skipped ${bgSkipped} short background segments (< 60s)`)
  }

  console.log('Created', backgroundWindows.length, 'background window chart items')

  // Combine background windows (rendered first, behind) and focused windows (on top)
  const allWindows = [...backgroundWindows, ...focusedWindows]
  // Keep bars for hover-position computation (the chart isn't init'ed until
  // below, so convertToPixel can't run yet — buildAllBarsPx() runs after
  // setOption); reset hover state.
  focusedWindowsData = focusedWindows
  backgroundWindowsData = backgroundWindows
  lastRowCount = rowProcs.length
  hoveredProcess.value = null

  // Build sleep/shutdown area overlays from backend events (exclude Idle)
  const sleepAreas = systemEvents.value
    .filter(e => (e.eventType === 'Sleep' || e.eventType === 'Shutdown') && e.durationSeconds > 3)
    .map(e => {
      const start = parseUtcTs(e.timestamp).getTime()
      const end = start + e.durationSeconds * 1000
      // Skip if entirely outside visible range
      if (end <= xAxisMin || start >= xAxisMax) return null
      return {
        value: [Math.max(start, xAxisMin), Math.min(end, xAxisMax), 1],
        eventType: e.eventType,
        timestamp: e.timestamp,
        durationSeconds: e.durationSeconds,
        areaType: 'sleep',
      }
    })
    .filter(Boolean)

  // Dynamic time format based on range
  let timeFormatter
  if (rangeInDays <= 2) {
    // 2 days or less: show time only (HH:mm)
    timeFormatter = '{HH}:{mm}'
  } else {
    // More than 2 days: show date and time (MM-DD HH:mm)
    timeFormatter = '{MM}-{dd} {HH}:{mm}'
  }

  // Dynamic interval for axis labels (to avoid crowding)
  let axisLabelInterval
  if (rangeInDays <= 1) {
    axisLabelInterval = 'auto' // hourly or so
  } else if (rangeInDays <= 7) {
    axisLabelInterval = 'auto' // every few hours
  } else {
    axisLabelInterval = 'auto' // daily
  }

  console.log('X-axis range (query dates):', {
    from: startDate.value.toISOString(),
    to: endDate.value.toISOString(),
    rangeInDays: rangeInDays.toFixed(1),
    timeFormatter,
    xAxisMin: new Date(xAxisMin).toISOString(),
    xAxisMax: new Date(xAxisMax).toISOString()
  })

  // Debug: log first item
  if (focusedWindows.length > 0) {
    console.log('First chart item:', {
      name: focusedWindows[0].name,
      value: focusedWindows[0].value,
      itemStyle: focusedWindows[0].itemStyle
    })
  }

  if (!timelineChart) {
    timelineChart = echarts.init(timelineChartRef.value)
    timelineChart.on('mouseover', onTimelineMouseOver)
    timelineChart.on('mouseout', onTimelineMouseOut)
    // Large datasets render progressively (async): renderBar records the true
    // bar height only once rendering starts, so buildAllBarsPx right after
    // setOption can guess wrong. Rebuild the overlay once the render finishes
    // so the dimmer bands always match the painted bars.
    timelineChart.on('finished', () => {
      if (lastFocusedBarH > 0 && lastFocusedBarH !== lastUsedBarH) {
        buildAllBarsPx()
        if (hoveredProcess.value) paintDimmer()
      }
    })
    // DOM mouseleave is the reliable way to clear hover state — ECharts'
    // series mouseout may not fire when the cursor leaves the canvas.
    timelineChartRef.value.addEventListener('mouseleave', onTimelineMouseLeave)
  }

  // Bail if a newer load started while we were working
  if (myLoadId !== undefined && myLoadId !== loadId) return

  // Get computed CSS colors (ECharts renders on Canvas — CSS variables don't work)
  const computedStyle = getComputedStyle(document.documentElement)
  const tooltipBg = computedStyle.getPropertyValue('--surface-card').trim()
  surfaceCard = tooltipBg
  const tooltipBorder = computedStyle.getPropertyValue('--primary-color').trim()
  const tooltipText = computedStyle.getPropertyValue('--text-color').trim()
  const secondaryColor = computedStyle.getPropertyValue('--secondary-color').trim()
  const borderColor = computedStyle.getPropertyValue('--border-color').trim()
  const surface200 = computedStyle.getPropertyValue('--surface-200').trim()

  function renderIdleRect(params, api) {
    const startX = api.coord([api.value(0), 0])[0]
    const endX = api.coord([api.value(1), 0])[0]
    const rowH = api.size([0, 1])[1]
    const centerY = api.coord([0, 0])[1]

    return {
      type: 'rect',
      shape: {
        x: startX,
        y: centerY - rowH * 0.4,
        width: Math.max(endX - startX, 2),
        height: rowH * 0.8,
      },
      style: {
        fill: 'rgba(128, 128, 128, 0.15)',
        stroke: secondaryColor,
        lineWidth: 2,
        lineDash: [],
      },
    }
  }

  function renderSleepRect(params, api) {
    const startX = api.coord([api.value(0), 0])[0]
    const endX = api.coord([api.value(1), 0])[0]
    const rowH = api.size([0, 1])[1]
    const centerY = api.coord([0, 0])[1]

    return {
      type: 'rect',
      shape: {
        x: startX,
        y: centerY - rowH * 0.4,
        width: Math.max(endX - startX, 2),
        height: rowH * 0.8,
      },
      style: {
        fill: 'rgba(128, 128, 128, 0.12)',
        stroke: secondaryColor,
        lineWidth: 2,
        lineDash: [8, 4],
      },
    }
  }

  const option = {
    animation: false,
    // progressive is disabled on purpose: with ECharts 6.1, custom-series
    // chunks advance via the animation timeline, so animation: false leaves
    // large series permanently unpainted. Bars are cheap to draw.
    progressive: 0,
    progressiveThreshold: 500,
    grid: [
      {
        left: 20,
        right: 40,
        top: 40,
        bottom: 40,
        containLabel: true,
      },
      {
        left: 20,
        right: 40,
        height: 32,
        bottom: 4,
        containLabel: false,
      },
    ],
    xAxis: [
      {
        type: 'time',
        gridIndex: 0,
        min: xAxisMin,
        max: xAxisMax,
        axisLabel: {
          color: tooltipText,
          fontWeight: 600,
          formatter: timeFormatter,
          rotate: rangeInDays > 3 ? 15 : 0,
        },
        axisLine: {
          lineStyle: { color: borderColor, width: 2 },
        },
        splitLine: {
          lineStyle: { type: 'dashed', color: surface200 },
        },
      },
      {
        type: 'time',
        gridIndex: 1,
        min: xAxisMin,
        max: xAxisMax,
        axisLabel: { show: false },
        axisLine: { show: false },
        axisTick: { show: false },
        splitLine: { show: false },
      },
    ],
    yAxis: [
      {
        type: 'category',
        gridIndex: 0,
        data: processList,
        axisLabel: { show: false },
        axisLine: { show: false },
        axisTick: { show: false },
        splitLine: { show: false },
      },
      {
        type: 'category',
        gridIndex: 1,
        data: [''],
        axisLabel: { show: false },
        axisLine: { show: false },
        axisTick: { show: false },
        splitLine: { show: false },
      },
    ],
    series: [
      {
        name: 'idleAreas',
        type: 'custom',
        z: 0,
        xAxisIndex: 1,
        yAxisIndex: 1,
        renderItem: renderIdleRect,
        data: idleAreas,
        tooltip: {
          formatter: (params) => {
            const data = params.data
            if (!data) return ''
            const durMin = Math.floor(data.durationMs / 60000)
            const durHr = Math.floor(durMin / 60)
            const durStr = durHr > 0 ? `${durHr}h ${durMin % 60}m` : `${durMin}m`
            return `<div style="font-weight:600;margin-bottom:4px;color:${secondaryColor};">${t('history.status.idlePeriod')}</div>
                    <div style="margin-top:4px;color:var(--surface-400);">${t('common.duration')}: ${durStr}</div>`
          },
        },
      },
      {
        name: 'sleepAreas',
        type: 'custom',
        z: 0,
        xAxisIndex: 1,
        yAxisIndex: 1,
        renderItem: renderSleepRect,
        data: sleepAreas,
        tooltip: {
          formatter: (params) => {
            const data = params.data
            if (!data) return ''
            const typeName = data.eventType === 'Sleep'
              ? t('history.status.sleepEvent')
              : t('history.status.shutdownEvent')
            return `<div style="font-weight:600;margin-bottom:4px;color:${secondaryColor};">${typeName}</div>
                    <div style="color:var(--text-color);">${toLocalTime(data.timestamp)}</div>
                    <div style="margin-top:4px;color:var(--surface-400);">${t('common.duration')}: ${fmtShortDur(data.durationSeconds)}</div>`
          },
        },
      },
      {
        name: 'windows',
        type: 'custom',
        xAxisIndex: 0,
        yAxisIndex: 0,
        renderItem: renderBar,
        encode: {
          x: [1, 2],
          y: 0,
        },
        data: allWindows,
      },
    ],
    tooltip: {
      backgroundColor: tooltipBg,
      borderColor: tooltipBorder,
      borderWidth: 2,
      textStyle: {
        color: tooltipText,
        fontFamily: 'Ubuntu Mono',
      },
      formatter: (params) => {
        const data = params.data
        if (!data) return ''

        if (data._bgSession) {
          const s = data._bgSession
          const status = s.closeTime ? t('history.status.closed') : t('history.status.running')
          return `<div style="font-weight:600;margin-bottom:4px;font-family:'Ubuntu Mono';color:${data.itemColor};">${s.processName}</div>
                  <div style="font-size:0.9em;">${s.windowTitle}</div>
                  <div style="margin-top:4px;color:var(--surface-500);">
                    ${toLocalTime(s.openTime)} - ${s.closeTime ? toLocalTime(s.closeTime) : t('history.status.now')}
                  </div>
                  <div style="font-size:0.85em;color:var(--surface-400);">${t('history.status.background')} · ${status}</div>`
        }

        return `<div style="font-weight:600;margin-bottom:4px;font-family:'Ubuntu Mono';color:${data.itemColor};">${data.processName}</div>
                <div style="font-size:0.9em;">${data.windowTitle}</div>
                <div style="margin-top:4px;color:var(--primary-color);">
                  ${toLocalTime(data.timestamp)} · ${fmtShortDur(data.durationSeconds)}
                </div>`
      }
    },
  }

  console.log(`Setting option with ${focusedWindows.length} focused + ${backgroundWindows.length} bg = ${allWindows.length} total points`)

  // Bail if stale
  if (myLoadId !== undefined && myLoadId !== loadId) return

  // Reset the renderBar-recorded bar height before the render: if this data
  // has no focused bars, renderBar won't run and the stale value from the
  // previous render would mis-size the dimmer bands (row counts differ).
  lastFocusedBarH = 0
  timelineChart.setOption(option, true)
  // Chart is now laid out — precompute bar pixel rects for the hover layer
  buildAllBarsPx()
  paintDimmer()

  console.log('Timeline rendered successfully')
}

function renderBar(params, api) {
  const categoryIndex = api.value(0)
  const start = api.coord([api.value(1), categoryIndex])
  const end = api.coord([api.value(2), categoryIndex])
  const durationSeconds = api.value(3)

  // Check if this is a background window (durationSeconds = 0) or focused window
  const isBackground = durationSeconds === 0

  let rectShape
  if (isBackground) {
    // Render as a thin horizontal line in the middle
    rectShape = {
      x: start[0],
      y: start[1], // Middle of the row
      width: Math.max(end[0] - start[0], 2),
      height: 1, // 1px thin line
    }
  } else {
    // Render as a regular bar (focused window)
    const height = api.size([0, 1])[1] * 0.6
    // Remember it so the hover dimmer paints rects of exactly this height
    // (row counts vary, so api.size is the only correct source).
    lastFocusedBarH = height
    rectShape = {
      x: start[0],
      y: start[1] - height / 2,
      width: Math.max(end[0] - start[0], 2),
      height: height,
    }
  }

  return {
    type: 'rect',
    shape: rectShape,
    style: api.style(),
  }
}
</script>

<style lang="scss" scoped>
.history-page {
  width: 100%;
}

.error-banner {
  padding: 12px 16px;
  background: var(--danger-color);
  color: white;
  border: 2px solid var(--border-color);
  display: flex;
  justify-content: space-between;
  align-items: center;
  font-weight: 600;
}

.close-btn {
  background: transparent;
  border: none;
  color: white;
  font-size: 1.2rem;
  cursor: pointer;
  padding: 0 8px;
}

.timeline-card {
  min-height: 200px;
  border: 3px solid color-mix(in srgb, var(--text-color) 80%, transparent) !important;
  box-shadow: 0 0 0 transparent;
  transition: transform 0.12s ease-out, box-shadow 0.15s ease-out;

  &:hover {
    border-color: var(--text-color);
    transform: translate(-2px, -2px);
    box-shadow: 4px 4px 0 color-mix(in srgb, var(--primary-color) 80%, transparent);
  }
}

.easter-egg-chart {
  display: flex;
  align-items: center;
  justify-content: center;
  min-height: 400px;
  border: 2px dashed var(--danger-color, #E76F51);
  background: var(--surface-ground, #f8f9fa);
  animation: eggFadeIn 0.4s ease-out;
}

.easter-egg-chart-text {
  font-size: 1.6rem;
  font-weight: 700;
  color: var(--danger-color, #E76F51);
  text-align: center;
  padding: 32px;
  animation: eggPulse 2s ease-in-out infinite;
}

@keyframes eggFadeIn {
  from { opacity: 0; transform: scale(0.95); }
  to { opacity: 1; transform: scale(1); }
}

@keyframes eggPulse {
  0%, 100% { opacity: 1; }
  50% { opacity: 0.6; }
}

.card-title {
  font-size: 1.1rem;
  font-weight: 600;
  letter-spacing: 0.5px;
  margin-bottom: 16px;
  color: var(--text-color);
}

// Positioned wrapper: the overlay layers are siblings of the chart container
// (echarts.init() clears the container's children), absolute within this box,
// sharing the chart's origin so convertToPixel coords map directly.
.timeline-chart-wrap {
  position: relative;
  margin-bottom: 16px;
}

.timeline-chart {
  width: 100%;
  height: 440px;
  background: var(--surface-card);
}

// Hover overlay: a pointer-events: none canvas so the chart canvas keeps
// receiving mouse events. Painted by paintDimmer() — background-color rects
// over every bar except the hovered process's. Shares the wrapper's
// coordinate space (inset: 0), so convertToPixel coords map directly.
.hover-dimmer {
  position: absolute;
  inset: 0;
  width: 100%;
  height: 100%;
  opacity: 0;
  pointer-events: none;
  z-index: 5;
  transition: opacity 0.25s ease;

  &.visible {
    opacity: 1;
  }
}

.timeline-legend {
  display: flex;
  gap: 24px;
  justify-content: center;
  padding: 12px 0;
  border-top: 2px solid var(--surface-200);
}

.legend-item {
  display: flex;
  align-items: center;
  gap: 8px;
  font-weight: 600;
  font-size: 0.9rem;
  color: var(--text-color);
}

.legend-box {
  width: 24px;
  height: 12px;

  &.focus {
    background: var(--primary-color);
  }

  &.visible {
    background: var(--accent-color);
    height: 2px;
    opacity: 0.6;
  }

  &.idle {
    background: transparent;
    border: 2px solid var(--secondary-color);
    opacity: 0.7;
  }

  &.offline {
    background: transparent;
    border: 2px dashed var(--secondary-color);
    opacity: 0.7;
  }
}

:deep(.time-range-picker .wheel-frame:not(.frameless)) {
  border-color: color-mix(in srgb, var(--text-color) 80%, transparent);
  box-shadow: 0 0 0 transparent;
  transition:
    transform 0.12s ease-out,
    box-shadow 0.12s ease-out,
    border-color 0.2s 5s;

  &:hover {
    border-color: var(--text-color);
    transform: translate(-2px, -2px);
    box-shadow: 5px 5px 0 color-mix(in srgb, var(--primary-color) 80%, transparent);
    transition:
      transform 0.12s ease-out,
      box-shadow 0.12s ease-out,
      border-color 0s 0s;
  }

  &.scrolling {
    border-color: var(--text-color);
    box-shadow: 4px 4px 0 color-mix(in srgb, var(--primary-color) 80%, transparent);
    transition:
      transform 0.12s ease-out,
      box-shadow 0.12s ease-out,
      border-color 0s 0s;
  }
}

</style>
