<!--
  Dashboard view — Memphis style, ECharts charts, auto-refresh every 2s
-->
<template>
  <div class="dashboard">
    <!-- Period selector -->
    <div class="period-selector mb-3">
      <div class="period-buttons">
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
      <input
        type="date"
        v-model="pickDate"
        class="date-picker"
        @change="onPickDate"
        :title="t('dashboard.pickDateTitle')"
      />
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
                <td>{{ m.durationFmt }}</td>
                <td>{{ m.playbackStatus === 'Playing' ? '▶' : '⏸' }}</td>
                <td>{{ m.title }}</td>
                <td>{{ m.artist }}</td>
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
import { ref, inject, onMounted, onUnmounted, computed, nextTick } from 'vue'
import { fmtShortDur, parseUtcTs, fmtDuration, toLocalDateString } from '../utils/time.js'
import { mergeByProcessName } from '../utils/process.js'
import { useI18n } from '../i18n/index.js'
import * as echarts from 'echarts'
import MemphisCard from '../components/MemphisCard.vue'
import MemphisSkeleton from '../components/MemphisSkeleton.vue'

const apiBase = inject('apiBase')
const { t } = useI18n()

const periods = [
  { key: 'today', label: t('dashboard.periods.today') },
  { key: 'week', label: t('dashboard.periods.week') },
  { key: 'month', label: t('dashboard.periods.month') },
  { key: 'year', label: t('dashboard.periods.year') },
  { key: 'all', label: t('dashboard.periods.all') },
]

const period = ref('today')
const pickDate = ref(toLocalDateString())
const mergeSameProcess = ref(true)
const summary = ref([])
const totalSleepSeconds = ref(0)
const media = ref([])
const error = ref('')
const loading = ref(true)
const sortAsc = ref(false)

const focusChartRef = ref(null)
let focusChart = null
let timer = null

function setPeriod(p) {
  period.value = p
  clearInterval(timer)
  loadSummary()
  timer = setInterval(loadSummary, 2000)
}

function onPickDate() {
  period.value = 'today'
  clearInterval(timer)
  loadSummary()
  timer = setInterval(loadSummary, 2000)
}

function toggleSort() {
  sortAsc.value = !sortAsc.value
}

function periodRange() {
  const now = new Date()
  const to = toLocalDateString(now)
  switch (period.value) {
    case 'week': {
      const d = new Date(now - 7 * 86400000)
      return [toLocalDateString(d), to]
    }
    case 'month': {
      const d = new Date(now.getFullYear(), now.getMonth() - 1, now.getDate())
      return [toLocalDateString(d), to]
    }
    case 'year': {
      const d = new Date(now.getFullYear() - 1, now.getMonth(), now.getDate())
      return [toLocalDateString(d), to]
    }
    case 'all':
      return ['2020-01-01', to]
    default:
      return [pickDate.value, pickDate.value]
  }
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

const sortedSummary = computed(() => {
  const list = [...summary.value]
  list.sort((a, b) =>
    sortAsc.value ? a.totalSeconds - b.totalSeconds : b.totalSeconds - a.totalSeconds
  )
  return list
})

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
})

onUnmounted(() => {
  clearInterval(timer)
  if (focusChart) {
    focusChart.dispose()
    focusChart = null
  }
})

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
    const limits = { today: 50, week: 200, month: 500, year: 2000, all: 5000 }
    const limit = limits[period.value] || 50
    const r = await fetch(`${apiBase}/api/media/history?limit=${limit}&from=${fromDate}&to=${toDate}`)
    if (!r.ok) throw new Error(`API ${r.status}`)
    media.value = await r.json()
  } catch (e) {
    console.error(e)
  }
}

function getColor(name, alpha = 1) {
  // Use theme colors from CSS variables
  const primary = getComputedStyle(document.documentElement).getPropertyValue('--primary-color').trim()
  const secondary = getComputedStyle(document.documentElement).getPropertyValue('--secondary-color').trim()
  const accent = getComputedStyle(document.documentElement).getPropertyValue('--accent-color').trim()
  
  const colors = [primary, secondary, accent, '#06D6A0', '#9B5DE5', '#F15BB5', '#E76F51', '#4ECDC4']
  const hash = name.split('').reduce((h, c) => ((h << 5) - h + c.charCodeAt(0)) | 0, 0)
  return colors[Math.abs(hash) % colors.length]
}

async function renderCharts(data) {
  if (!focusChartRef.value) return

  const top = data.slice(0, 10)
  const labels = top.map(d => d.processName)
  const focusData = top.map(d => +(d.totalSeconds / 60).toFixed(1))

  // Fetch icons and colors for all processes
  const iconPromises = labels.map(async (processName) => {
    try {
      const response = await fetch(`${apiBase}/api/icons/${encodeURIComponent(processName)}`)
      if (response.ok) {
        const iconData = await response.json()
        return {
          icon: iconData.iconData && iconData.iconData.length > 0
            ? `data:image/png;base64,${iconData.iconData}`
            : null,
          colorPrimary: iconData.colorPrimary || '#6B7FD7',
          colorSecondary: iconData.colorSecondary || '#DD7596',
          colorAccent: iconData.colorAccent || '#06D6A0'
        }
      }
    } catch (e) {
      console.warn(`Failed to fetch icon for ${processName}:`, e)
    }
    return {
      icon: null,
      colorPrimary: '#6B7FD7',
      colorSecondary: '#DD7596',
      colorAccent: '#06D6A0'
    }
  })

  const iconDataList = await Promise.all(iconPromises)
  const icons = iconDataList.map(d => d.icon)
  const colors = iconDataList.map(d => d.colorPrimary)

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
      axisLine: { lineStyle: { color: 'var(--border-color)', width: 2 } },
    },
    yAxis: {
      type: 'value',
      name: t('dashboard.chart.durationAxis'),
      nameTextStyle: { color: 'var(--text-color)', fontWeight: 600 },
      axisLabel: { color: 'var(--text-color)' },
      axisLine: { lineStyle: { color: 'var(--border-color)', width: 2 } },
      splitLine: { lineStyle: { type: 'dashed', color: 'var(--surface-200)' } },
    },
    series: [
      {
        type: 'bar',
        data: focusData.map((val, idx) => ({
          value: val,
          itemStyle: {
            color: colors[idx],
            borderColor: 'var(--border-color)',
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
      textStyle: { color: 'var(--text-color)', fontWeight: 600 },
      formatter: params => {
        const p = params[0]
        return `<strong>${p.name}</strong><br/>${p.value} ${t('dashboard.chart.durationUnit')}`
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
  display: flex;
  gap: 4px;
}

.period-btn {
  padding: 8px 16px;
  border: 2px solid var(--surface-200);
  background: transparent;
  color: var(--text-color);
  font-weight: 600;
  text-transform: uppercase;
  font-size: 0.85rem;
  letter-spacing: 0.5px;
  cursor: pointer;
  transition: all 0.2s ease;

  &:hover {
    border-color: var(--primary-color);
    transform: translateY(-2px);
  }

  &.active {
    border-color: var(--primary-color);
    border-bottom-width: 3px;
    margin-bottom: -1px;
  }
}

.date-picker {
  padding: 8px 12px;
  border: 2px solid var(--surface-200);
  background: var(--surface-card);
  color: var(--text-color);
  font-weight: 600;
  transition: border-color 0.2s ease;

  &:focus {
    outline: none;
    border-color: var(--primary-color);
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
</style>
