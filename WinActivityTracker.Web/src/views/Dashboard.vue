<!--
  Dashboard view — today's usage overview. Auto-refreshes every 2s.
  Features:
    - Date picker to view any day's data
    - Two Chart.js bar charts: focus duration and switch count (Top 10)
    - Full data table with all tracked processes (scrollable)
    - Recent media playback list (scrollable)

  Chart lifecycle: created on first load, updated in-place on refresh.
  fc.update('none') skips animation to avoid visual noise during polling.

  Timezone: DB timestamps are UTC (EF Core strips DateTimeKind, so no 'Z' suffix).
  The toLocal() helper appends 'Z' before parsing, ensuring correct local-time display.
-->
<template>
  <div>
    <div class="row mb-3">
      <div class="col d-flex align-items-center">
        <div class="btn-group btn-group-sm me-2" role="group">
          <button v-for="p in periods" :key="p.key" class="btn"
            :class="period===p.key?'btn-primary':'btn-outline-secondary'"
            @click="setPeriod(p.key)">{{ p.label }}</button>
        </div>
        <input type="date" v-model="pickDate" class="form-control form-control-sm" style="width:150px"
          @change="onPickDate" title="查询特定日期" />
      </div>
    </div>

    <div v-if="error" class="alert alert-danger alert-dismissible fade show" role="alert">
      {{ error }}
      <button type="button" class="btn-close" @click="error=''"></button>
    </div>

    <div class="row">
      <div class="col-md-6">
        <div class="card mb-3">
          <div class="card-header">焦点时长 Top 10</div>
          <div class="card-body"><canvas ref="focusChart" height="300"></canvas></div>
        </div>
      </div>
      <div class="col-md-6">
        <div class="card mb-3">
          <div class="card-header">焦点切换次数</div>
          <div class="card-body"><canvas ref="switchChart" height="300"></canvas></div>
        </div>
      </div>
    </div>

    <div class="row">
      <div class="col-md-6">
        <div class="card mb-3">
          <div class="card-header">统计概览
            <small class="text-muted ms-2" v-if="totalSleepSeconds > 0">休眠/关机 {{ fmtDuration(totalSleepSeconds) }}</small>
            <button class="btn btn-sm btn-outline-secondary float-end" @click="toggleSort" title="切换排序">
              {{ sortAsc ? '↑ 升序' : '↓ 降序' }}
            </button>
          </div>
          <div class="card-body" style="max-height:400px;overflow-y:auto">
            <table class="table table-sm table-striped">
              <thead><tr><th>程序</th><th>总时长</th><th>切换次数</th></tr></thead>
              <tbody>
                <tr v-for="d in sortedSummary" :key="d.processName">
                  <td><strong>{{ d.processName }}</strong></td>
                  <td>{{ fmtDuration(d.totalSeconds) }}</td>
                  <td>{{ mergeSameProcess ? (d.adjustedSwitchCount ?? d.switchCount) : d.switchCount }}</td>
                </tr>
                <tr v-if="!summary.length"><td colspan="3" class="text-muted">暂无数据</td></tr>
              </tbody>
            </table>
          </div>
        </div>
      </div>
      <div class="col-md-6">
        <div class="card mb-3">
          <div class="card-header">最近媒体播放 <small class="text-muted">总计听歌 {{ totalListenFmt }}</small></div>
          <div class="card-body" style="max-height:400px;overflow-y:auto;padding:0">
            <table class="table table-sm mb-0">
              <thead><tr><th>持续</th><th>状态</th><th>歌曲</th><th>艺术家</th></tr></thead>
              <tbody>
                <tr v-for="m in displayMedia.slice().reverse()" :key="m.id || m.startTime"
                  :style="{borderLeft:'4px solid '+(m.playbackStatus==='Playing'?'#198754':'#dee2e6')}">
                  <td>{{ m.durationFmt }}</td>
                  <td>{{ m.playbackStatus === 'Playing' ? '▶' : '⏸' }}</td>
                  <td>{{ m.title }}</td>
                  <td>{{ m.artist }}</td>
                </tr>
                <tr v-if="!displayMedia.length"><td colspan="4" class="text-muted">暂无数据</td></tr>
              </tbody>
            </table>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, inject, onMounted, onUnmounted, computed } from 'vue'
import { fmtShortDur, parseUtcTs, fmtDuration, toLocalDateString } from '../utils/time.js'

import { Chart, registerables } from 'chart.js'

Chart.register(...registerables)

const apiBase = inject('apiBase')

const periods = [
  { key: 'today', label: '当日' },
  { key: 'week', label: '7天' },
  { key: 'month', label: '1月' },
  { key: 'year', label: '1年' },
  { key: 'all', label: '全部' },
]
const period = ref('today')
const pickDate = ref(toLocalDateString())
const mergeSameProcess = ref(true)  // loaded from /api/settings
const summary = ref([])
const totalSleepSeconds = ref(0)
const media = ref([])
const error = ref('')
const loading = ref(false)
const sortAsc = ref(false)

function setPeriod(p) { period.value = p; clearInterval(timer); timer = setInterval(loadSummary, 2000); loadSummary() }
function onPickDate() { period.value = 'today'; clearInterval(timer); timer = setInterval(loadSummary, 2000); loadSummary() }
function toggleSort() { sortAsc.value = !sortAsc.value }

function periodRange() {
  const now = new Date()
  const to = toLocalDateString(now)
  switch (period.value) {
    case 'week': { const d = new Date(now - 7 * 86400000); return [toLocalDateString(d), to] }
    case 'month': { const d = new Date(now.getFullYear(), now.getMonth() - 1, now.getDate()); return [toLocalDateString(d), to] }
    case 'year': { const d = new Date(now.getFullYear() - 1, now.getMonth(), now.getDate()); return [toLocalDateString(d), to] }
    case 'all': return ['2020-01-01', to]
    default: return [pickDate.value, pickDate.value]
  }
}

// Duration computed from session StartTime/EndTime (or now for active sessions).
const mediaWithDuration = computed(() => {
  const now = Date.now()
  return media.value.map(m => {
    const start = parseUtcTs(m.startTime)?.getTime() || now
    const end = m.endTime ? (parseUtcTs(m.endTime)?.getTime() || now) : now
    const sec = Math.max(1, Math.round((end - start) / 1000))
    return { ...m, durationSec: sec, durationFmt: fmtShortDur(sec) }
  })
})

// Merge consecutive records with same title+status (handles restart/wake edge cases).
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
  list.sort((a, b) => sortAsc.value
    ? a.totalSeconds - b.totalSeconds
    : b.totalSeconds - a.totalSeconds)
  return list
})


// Merge overlapping Playing intervals from ALL apps into non-overlapping ranges.
const totalListenFmt = computed(() => {
  const playing = displayMedia.value.filter(m => m.playbackStatus === 'Playing')
  if (!playing.length) return '0s'
  // Build intervals: [timestamp, timestamp + duration] in ms, sorted by start
  const intervals = playing.map(m => {
    const t = parseUtcTs(m.startTime)?.getTime() || 0
    return [t, t + m.durationSec * 1000]
  }).sort((a, b) => a[0] - b[0])
  const merged = []
  for (const [s, e] of intervals) {
    const last = merged[merged.length-1]
    if (last && s <= last[1]) last[1] = Math.max(last[1], e)
    else merged.push([s, e])
  }
  const total = merged.reduce((sum, [s, e]) => sum + (e-s)/1000, 0)
  return fmtShortDur(total)
})

// Template refs for the two canvas elements
const focusChart = ref(null)
const switchChart = ref(null)
let fc = null, sc = null  // Chart.js instance references for destroy/recreate
let timer = null

onMounted(async () => {
  try {
    const r = await fetch(`${apiBase}/api/settings`)
    if (r.ok) {
      const s = await r.json()
      mergeSameProcess.value = s.mergeSameProcessSwitches ?? true
    }
  } catch {}
  await loadSummary()
  timer = setInterval(loadSummary, 2000)
})

onUnmounted(() => {
  clearInterval(timer)
  fc?.destroy(); fc = null
  sc?.destroy(); sc = null
})

async function loadSummary() {
  await Promise.all([fetchSummary(), fetchMedia()])
}

async function fetchSummary() {
  try {
    const [from, to] = periodRange()
    const url = period.value === 'today'
      ? `${apiBase}/api/summary/today?date=${pickDate.value}`
      : `${apiBase}/api/summary/range?from=${from}&to=${to}T23:59:59`
    const r = await fetch(url)
    if (!r.ok) throw new Error(`API ${r.status}`)
    const res = await r.json()
    summary.value = Array.isArray(res) ? res : (res.items || [])
    totalSleepSeconds.value = Array.isArray(res) ? 0 : (res.totalSleepSeconds || 0)
    error.value = ''
    renderCharts(summary.value)
  } catch (e) { console.error(e); error.value = '加载摘要失败: ' + e.message }
}

async function fetchMedia() {
  try {
    const [fromDate, toDate] = periodRange()
    const r = await fetch(`${apiBase}/api/media/history?limit=50&from=${fromDate}&to=${toDate}`)
    if (!r.ok) throw new Error(`API ${r.status}`)
    media.value = await r.json()
  } catch (e) { console.error(e) }
}

function hashStr(s) {
  let h = 0
  for (let i = 0; i < s.length; i++) h = ((h << 5) - h + s.charCodeAt(i)) | 0
  return Math.abs(h)
}

function renderCharts(data) {
  const top = data.slice(0, 10)
  const labels = top.map(d => d.processName)
  const colors = labels.map(name => `hsl(${hashStr(name) % 360}, 60%, 55%)`)
  const focusData = top.map(d => +(d.totalSeconds / 60).toFixed(1))
  const switchData = top.map(d => mergeSameProcess.value ? (d.adjustedSwitchCount ?? d.switchCount) : d.switchCount)

  // Update in-place to avoid flicker; create on first call
  if (fc) {
    fc.data.labels = labels
    fc.data.datasets[0].data = focusData
    fc.data.datasets[0].backgroundColor = colors
    fc.update('none')
  } else {
    fc = new Chart(focusChart.value, {
      type: 'bar',
      data: {
        labels,
        datasets: [{
          label: '时长 (分钟)',
          data: focusData,
          backgroundColor: colors
        }]
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        plugins: { legend: { display: false } },
        scales: { y: { beginAtZero: true, title: { display: true, text: '分钟' } } }
      }
    })
  }

  if (sc) {
    sc.data.labels = labels
    sc.data.datasets[0].data = switchData
    sc.data.datasets[0].backgroundColor = colors
    sc.update('none')
  } else {
    sc = new Chart(switchChart.value, {
      type: 'bar',
      data: {
        labels,
        datasets: [{
          label: '切换次数',
          data: switchData,
          backgroundColor: colors
        }]
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        plugins: { legend: { display: false } },
        scales: { y: { beginAtZero: true, title: { display: true, text: '次数' } } }
      }
    })
  }
}

</script>
