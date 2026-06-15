<!--
  Dashboard view — Memphis style, ECharts charts, auto-refresh every 2s
-->
<template>
  <div class="dashboard">
    <!-- Period selector -->
    <div class="period-selector mb-3">
      <div class="period-buttons" ref="periodButtonsRef">
        <div class="sliding-frame" :style="frameStyle" v-show="frameReady"></div>
        <button
          v-for="p in periods"
          :key="p.key"
          class="period-btn"
          :class="{ active: period === p.key }"
          @click="setPeriod(p.key)"
        >
          {{ p.label }}
        </button>
      </div>
      <div class="date-wheels" :class="{ dimmed: !isToday }">
        <TimeWheel v-model="wheelYear" :items="yearOptions" wide :label="t('common.year')" @carry="(d) => carryDate('year', d)" />
        <TimeWheel v-model="wheelMonth" :items="monthOptions" :label="t('common.month')" @carry="(d) => carryDate('month', d)" />
        <TimeWheel v-model="wheelDay" :items="dayOptions" :label="t('common.day')" @carry="(d) => carryDate('day', d)" />
      </div>
    </div>

    <!-- Error -->
    <div v-if="error" class="error-banner mb-3">
      {{ error }}
      <button class="close-btn" @click="error = ''">✕</button>
    </div>

    <!-- Charts row -->
    <div class="charts-row mb-3">
      <MemphisCard class="chart-card">
        <h3 class="card-title">{{ t('dashboard.card.focusDurationTop10') }}</h3>
        <MemphisSkeleton v-if="loading" :lines="5" />
        <div v-else ref="focusChartRef" class="chart-container"></div>
      </MemphisCard>

      <MemphisCard class="data-card">
        <h3 class="card-title">
          {{ t('dashboard.card.recentMedia') }}
          <small class="total-listen">{{ t('dashboard.totalListen', { duration: totalListenFmt }) }}</small>
        </h3>
        <MemphisSkeleton v-if="loading" :lines="8" />
        <div v-else class="table-wrapper">
          <table class="media-table">
            <thead>
              <tr>
                <th>{{ t('dashboard.media.duration') }}</th>
                <th>{{ t('dashboard.media.status') }}</th>
                <th>{{ t('dashboard.media.song') }}</th>
                <th>{{ t('dashboard.media.artist') }}</th>
              </tr>
            </thead>
            <tbody>
              <tr
                v-for="m in displayMedia.slice().reverse()"
                :key="m.id || m.startTime"
                :class="{ playing: m.playbackStatus === 'Playing' }"
              >
                <td><span :key="m.durationFmt" class="flicker-text">{{ m.durationFmt }}</span></td>
                <td><span :key="m.playbackStatus" class="flicker-text">{{ m.playbackStatus === 'Playing' ? '▶' : '⏸' }}</span></td>
                <td><span :key="m.title" class="flicker-text">{{ m.title }}</span></td>
                <td><span :key="m.artist" class="flicker-text">{{ m.artist }}</span></td>
              </tr>
              <tr v-if="!displayMedia.length">
                <td colspan="4" class="no-data">{{ t('dashboard.media.noData') }}</td>
              </tr>
            </tbody>
          </table>
        </div>
      </MemphisCard>
    </div>
  </div>
</template>

<script setup>
import { ref, inject, onMounted, onUnmounted, computed, nextTick, watch } from 'vue'
import { fmtShortDur, parseUtcTs, toLocalDateString } from '../utils/time.js'
import { mergeByProcessName } from '../utils/process.js'
import { useI18n } from '../i18n/index.js'
import { useTheme } from '../composables/useTheme.js'
import * as echarts from 'echarts'
import MemphisCard from '../components/MemphisCard.vue'
import MemphisSkeleton from '../components/MemphisSkeleton.vue'
import TimeWheel from '../components/TimeWheel.vue'

const apiBase = inject('apiBase')
const { t } = useI18n()
const { isDark } = useTheme()

// Cache icon data to avoid re-fetching every refresh (2s interval)
const iconCache = new Map()

const periods = [
  { key: 'today', label: t('dashboard.periods.today') },
  { key: 'week', label: t('dashboard.periods.week') },
  { key: 'month', label: t('dashboard.periods.month') },
  { key: 'halfYear', label: t('dashboard.periods.halfYear') },
  { key: 'year', label: t('dashboard.periods.year') },
]

const period = ref('today')
const isToday = computed(() => period.value === 'today')
const pickDate = ref(toLocalDateString())
const mergeSameProcess = ref(true)
const summary = ref([])
const totalSleepSeconds = ref(0)
const media = ref([])
const error = ref('')
const loading = ref(true)

// ── Date wheels (replaces date input) ──

const earliestDate = ref(null)
const wheelYear = ref(new Date().getFullYear())
const wheelMonth = ref(new Date().getMonth() + 1)
const wheelDay = ref(new Date().getDate())

function range(from, to) {
  const arr = []
  for (let i = from; i <= to; i++) arr.push(i)
  return arr
}

const yearOptions = computed(() => {
  const from = earliestDate.value ? earliestDate.value.getFullYear() : new Date().getFullYear() - 5
  return range(from, new Date().getFullYear())
})
const monthOptions = computed(() => {
  let min = 1, max = 12
  if (earliestDate.value && wheelYear.value === earliestDate.value.getFullYear()) {
    min = earliestDate.value.getMonth() + 1
  }
  if (wheelYear.value === new Date().getFullYear()) {
    max = new Date().getMonth() + 1
  }
  return range(min, max)
})
const dayOptions = computed(() => {
  const maxDay = new Date(wheelYear.value, wheelMonth.value, 0).getDate()
  let min = 1, max = maxDay
  if (earliestDate.value &&
      wheelYear.value === earliestDate.value.getFullYear() &&
      wheelMonth.value === earliestDate.value.getMonth() + 1) {
    min = earliestDate.value.getDate()
  }
  if (wheelYear.value === new Date().getFullYear() &&
      wheelMonth.value === new Date().getMonth() + 1) {
    max = Math.min(maxDay, new Date().getDate())
  }
  return range(min, max)
})

function carryDate(unit, delta) {
  const cur = new Date(wheelYear.value, wheelMonth.value - 1, wheelDay.value)
  const next = new Date(cur)

  if (unit === 'month') {
    const d = next.getDate()
    next.setDate(1)
    next.setMonth(next.getMonth() + delta)
    const maxD = new Date(next.getFullYear(), next.getMonth() + 1, 0).getDate()
    next.setDate(Math.min(d, maxD))
  } else if (unit === 'day') {
    next.setDate(next.getDate() + delta)
  } else {
    return
  }

  if (earliestDate.value) {
    const e = new Date(earliestDate.value)
    e.setHours(0, 0, 0, 0)
    if (+next < +e) return
  }
  const todayEnd = new Date()
  todayEnd.setHours(23, 59, 59, 999)
  if (+next > +todayEnd) return

  wheelYear.value = next.getFullYear()
  wheelMonth.value = next.getMonth() + 1
  wheelDay.value = next.getDate()
}

watch(yearOptions, (opts) => {
  if (!opts.includes(wheelYear.value)) {
    wheelYear.value = wheelYear.value < opts[0] ? opts[0] : opts[opts.length - 1]
  }
})
watch(monthOptions, (opts) => {
  if (!opts.includes(wheelMonth.value)) {
    wheelMonth.value = wheelMonth.value < opts[0] ? opts[0] : opts[opts.length - 1]
  }
})
watch([wheelYear, wheelMonth], () => {
  const opts = dayOptions.value
  if (!opts.includes(wheelDay.value)) {
    wheelDay.value = opts[opts.length - 1]
  }
})

watch([wheelYear, wheelMonth, wheelDay], () => {
  const d = `${wheelYear.value}-${String(wheelMonth.value).padStart(2, '0')}-${String(wheelDay.value).padStart(2, '0')}`
  pickDate.value = d
  onPickDate()
})

const periodButtonsRef = ref(null)
const frameStyle = ref({})
const frameReady = ref(false)

function updateFrame() {
  nextTick(() => {
    const el = periodButtonsRef.value
    if (!el) return
    const active = el.querySelector('.period-btn.active')
    if (!active) return
    frameStyle.value = {
      width: active.offsetWidth + 'px',
      left: active.offsetLeft + 'px'
    }
    frameReady.value = true
  })
}

watch(period, updateFrame)

const focusChartRef = ref(null)
let focusChart = null
let timer = null

function setPeriod(p) {
  period.value = p
  if (p === 'today') {
    const now = new Date()
    wheelYear.value = now.getFullYear()
    wheelMonth.value = now.getMonth() + 1
    wheelDay.value = now.getDate()
  }
  clearInterval(timer)
  loadSummary()
  timer = setInterval(loadSummary, 2000)
}

function onPickDate() {
  clearInterval(timer)
  loadSummary()
  timer = setInterval(loadSummary, 2000)
}

function periodRange() {
  const [y, m, d] = pickDate.value.split('-').map(Number)
  const endDate = new Date(y, m - 1, d)
  const to = toLocalDateString(endDate)

  if (period.value === 'today') return [pickDate.value, pickDate.value]

  let startDate
  switch (period.value) {
    case 'week':
      startDate = new Date(endDate); startDate.setDate(startDate.getDate() - 7); break
    case 'month':
      startDate = new Date(endDate); startDate.setMonth(startDate.getMonth() - 1); break
    case 'halfYear':
      startDate = new Date(endDate); startDate.setMonth(startDate.getMonth() - 6); break
    case 'year':
      startDate = new Date(endDate); startDate.setFullYear(startDate.getFullYear() - 1); break
    default:
      return [pickDate.value, pickDate.value]
  }

  return [toLocalDateString(startDate), to]
}

const mediaWithDuration = computed(() => {
  const now = Date.now()
  return media.value.map(m => {
    const start = parseUtcTs(m.startTime)?.getTime() || now
    const end = m.endTime ? (parseUtcTs(m.endTime)?.getTime() || now) : now
    const sec = Math.max(1, Math.round((end - start) / 1000))
    return { ...m, durationSec: sec, durationFmt: fmtShortDur(sec) }
  })
})

const mergedMedia = computed(() => {
  const list = mediaWithDuration.value
  if (!list.length) return []
  const merged = []
  let cur = { ...list[0] }
  for (let i = 1; i < list.length; i++) {
    const item = list[i]
    if (cur.title === item.title && cur.playbackStatus === item.playbackStatus) {
      cur.durationSec += item.durationSec
      cur.durationFmt = fmtShortDur(cur.durationSec)
      if (item.endTime) cur.endTime = item.endTime
    } else {
      merged.push(cur)
      cur = { ...item }
    }
  }
  merged.push(cur)
  return merged
})

const displayMedia = computed(() =>
  mergedMedia.value.filter(m => m.playbackStatus !== 'SystemSleep')
)

const totalListenFmt = computed(() => {
  const playing = displayMedia.value.filter(m => m.playbackStatus === 'Playing')
  if (!playing.length) return '0s'
  const intervals = playing
    .map(m => {
      const t = parseUtcTs(m.startTime)?.getTime() || 0
      return [t, t + m.durationSec * 1000]
    })
    .sort((a, b) => a[0] - b[0])
  const merged = []
  for (const [s, e] of intervals) {
    const last = merged[merged.length - 1]
    if (last && s <= last[1]) last[1] = Math.max(last[1], e)
    else merged.push([s, e])
  }
  const total = merged.reduce((sum, [s, e]) => sum + (e - s) / 1000, 0)
  return fmtShortDur(total)
})

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
  await loadSummary()
  timer = setInterval(loadSummary, 2000)

  // Add window resize listener for chart responsiveness
  window.addEventListener('resize', handleResize)

  // Fetch earliest record to constrain date wheels
  try {
    const r = await fetch(`${apiBase}/api/db/stats`)
    if (r.ok) {
      const stats = await r.json()
      if (stats.oldestRecord) {
        earliestDate.value = new Date(stats.oldestRecord)
      }
    }
  } catch (e) {
    console.error('Failed to fetch DB stats:', e)
  }

  // Position the sliding frame on the initially active button
  updateFrame()
})

// Watch for theme changes and re-render charts
watch(isDark, () => {
  console.log('Theme changed, re-rendering charts')
  if (focusChart && summary.value && summary.value.length > 0) {
    renderCharts(summary.value)
  }
})

onUnmounted(() => {
  clearInterval(timer)

  // Remove resize listener
  window.removeEventListener('resize', handleResize)

  if (focusChart) {
    focusChart.dispose()
    focusChart = null
  }
})

// Debounced resize handler
let resizeTimer = null
function handleResize() {
  if (resizeTimer) clearTimeout(resizeTimer)
  resizeTimer = setTimeout(() => {
    if (focusChart) {
      focusChart.resize()
      console.log('Chart resized due to window resize')
      // Re-render chart with current data to adapt to new dimensions
      if (summary.value && summary.value.length > 0) {
        renderCharts(summary.value)
        console.log('Chart re-rendered after resize')
      }
    }
  }, 200) // 200ms debounce
}

async function loadSummary() {
  await Promise.all([fetchSummary(), fetchMedia()])
}

async function fetchSummary() {
  try {
    const [from, to] = periodRange()
    const url =
      period.value === 'today'
        ? `${apiBase}/api/summary/today?date=${pickDate.value}`
        : `${apiBase}/api/summary/range?from=${from}&to=${to}T23:59:59`
    const r = await fetch(url)
    if (!r.ok) throw new Error(`API ${r.status}`)
    const res = await r.json()
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
    summary.value = mergedData
    totalSleepSeconds.value = Array.isArray(res) ? 0 : res.totalSleepSeconds || 0
    error.value = ''
    loading.value = false
    await nextTick()
    renderCharts(summary.value)
  } catch (e) {
    console.error(e)
    error.value = t('dashboard.error.loadSummaryFailed', { message: e.message })
    loading.value = false
  }
}

async function fetchMedia() {
  try {
    const [fromDate, toDate] = periodRange()
    const limits = { today: 50, week: 200, month: 500, halfYear: 3000, year: 2000 }
    const limit = limits[period.value] || 50
    const r = await fetch(`${apiBase}/api/media/history?limit=${limit}&from=${fromDate}&to=${toDate}`)
    if (!r.ok) throw new Error(`API ${r.status}`)
    media.value = await r.json()
  } catch (e) {
    console.error(e)
  }
}

async function renderCharts(data) {
  if (!focusChartRef.value) return

  const top = data.slice(0, 10)
  const labels = top.map(d => d.processName)
  const focusData = top.map(d => +(d.totalSeconds / 60).toFixed(1))

  // Fetch icons and colors for all processes (cached)
  const iconPromises = labels.map(async (processName) => {
    const cached = iconCache.get(processName)
    if (cached) return cached
    try {
      const response = await fetch(`${apiBase}/api/icons/${encodeURIComponent(processName)}`)
      if (response.ok) {
        const iconData = await response.json()
        const result = {
          icon: iconData.iconData && iconData.iconData.length > 0
            ? `data:image/png;base64,${iconData.iconData}`
            : null,
          colorPrimary: iconData.colorPrimary || '#6B7FD7',
          colorSecondary: iconData.colorSecondary || '#DD7596',
          colorAccent: iconData.colorAccent || '#06D6A0'
        }
        iconCache.set(processName, result)
        return result
      }
    } catch (e) {
      console.warn(`Failed to fetch icon for ${processName}:`, e)
    }
    const fallback = {
      icon: null,
      colorPrimary: '#6B7FD7',
      colorSecondary: '#DD7596',
      colorAccent: '#06D6A0'
    }
    iconCache.set(processName, fallback)
    return fallback
  })

  const iconDataList = await Promise.all(iconPromises)
  const icons = iconDataList.map(d => d.icon)
  const colors = iconDataList.map(d => d.colorPrimary)

  // Resolve CSS variables for ECharts (Canvas doesn't support CSS custom properties)
  const cs = getComputedStyle(document.documentElement)
  const textColor = cs.getPropertyValue('--text-color').trim()
  const borderColor = cs.getPropertyValue('--border-color').trim()
  const surface200 = cs.getPropertyValue('--surface-200').trim()

  // Focus duration chart
  if (!focusChart) {
    focusChart = echarts.init(focusChartRef.value)
  }
  focusChart.setOption({
    grid: { left: 60, right: 20, top: 20, bottom: 80 },
    xAxis: {
      type: 'category',
      data: labels,
      axisLabel: {
        interval: 0,
        formatter: (value, index) => {
          // Return image placeholder that will be replaced by rich text
          return `{img${index}|}`
        },
        rich: icons.reduce((acc, icon, idx) => {
          acc[`img${idx}`] = {
            backgroundColor: {
              image: icon || 'data:image/svg+xml;base64,PHN2ZyB3aWR0aD0iMzIiIGhlaWdodD0iMzIiIHZpZXdCb3g9IjAgMCAzMiAzMiIgZmlsbD0ibm9uZSIgeG1sbnM9Imh0dHA6Ly93d3cudzMub3JnLzIwMDAvc3ZnIj4KICA8cmVjdCB3aWR0aD0iMzIiIGhlaWdodD0iMzIiIGZpbGw9IiNDQ0NDQ0MiLz4KICA8dGV4dCB4PSI1MCUiIHk9IjUwJSIgZG9taW5hbnQtYmFzZWxpbmU9Im1pZGRsZSIgdGV4dC1hbmNob3I9Im1pZGRsZSIgZmlsbD0id2hpdGUiIGZvbnQtc2l6ZT0iMTYiPj88L3RleHQ+Cjwvc3ZnPg=='
            },
            height: 32,
            width: 32
          }
          return acc
        }, {})
      },
      axisLine: { lineStyle: { color: borderColor, width: 2 } },
    },
    yAxis: {
      type: 'value',
      name: t('dashboard.chart.durationAxis'),
      nameTextStyle: { color: textColor, fontWeight: 600 },
      axisLabel: { color: textColor },
      axisLine: { lineStyle: { color: borderColor, width: 2 } },
      splitLine: { lineStyle: { type: 'dashed', color: surface200 } },
    },
    series: [
      {
        type: 'bar',
        data: focusData.map((val, idx) => ({
          value: val,
          itemStyle: {
            color: colors[idx],
            borderColor: borderColor,
            borderWidth: 2,
          },
        })),
        label: { show: false },
        emphasis: {
          itemStyle: {
            shadowBlur: 10,
            shadowColor: 'rgba(0,0,0,0.3)',
          },
        },
      },
    ],
    tooltip: {
      trigger: 'axis',
      axisPointer: { type: 'shadow' },
      backgroundColor: 'var(--surface-card)',
      borderColor: 'var(--primary-color)',
      borderWidth: 2,
      textStyle: { color: 'var(--text-color)', fontWeight: 600, fontFamily: 'Ubuntu Mono' },
      formatter: params => {
        const p = params[0]
        const totalSec = Math.round(p.value * 60)
        return `<strong style="font-family:'Ubuntu Mono'">${p.name}</strong><br/>${fmtShortDur(totalSec)}`
      },
    },
  })
}
</script>

<style lang="scss" scoped>
.dashboard {
  width: 100%;
}

.period-selector {
  display: flex;
  gap: 16px;
  align-items: center;
  flex-wrap: wrap;
}

.period-buttons {
  position: relative;
  display: flex;
  gap: 4px;

  &:has(.period-btn.active:hover) .sliding-frame {
    border-color: var(--text-color);
    transform: translateY(-2px);
    box-shadow: 4px 4px 0 color-mix(in srgb, var(--primary-color) 80%, transparent);
    transition:
      left 0.28s cubic-bezier(0.4, 0, 0.2, 1),
      width 0.28s cubic-bezier(0.4, 0, 0.2, 1),
      transform 0.12s ease-out,
      box-shadow 0.15s ease-out,
      border-color 0s 0s;
  }
}

.sliding-frame {
  position: absolute;
  top: 0;
  left: 0;
  height: 100%;
  border: 3px solid color-mix(in srgb, var(--text-color) 80%, transparent);
  pointer-events: none;
  box-shadow: 0 0 0 transparent;
  transition:
    left 0.28s cubic-bezier(0.4, 0, 0.2, 1),
    width 0.28s cubic-bezier(0.4, 0, 0.2, 1),
    transform 0.12s ease-out,
    box-shadow 0.15s ease-out,
    border-color 0.2s 5s;
  z-index: 0;
}

.period-btn {
  position: relative;
  z-index: 1;
  padding: 8px 16px;
  border: 2px solid transparent;
  background: transparent;
  color: var(--text-color);
  font-weight: 600;
  text-transform: uppercase;
  font-size: 0.85rem;
  letter-spacing: 0.5px;
  cursor: pointer;
  transition: all 0.2s ease;

  &:hover {
    transform: translateY(-2px);
  }

}

.date-wheels {
  display: flex;
  gap: 4px;
  align-items: center;
  transition: opacity 0.25s ease;

  :deep(.wheel-frame) {
    border-color: color-mix(in srgb, var(--text-color) 80%, transparent);
    box-shadow: 0 0 0 transparent;
    transition:
      transform 0.12s ease-out,
      box-shadow 0.12s ease-out,
      border-color 0.2s 5s;

    &:hover {
      transform: translate(-2px, -2px);
      border-color: var(--text-color);
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

  &.dimmed {
    opacity: 0.45;

    :deep(.wheel-frame) {
      border-color: transparent;
      box-shadow: none;

      &:hover {
        transform: none;
        border-color: transparent;
        box-shadow: none;
      }

      &.scrolling {
        border-color: transparent;
        box-shadow: none;
      }
    }
  }
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

.charts-row {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(400px, 1fr));
  gap: 24px;
}

.chart-card,
.data-card {
  min-height: 350px;
}

.card-title {
  font-size: 1.1rem;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.5px;
  margin-bottom: 16px;
  color: var(--text-color);
}

.chart-container {
  width: 100%;
  height: 300px;
}

.card-header-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 8px;
}

.sort-btn {
  padding: 4px 12px;
  border: 2px solid var(--surface-200);
  background: transparent;
  color: var(--text-color);
  font-size: 1.2rem;
  cursor: pointer;
  transition: all 0.2s ease;

  &:hover {
    border-color: var(--primary-color);
    transform: translateY(-2px);
  }
}

.sleep-info,
.total-listen {
  display: block;
  font-size: 0.85rem;
  color: var(--surface-400);
  margin-bottom: 12px;
}

.table-wrapper {
  max-height: 400px;
  overflow-y: auto;
  border: 2px solid var(--surface-200);
}

.data-table,
.media-table {
  width: 100%;
  border-collapse: collapse;

  thead {
    position: sticky;
    top: 0;
    background: var(--surface-card);
    z-index: 1;

    th {
      padding: 12px 16px;
      text-align: left;
      font-weight: 600;
      text-transform: uppercase;
      font-size: 0.85rem;
      letter-spacing: 0.5px;
      border-bottom: 2px solid var(--primary-color);
      color: var(--text-color);
    }
  }

  tbody {
    tr {
      border-bottom: 1px solid var(--surface-200);
      transition: all 0.2s ease;

      &:hover {
        background: var(--surface-100);
        transform: translateX(2px);
      }

      &.playing {
        border-left: 4px solid var(--success-color);
      }

      td {
        padding: 12px 16px;
        color: var(--text-color);
      }
    }
  }

  .no-data {
    text-align: center;
    color: var(--surface-400);
    font-style: italic;
  }
}

.flicker-text {
  display: inline-block;
  animation: textFlicker 0.15s ease;
}

@keyframes textFlicker {
  0% { opacity: 0.35; }
  100% { opacity: 1; }
}
</style>
