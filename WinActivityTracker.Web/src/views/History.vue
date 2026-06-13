<!--
  History view — date range query + visual timeline + summary table
  Merges old Timeline functionality with aggregated stats
-->
<template>
  <div class="history-page">
    <!-- Date range selector -->
    <MemphisCard class="query-card mb-3" :show-deco="false">
      <div class="query-row">
        <div class="input-group">
          <label class="input-label">{{ t('history.startDate') }}</label>
          <input type="date" v-model="fromDate" class="memphis-input" />
        </div>
        <div class="input-group">
          <label class="input-label">{{ t('history.endDate') }}</label>
          <input type="date" v-model="toDate" class="memphis-input" />
        </div>
        <button class="query-btn" @click="loadData">
          {{ t('common.query') }}
        </button>
      </div>
    </MemphisCard>

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
import { ref, inject, onMounted, onUnmounted, computed, nextTick } from 'vue'
import { toLocalDateString, toLocalTime, fmtDuration } from '../utils/time.js'
import { useI18n } from '../i18n/index.js'
import * as echarts from 'echarts'
import MemphisCard from '../components/MemphisCard.vue'
import MemphisSkeleton from '../components/MemphisSkeleton.vue'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'

const apiBase = inject('apiBase')
const { t } = useI18n()

const fromDate = ref(toLocalDateString(new Date(new Date().getFullYear(), new Date().getMonth(), 1)))
const toDate = ref(toLocalDateString(new Date()))
const data = ref([])
const timeline = ref([])
const sortAsc = ref(false)
const mergeSameProcess = ref(true)
const totalSleepSeconds = ref(0)
const loading = ref(true)
const error = ref('')

const timelineChartRef = ref(null)
let timelineChart = null

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
})

onUnmounted(() => {
  if (timelineChart) {
    timelineChart.dispose()
    timelineChart = null
  }
})

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
    // Load summary
    const r1 = await fetch(`${apiBase}/api/summary/range?from=${fromDate.value}&to=${toDate.value}T23:59:59`)
    if (!r1.ok) throw new Error(`Summary API ${r1.status}`)
    const res = await r1.json()
    data.value = Array.isArray(res) ? res : res.items || []
    totalSleepSeconds.value = Array.isArray(res) ? 0 : res.totalSleepSeconds || 0

    // Load timeline for visualization
    const r2 = await fetch(`${apiBase}/api/windows/timeline?from=${fromDate.value}T00:00:00&to=${toDate.value}T23:59:59&limit=5000`)
    if (!r2.ok) throw new Error(`Timeline API ${r2.status}`)
    const timelineRes = await r2.json()
    timeline.value = timelineRes.data || timelineRes

    loading.value = false
    await nextTick()
    renderTimeline()
  } catch (e) {
    console.error(e)
    error.value = `Failed to load: ${e.message}`
    loading.value = false
  }
}

function renderTimeline() {
  if (!timelineChartRef.value || !timeline.value.length) return

  // Group timeline by process
  const processList = [...new Set(timeline.value.map(t => t.processName))].slice(0, 15)
  
  // Convert timeline to chart data
  const focusData = []
  timeline.value.forEach(item => {
    const processIndex = processList.indexOf(item.processName)
    if (processIndex === -1) return
    
    const start = new Date(item.timestamp).getTime()
    const end = start + item.durationSeconds * 1000
    
    focusData.push({
      name: item.processName,
      value: [processIndex, start, end, item.durationSeconds],
      itemStyle: {
        color: getProcessColor(item.processName),
        borderColor: 'var(--border-color)',
        borderWidth: 1,
      },
      tooltip: {
        formatter: () => {
          return `<div style="font-weight:600;margin-bottom:4px;">${item.processName}</div>
                  <div style="font-size:0.9em;">${item.windowTitle}</div>
                  <div style="margin-top:4px;color:var(--primary-color);">
                    ${toLocalTime(item.timestamp)} · ${item.durationSeconds.toFixed(1)}s
                  </div>`
        },
      },
    })
  })

  if (!timelineChart) {
    timelineChart = echarts.init(timelineChartRef.value)
  }

  timelineChart.setOption({
    grid: {
      left: 120,
      right: 40,
      top: 40,
      bottom: 60,
      containLabel: false,
    },
    xAxis: {
      type: 'time',
      axisLabel: {
        color: 'var(--text-color)',
        fontWeight: 600,
        formatter: '{HH}:{mm}',
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
        color: 'var(--text-color)',
        fontWeight: 600,
        fontSize: 12,
      },
      axisLine: {
        lineStyle: { color: 'var(--border-color)', width: 2 },
      },
      splitLine: {
        lineStyle: { type: 'dashed', color: 'var(--surface-200)' },
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
        data: focusData,
      },
    ],
    tooltip: {
      backgroundColor: 'var(--surface-card)',
      borderColor: 'var(--primary-color)',
      borderWidth: 2,
      textStyle: {
        color: 'var(--text-color)',
      },
    },
  })
}

function renderBar(params, api) {
  const categoryIndex = api.value(0)
  const start = api.coord([api.value(1), categoryIndex])
  const end = api.coord([api.value(2), categoryIndex])
  const height = api.size([0, 1])[1] * 0.6

  return {
    type: 'rect',
    shape: {
      x: start[0],
      y: start[1] - height / 2,
      width: Math.max(end[0] - start[0], 2),
      height: height,
    },
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

.query-card {
  background: var(--surface-card);
}

.query-row {
  display: flex;
  gap: 16px;
  align-items: flex-end;
  flex-wrap: wrap;
}

.input-group {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.input-label {
  font-size: 0.9rem;
  font-weight: 600;
  color: var(--text-color);
  text-transform: uppercase;
  letter-spacing: 0.5px;
}

.query-btn {
  padding: 10px 24px;
  border: 2px solid var(--primary-color);
  background: transparent;
  color: var(--text-color);
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.5px;
  cursor: pointer;
  transition: all 0.2s ease;

  &:hover {
    background: var(--primary-color);
    color: var(--surface-card);
    transform: translateY(-2px);
    box-shadow: 0 4px 0 var(--primary-color);
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
