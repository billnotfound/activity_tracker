<!--
  Dashboard view — today's usage overview. Auto-refreshes every 2s.
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
          @change="onPickDate" :title="t('dashboard.pickDateTitle')" />
      </div>
    </div>

    <div v-if="error" class="alert alert-danger alert-dismissible fade show" role="alert">
      {{ error }}
      <button type="button" class="btn-close" @click="error=''"></button>
    </div>

    <div class="row">
      <div class="col-md-6">
        <div class="card mb-3">
          <div class="card-header">{{ t('dashboard.card.focusDurationTop10') }}</div>
          <div class="card-body"><canvas ref="focusChart" height="300"></canvas></div>
        </div>
      </div>
      <div class="col-md-6">
        <div class="card mb-3">
          <div class="card-header">{{ t('dashboard.card.switchCount') }}</div>
          <div class="card-body"><canvas ref="switchChart" height="300"></canvas></div>
        </div>
      </div>
    </div>

    <div class="row">
      <div class="col-md-6">
        <div class="card mb-3">
          <div class="card-header">{{ t('dashboard.card.overview') }}
            <small class="text-muted ms-2" v-if="totalSleepSeconds > 0">{{ t('dashboard.sleepOff', { duration: fmtDuration(totalSleepSeconds) }) }}</small>
            <button class="btn btn-sm btn-outline-secondary float-end" @click="toggleSort" :title="t('dashboard.sortToggle')">
              {{ sortAsc ? t('dashboard.sortAsc') : t('dashboard.sortDesc') }}
            </button>
          </div>
          <div class="card-body" style="max-height:400px;overflow-y:auto">
            <table class="table table-sm table-striped">
              <thead><tr><th>{{ t('dashboard.table.process') }}</th><th>{{ t('dashboard.table.totalDuration') }}</th><th>{{ t('dashboard.table.switches') }}</th></tr></thead>
              <tbody>
                <tr v-for="d in sortedSummary" :key="d.processName">
                  <td><strong>{{ d.processName }}</strong></td>
                  <td>{{ fmtDuration(d.totalSeconds) }}</td>
                  <td>{{ mergeSameProcess ? (d.adjustedSwitchCount ?? d.switchCount) : d.switchCount }}</td>
                </tr>
                <tr v-if="!summary.length"><td colspan="3" class="text-muted">{{ t('dashboard.table.noData') }}</td></tr>
              </tbody>
            </table>
          </div>
        </div>
      </div>
      <div class="col-md-6">
        <div class="card mb-3">
          <div class="card-header">{{ t('dashboard.card.recentMedia') }} <small class="text-muted">{{ t('dashboard.totalListen', { duration: totalListenFmt }) }}</small></div>
          <div class="card-body" style="max-height:400px;overflow-y:auto;padding:0">
            <table class="table table-sm mb-0">
              <thead><tr><th>{{ t('dashboard.media.duration') }}</th><th>{{ t('dashboard.media.status') }}</th><th>{{ t('dashboard.media.song') }}</th><th>{{ t('dashboard.media.artist') }}</th></tr></thead>
              <tbody>
                <tr v-for="m in displayMedia.slice().reverse()" :key="m.id || m.startTime"
                  :style="{borderLeft:'4px solid '+(m.playbackStatus==='Playing'?'#198754':'#dee2e6')}">
                  <td>{{ m.durationFmt }}</td>
                  <td>{{ m.playbackStatus === 'Playing' ? '▶' : '⏸' }}</td>
                  <td>{{ m.title }}</td>
                  <td>{{ m.artist }}</td>
                </tr>
                <tr v-if="!displayMedia.length"><td colspan="4" class="text-muted">{{ t('dashboard.media.noData') }}</td></tr>
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
import { useI18n } from '../i18n/index.js'

import { Chart, registerables } from 'chart.js'

Chart.register(...registerables)

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
  list.sort((a, b) => sortAsc.value
    ? a.totalSeconds - b.totalSeconds
    : b.totalSeconds - a.totalSeconds)
  return list
})

const totalListenFmt = computed(() => {
  const playing = displayMedia.value.filter(m => m.playbackStatus === 'Playing')
  if (!playing.length) return '0s'
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

const focusChart = ref(null)
const switchChart = ref(null)
let fc = null, sc = null
let timer = null

onMounted(async () => {
  try {
    const r = await fetch(`${apiBase}/api/settings`)
    if (r.ok) {
      const s = await r.json()
      mergeSameProcess.value = s.mergeSameProcessSwitches ?? true
    }
  } catch (e) { console.error('Failed to load settings for dashboard:', e) }
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
  } catch (e) { console.error(e); error.value = t('dashboard.error.loadSummaryFailed', { message: e.message }) }
}

async function fetchMedia() {
  try {
    const [fromDate, toDate] = periodRange()
    const limits = { today: 50, week: 200, month: 500, year: 2000, all: 5000 }
    const limit = limits[period.value] || 50
    const r = await fetch(`${apiBase}/api/media/history?limit=${limit}&from=${fromDate}&to=${toDate}`)
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
          label: t('dashboard.chart.durationLabel'),
          data: focusData,
          backgroundColor: colors
        }]
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        plugins: { legend: { display: false } },
        scales: { y: { beginAtZero: true, title: { display: true, text: t('dashboard.chart.durationAxis') } } }
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
          label: t('dashboard.chart.switchLabel'),
          data: switchData,
          backgroundColor: colors
        }]
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        plugins: { legend: { display: false } },
        scales: { y: { beginAtZero: true, title: { display: true, text: t('dashboard.chart.switchAxis') } } }
      }
    })
  }
}
</script>
