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
        </div>
      </div>
    </MemphisCard>

    <!-- Summary table -->
    <MemphisCard class="summary-card">
      <div class="card-header-row">
        <h3 class="card-title">
          {{ t('history.card.periodSummary') }}
          <small class="count-info">{{ t('history.card.programCount', { count: data.length }) }}</small>
        </h3>
        <button class="sort-btn" @click="toggleSort">
          {{ sortAsc ? '↑' : '↓' }}
        </button>
      </div>
      <small v-if="totalSleepSeconds > 0" class="sleep-info">
        {{ t('history.sleepOff', { duration: fmtDuration(totalSleepSeconds) }) }}
      </small>
      <MemphisSkeleton v-if="loading" :lines="8" />
      <DataTable
        v-else
        :value="sortedData"
        :rows="20"
        :paginator="data.length > 20"
        :rowsPerPageOptions="[10, 20, 50, 100]"
        class="memphis-datatable"
        stripedRows
        showGridlines
      >
        <Column field="processName" :header="t('history.table.process')" sortable>
          <template #body="{ data }">
            <strong>{{ data.processName }}</strong>
          </template>
        </Column>
        <Column field="totalSeconds" :header="t('history.table.totalDuration')" sortable>
          <template #body="{ data }">
            {{ fmtDuration(data.totalSeconds) }}
          </template>
        </Column>
        <Column field="switchCount" :header="t('history.table.switches')" sortable>
          <template #body="{ data }">
            {{ mergeSameProcess ? (data.adjustedSwitchCount ?? data.switchCount) : data.switchCount }}
          </template>
        </Column>
      </DataTable>
    </MemphisCard>
  </div>
</template>

<script setup>
import { ref, inject, onMounted, onUnmounted, computed, nextTick, watch } from 'vue'
import { toLocalTime, fmtDuration, parseUtcTs } from '../utils/time.js'
import { mergeByProcessName } from '../utils/process.js'
import { useI18n } from '../i18n/index.js'
import { useTheme } from '../composables/useTheme.js'
import * as echarts from 'echarts'
import MemphisCard from '../components/MemphisCard.vue'
import MemphisSkeleton from '../components/MemphisSkeleton.vue'
import TimeRangePicker from '../components/TimeRangePicker.vue'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'

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
const sortAsc = ref(false)
const mergeSameProcess = ref(true)
const totalSleepSeconds = ref(0)
const loading = ref(true)
const error = ref('')

const timelineChartRef = ref(null)
let timelineChart = null

// Handle time change from picker
function handleTimeChange({ start, end }) {
  startDate.value = start
  endDate.value = end
  loadData()
}

// Convert Date to API format (YYYY-MM-DDTHH:mm:ss)
function toApiDateTime(date) {
  return date.toISOString().slice(0, 19)
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
  await loadData()

  // Add window resize listener for chart responsiveness
  window.addEventListener('resize', handleResize)
})

// Watch for theme changes and re-render timeline
watch(isDark, () => {
  console.log('Theme changed, re-rendering timeline')
  if (timelineChart && timeline.value && timeline.value.length > 0) {
    renderTimeline()
  }
})

onUnmounted(() => {
  // Remove resize listener
  window.removeEventListener('resize', handleResize)

  if (timelineChart) {
    timelineChart.dispose()
    timelineChart = null
  }
})

// Debounced resize handler
let resizeTimer = null
function handleResize() {
  if (resizeTimer) clearTimeout(resizeTimer)
  resizeTimer = setTimeout(() => {
    if (timelineChart) {
      timelineChart.resize()
      console.log('Chart resized due to window resize')
      // Re-render chart with current data to adapt to new dimensions
      if (timeline.value && timeline.value.length > 0) {
        renderTimeline()
        console.log('Chart re-rendered after resize')
      }
    }
  }, 200) // 200ms debounce
}

const sortedData = computed(() => {
  const arr = [...data.value]
  arr.sort((a, b) =>
    sortAsc.value ? a.totalSeconds - b.totalSeconds : b.totalSeconds - a.totalSeconds
  )
  return arr
})

function toggleSort() {
  sortAsc.value = !sortAsc.value
}

async function loadData() {
  loading.value = true
  error.value = ''

  try {
    const fromStr = toApiDateTime(startDate.value)
    const toStr = toApiDateTime(endDate.value)
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

    // For large datasets, apply intelligent sampling to improve rendering performance
    let sampledData = timelineData
    const samplingThreshold = rangeInHours <= 6 ? 2000 : 5000  // Lower threshold for small ranges

    if (timelineData.length > samplingThreshold) {
      // Sample to keep most significant records
      // Sort by duration (longer durations are more important)
      const sorted = [...timelineData].sort((a, b) => b.durationSeconds - a.durationSeconds)
      sampledData = sorted.slice(0, samplingThreshold)
      // Re-sort by timestamp for timeline rendering
      sampledData.sort((a, b) => new Date(a.timestamp) - new Date(b.timestamp))
      console.log(`📊 Sampled ${sampledData.length} records from ${timelineData.length} (kept longest durations)`)
    }

    timeline.value = sampledData
    console.log('✅ timeline.value set to:', timeline.value.length, 'records')

    // Load system events (sleep/shutdown periods)
    const eventsUrl = `${apiBase}/api/system/events?from=${fromStr}&to=${toStr}`
    console.log('Fetching system events:', eventsUrl)
    const r3 = await fetch(eventsUrl)
    if (r3.ok) {
      systemEvents.value = await r3.json()
      console.log('System events:', systemEvents.value.length, 'sleep/shutdown periods')
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
    await nextTick()
    await renderTimeline()
  } catch (e) {
    console.error('Load error:', e)
    error.value = `Failed to load: ${e.message}`
    loading.value = false
  }
}

async function renderTimeline() {
  console.log('=== renderTimeline called ===')
  console.log('timelineChartRef.value:', !!timelineChartRef.value)
  console.log('timeline.value.length:', timeline.value.length)

  if (!timelineChartRef.value) {
    console.warn('Timeline chart ref not ready')
    return
  }

  if (!timeline.value.length) {
    console.warn('No timeline data to render')
    // Clear chart if no data
    if (timelineChart) {
      timelineChart.clear()
    }
    return
  }

  console.log('Rendering timeline with', timeline.value.length, 'records')

  // Step 1: Use all data to calculate process statistics (for accurate representation)
  const processStats = {}
  timeline.value.forEach(item => {
    if (!processStats[item.processName]) {
      processStats[item.processName] = 0
    }
    processStats[item.processName] += item.durationSeconds
  })

  // Step 2: Sort processes by total duration (descending) and take top 15
  const processList = Object.entries(processStats)
    .sort((a, b) => b[1] - a[1])
    .slice(0, 15)
    .map(([name]) => name)

  console.log('Top 15 processes:', processList)

  // Step 2.5: Fetch colors for all processes
  const colorPromises = processList.map(async (processName) => {
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
  processList.forEach((name, idx) => {
    colorMap[name] = processColors[idx]
  })

  console.log('Process colors:', colorMap)

  // Step 3: Filter to only include top 15 processes (keep SystemSleep for now)
  const filteredTimeline = timeline.value
    .filter(item => processList.includes(item.processName))

  console.log('Rendering', filteredTimeline.length, 'records (all data points)')

  // Step 3.5: Build sleep period map for quick lookup
  const sleepPeriods = systemEvents.value.map(e => ({
    start: parseUtcTs(e.timestamp).getTime(),
    end: parseUtcTs(e.timestamp).getTime() + e.durationSeconds * 1000
  }))

  // Helper function to check if a timestamp is during sleep
  const isDuringSleep = (timestamp) => {
    const time = parseUtcTs(timestamp).getTime()
    return sleepPeriods.some(period => time >= period.start && time < period.end)
  }

  console.log('Sleep periods:', sleepPeriods.length)

  // Build chart data
  const focusedWindows = []
  filteredTimeline.forEach(item => {
    const processIndex = processList.indexOf(item.processName)
    if (processIndex === -1) return

    const start = parseUtcTs(item.timestamp).getTime()
    const end = start + item.durationSeconds * 1000
    const color = colorMap[item.processName]
    const duringsSleep = isDuringSleep(item.timestamp)

    focusedWindows.push({
      name: item.processName,
      value: [processIndex, start, end, item.durationSeconds],
      itemStyle: {
        color: duringsSleep ? 'transparent' : color,
        borderColor: color,
        borderWidth: duringsSleep ? 2 : 0,
      },
      // Store data for tooltip without creating closure
      processName: item.processName,
      windowTitle: item.windowTitle,
      timestamp: item.timestamp,
      durationSeconds: item.durationSeconds,
      duringsSleep: duringsSleep,
      itemColor: color
    })
  })

  console.log('Created', focusedWindows.length, 'focused window chart items')

  // Calculate X-axis range from selected dates
  const xAxisMin = startDate.value.getTime()
  const xAxisMax = endDate.value.getTime()

  // Step 4: Add background running windows (thin lines)
  // Only show background lines during periods with activity (not during sleep/shutdown)
  const backgroundWindows = []

  // Build activity periods from focus changes
  const activityPeriods = []
  filteredTimeline.forEach(item => {
    const start = parseUtcTs(item.timestamp).getTime()
    const end = start + item.durationSeconds * 1000
    activityPeriods.push({ start, end })
  })

  // Merge overlapping activity periods and sort
  activityPeriods.sort((a, b) => a.start - b.start)
  const mergedActivity = []
  activityPeriods.forEach(period => {
    if (mergedActivity.length === 0) {
      mergedActivity.push({ ...period })
    } else {
      const last = mergedActivity[mergedActivity.length - 1]
      if (period.start <= last.end) {
        // Overlapping, merge
        last.end = Math.max(last.end, period.end)
      } else {
        mergedActivity.push({ ...period })
      }
    }
  })

  console.log('Merged activity periods:', mergedActivity.length)

  windowSessions.value.forEach(session => {
    const processIndex = processList.indexOf(session.processName)
    if (processIndex === -1) return

    const color = colorMap[session.processName]
    const sessionOpenTime = parseUtcTs(session.openTime).getTime()
    const sessionCloseTime = session.closeTime ? parseUtcTs(session.closeTime).getTime() : xAxisMax

    // Only show background lines during activity periods
    mergedActivity.forEach(activityPeriod => {
      // Find intersection between window session and activity period
      const segmentStart = Math.max(sessionOpenTime, activityPeriod.start)
      const segmentEnd = Math.min(sessionCloseTime, activityPeriod.end)

      // Only create segment if there's actual overlap
      if (segmentStart < segmentEnd) {
        // Check if this segment is during sleep
        const isDuringSleepPeriod = sleepPeriods.some(sleep =>
          segmentStart >= sleep.start && segmentStart < sleep.end
        )

        // Skip if during sleep
        if (isDuringSleepPeriod) return

        backgroundWindows.push({
          name: session.processName,
          value: [processIndex, segmentStart, segmentEnd, 0],
          itemStyle: {
            color: 'transparent',
            borderColor: color,
            borderWidth: 1,
            opacity: 0.3
          },
          tooltip: {
            formatter: () => {
              const status = session.closeTime ? '已关闭' : '运行中'
              return `<div style="font-weight:600;margin-bottom:4px;color:${color};">${session.processName}</div>
                      <div style="font-size:0.9em;">${session.windowTitle}</div>
                      <div style="margin-top:4px;color:var(--surface-500);">
                        ${toLocalTime(session.openTime)} - ${session.closeTime ? toLocalTime(session.closeTime) : '现在'}
                      </div>
                      <div style="font-size:0.85em;color:var(--surface-400);">后台运行 · ${status}</div>`
            },
          },
        })
      }
    })
  })

  console.log('Created', backgroundWindows.length, 'background window chart items')

  // Combine background windows (rendered first, behind) and focused windows (on top)
  const allWindows = [...backgroundWindows, ...focusedWindows]

  // Calculate date range in days
  const rangeInDays = (xAxisMax - xAxisMin) / (1000 * 60 * 60 * 24)

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

  // Always dispose and recreate the chart instance for clean state
  if (timelineChart) {
    console.log('Disposing existing ECharts instance')
    timelineChart.dispose()
    timelineChart = null
  }

  timelineChart = echarts.init(timelineChartRef.value)
  console.log('ECharts instance created')

  // Get computed CSS colors (ECharts doesn't support CSS variables in tooltip)
  const computedStyle = getComputedStyle(document.documentElement)
  const tooltipBg = computedStyle.getPropertyValue('--surface-card').trim()
  const tooltipBorder = computedStyle.getPropertyValue('--primary-color').trim()
  const tooltipText = computedStyle.getPropertyValue('--text-color').trim()

  const option = {
    animation: false,
    grid: {
      left: 20,
      right: 40,
      top: 40,
      bottom: 60,
      containLabel: true,
    },
    xAxis: {
      type: 'time',
      min: xAxisMin,
      max: xAxisMax,
      axisLabel: {
        color: 'var(--text-color)',
        fontWeight: 600,
        formatter: timeFormatter,
        rotate: rangeInDays > 3 ? 15 : 0, // Rotate labels for longer ranges
      },
      axisLine: {
        lineStyle: { color: 'var(--border-color)', width: 2 },
      },
      splitLine: {
        lineStyle: { type: 'dashed', color: 'var(--surface-200)' },
      },
    },
    yAxis: {
      type: 'category',
      data: processList,
      axisLabel: {
        show: false,
      },
      axisLine: {
        show: false,
      },
      axisTick: {
        show: false,
      },
      splitLine: {
        show: false,
      },
    },
    series: [
      {
        type: 'custom',
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
      },
      formatter: (params) => {
        const data = params.data
        if (!data) return ''

        const sleepWarning = data.duringsSleep ? '<div style="color:#FFA500;font-size:0.85em;">⚠️ 休眠时段</div>' : ''
        return `<div style="font-weight:600;margin-bottom:4px;color:${data.itemColor};">${data.processName}</div>
                <div style="font-size:0.9em;">${data.windowTitle}</div>
                <div style="margin-top:4px;color:var(--primary-color);">
                  ${toLocalTime(data.timestamp)} · ${data.durationSeconds.toFixed(1)}s
                </div>
                ${sleepWarning}`
      }
    },
  }
  
  console.log('Setting option with', focusedWindows.length, 'data points')
  console.log('Option object:', JSON.stringify({
    seriesDataLength: option.series[0].data.length,
    yAxisData: option.yAxis.data,
    gridConfig: option.grid
  }))
  
  timelineChart.setOption(option, true) // notMerge: true
  
  // 强制刷新
  setTimeout(() => {
    timelineChart.resize()
    console.log('Chart resized after 100ms')
  }, 100)

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

  // Debug: log first render
  if (params.dataIndex === 0) {
    const debugInfo = {
      dataIndex: params.dataIndex,
      categoryIndex,
      isBackground,
      durationSeconds,
      value0: api.value(0),
      value1: api.value(1),
      value1Date: new Date(api.value(1)).toISOString(),
      value2: api.value(2),
      value2Date: new Date(api.value(2)).toISOString(),
      value3: api.value(3),
      startCoord: [start[0], start[1]],
      endCoord: [end[0], end[1]],
      size: [api.size([0, 1])[0], api.size([0, 1])[1]],
      rectShape: {
        x: rectShape.x,
        y: rectShape.y,
        width: rectShape.width,
        height: rectShape.height
      },
      hasStyle: !!api.style(),
      fill: api.style()?.fill
    }
    console.log('renderBar called for first item:')
    console.log(JSON.stringify(debugInfo, null, 2))
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
  height: 400px;
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
  border: 2px solid var(--border-color);

  &.focus {
    background: var(--primary-color);
  }

  &.visible {
    background: var(--accent-color);
    opacity: 0.5;
  }
}

.summary-card {
  min-height: 300px;
}

.card-header-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 8px;
}

.count-info {
  font-size: 0.85rem;
  color: var(--surface-400);
  margin-left: 12px;
  font-weight: normal;
}

.sleep-info {
  display: block;
  font-size: 0.85rem;
  color: var(--surface-400);
  margin-bottom: 16px;
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

:deep(.memphis-datatable) {
  .p-datatable-header {
    background: transparent;
    border: none;
  }

  .p-paginator {
    background: transparent;
    border: none;
    border-top: 2px solid var(--surface-200);
    padding: 16px 0;

    .p-paginator-pages .p-paginator-page {
      border: 2px solid var(--surface-200);
      background: transparent;
      color: var(--text-color);
      min-width: 32px;
      height: 32px;
      margin: 0 2px;
      transition: all 0.2s ease;

      &:hover {
        border-color: var(--primary-color);
        background: transparent;
      }

      &.p-highlight {
        border-color: var(--primary-color);
        background: transparent;
        color: var(--primary-color);
        font-weight: 700;
      }
    }

    .p-paginator-first,
    .p-paginator-prev,
    .p-paginator-next,
    .p-paginator-last {
      border: 2px solid var(--surface-200);
      background: transparent;
      color: var(--text-color);
      min-width: 32px;
      height: 32px;
      margin: 0 2px;

      &:hover {
        border-color: var(--primary-color);
        background: transparent;
      }
    }
  }
}
</style>
