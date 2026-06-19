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
      @change="handleTimeChange"
      class="mb-3"
    />

    <!-- Error -->
    <div v-if="error" class="error-banner mb-3">
      {{ error }}
      <button class="close-btn" @click="error = ''">✕</button>
    </div>

    <!-- Visual timeline -->
    <MemphisCard class="timeline-card mb-3">
      <h3 class="card-title">{{ t('history.card.visualTimeline') }}</h3>
      <!-- Easter egg: invalid time range -->
      <div v-if="!isTimeValid" class="easter-egg-chart">
        <div class="easter-egg-chart-text">{{ timeEasterEgg }}</div>
      </div>
      <template v-else>
        <MemphisSkeleton v-if="loading" :lines="6" />
        <div v-else>
          <div ref="timelineChartRef" class="timeline-chart"></div>
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
import * as echarts from 'echarts'
import MemphisCard from '../components/MemphisCard.vue'
import MemphisSkeleton from '../components/MemphisSkeleton.vue'
import TimeRangePicker from '../components/TimeRangePicker.vue'

const apiBase = inject('apiBase')
const { t } = useI18n()
const { isDark } = useTheme()

// Initialize with past 3 hours
const startDate = ref(new Date(Date.now() - 3 * 60 * 60 * 1000))
const endDate = ref(new Date())

const data = ref([])
const timeline = ref([])
const windowSessions = ref([])
const systemEvents = ref([])
const mergeSameProcess = ref(true)
const totalSleepSeconds = ref(0)
const loading = ref(true)
const error = ref('')
const earliestDate = ref(null)
const isTimeValid = ref(true)
const timeEasterEgg = ref('')

const timelineChartRef = ref(null)
let timelineChart = null

// Counter to cancel stale loadData calls — each call increments the ID;
// only the call whose ID still matches when it reaches renderTimeline() proceeds.
let loadId = 0

// Handle time change from picker
function handleTimeChange({ start, end, valid, easterEgg }) {
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
    const r = await fetch(`${apiBase}/api/settings`)
    if (r.ok) {
      const s = await r.json()
      mergeSameProcess.value = s.mergeSameProcessSwitches ?? true
    }
  } catch (e) {
    console.error('Failed to load settings:', e)
  }

  // Fetch oldest record to constrain time picker
  try {
    const r = await fetch(`${apiBase}/api/db/stats`)
    if (r.ok) {
      const stats = await r.json()
      if (stats.oldestRecord) {
        earliestDate.value = new Date(stats.oldestRecord.endsWith('Z') ? stats.oldestRecord : stats.oldestRecord + 'Z')
      }
    }
  } catch (e) {
    console.error('Failed to load DB stats:', e)
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

onUnmounted(() => {
  window.removeEventListener('resize', handleResize)

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
    }
  }, 200)
}


async function loadData() {
  const myLoadId = ++loadId
  loading.value = true
  error.value = ''

  // Dispose chart before skeleton replaces the DOM container,
  // so renderTimeline() will init a fresh instance on the new div.
  if (timelineChart && !timelineChart.isDisposed()) {
    timelineChart.dispose()
    timelineChart = null
  }

  try {
    const fromStr = toLocalDatetimeString(startDate.value)
    const toStr = toLocalDatetimeString(endDate.value)
    console.log('Loading data from', fromStr, 'to', toStr)

    // Load summary
    const summaryUrl = `${apiBase}/api/summary/range?from=${fromStr}&to=${toStr}`
    console.log('Fetching summary:', summaryUrl)
    const r1 = await fetch(summaryUrl)
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

    // Load timeline for visualization
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
    console.log('Fetching timeline:', timelineUrl, `(range: ${rangeInHours.toFixed(1)} hours / ${rangeInDays.toFixed(1)} days, limit: ${timelineLimit})`)
    const r2 = await fetch(timelineUrl)
    if (!r2.ok) throw new Error(`Timeline API ${r2.status}`)
    const timelineRes = await r2.json()
    const timelineData = timelineRes.data || timelineRes
    const timelineTotal = timelineRes.total || timelineData.length
    console.log('Timeline API returned', timelineData.length, 'records (total available:', timelineTotal, ')')

    if (timelineTotal > timelineData.length) {
      console.warn(`⚠️ Timeline data truncated: showing ${timelineData.length} of ${timelineTotal} records`)
      // Consider showing a warning to the user
    }

    // Systematic time-based sampling: keep every Nth point sorted by time,
    // so the chart spans the full range evenly instead of clustering on long-duration items.
    let sampledData = timelineData
    const MAX_POINTS = 8000

    if (timelineData.length > MAX_POINTS) {
      // Sort chronologically first
      const sorted = [...timelineData].sort((a, b) => new Date(a.timestamp) - new Date(b.timestamp))
      const step = Math.ceil(sorted.length / MAX_POINTS)
      sampledData = sorted.filter((_, i) => i % step === 0)
      console.log(`📊 Systematic sampling: ${sampledData.length} records from ${timelineData.length} (every ${step}th point)`)
    }

    timeline.value = sampledData
    console.log('✅ timeline.value set to:', timeline.value.length, 'records')

    // Load system events (sleep/shutdown/idle periods)
    const eventsUrl = `${apiBase}/api/system/events?from=${fromStr}&to=${toStr}`
    console.log('Fetching system events:', eventsUrl)
    const r3 = await fetch(eventsUrl)
    if (r3.ok) {
      systemEvents.value = await r3.json()
      console.log('System events:', systemEvents.value.length, 'events')
    } else {
      systemEvents.value = []
    }

    // Load window sessions (for background running apps)
    const sessionsUrl = `${apiBase}/api/windows/sessions?from=${fromStr}&to=${toStr}&limit=10000`
    console.log('Fetching window sessions:', sessionsUrl)
    const r4 = await fetch(sessionsUrl)
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

async function renderTimeline(myLoadId) {
  console.log('=== renderTimeline called ===')
  console.log('timelineChartRef.value:', !!timelineChartRef.value)
  console.log('timeline.value.length:', timeline.value.length)

  if (!timelineChartRef.value) {
    console.warn('Timeline chart ref not ready')
    return
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

  // Step 3.5: Fetch colors for all processes
  const colorPromises = allProcessNames.map(async (processName) => {
    try {
      const response = await fetch(`${apiBase}/api/icons/${encodeURIComponent(processName)}`)
      if (response.ok) {
        const iconData = await response.json()
        return iconData.colorPrimary || '#6B7FD7'
      }
    } catch (e) {
      console.warn(`Failed to fetch color for ${processName}:`, e)
    }
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

  // O(log S) check using sorted sleep periods
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

  // Step 3.6: Detect idle periods from gaps in the FULL timeline data (before top-15 filter).
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
  // Both backend Idle events and gap-detected idle can span into sleep when the system
  // auto-suspends during user AFK time. Sleep takes precedence in the display.
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

    focusedWindows.push({
      name: item.processName,
      value: [rowIdx, item._ts, end, item.durationSeconds],
      itemStyle: { color, borderColor: color, borderWidth: 0 },
      processName: item.processName,
      windowTitle: item.windowTitle,
      timestamp: item.timestamp,
      durationSeconds: item.durationSeconds,
      duringsSleep: false,
      itemColor: color,
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
        backgroundWindows.push({
          name: session.processName,
          value: [rowIdx, segStart, segEnd, 0],
          itemStyle: {
            color: 'transparent',
            borderColor: color,
            borderWidth: 1,
            opacity: 0.3,
          },
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
  }

  // Bail if a newer load started while we were working
  if (myLoadId !== undefined && myLoadId !== loadId) return

  // Get computed CSS colors (ECharts renders on Canvas — CSS variables don't work)
  const computedStyle = getComputedStyle(document.documentElement)
  const tooltipBg = computedStyle.getPropertyValue('--surface-card').trim()
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
    progressive: 200,
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

  timelineChart.setOption(option, true)

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

function getProcessColor(name) {
  const primary = getComputedStyle(document.documentElement)
    .getPropertyValue('--primary-color')
    .trim()
  const secondary = getComputedStyle(document.documentElement)
    .getPropertyValue('--secondary-color')
    .trim()
  const accent = getComputedStyle(document.documentElement)
    .getPropertyValue('--accent-color')
    .trim()

  const colors = [primary, secondary, accent, '#06D6A0', '#9B5DE5', '#F15BB5']
  const hash = name.split('').reduce((h, c) => ((h << 5) - h + c.charCodeAt(0)) | 0, 0)
  return colors[Math.abs(hash) % colors.length]
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
  min-height: 400px;
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
  text-transform: uppercase;
  letter-spacing: 0.5px;
  margin-bottom: 16px;
  color: var(--text-color);
}

.timeline-chart {
  width: 100%;
  height: 440px;
  margin-bottom: 16px;
  border: 2px solid var(--primary-color); /* 调试：确认容器可见 */
  background: var(--surface-card);
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
