<!--
  Dashboard view — today's usage overview.
  Features:
    - Date picker to view any day's data
    - Two Chart.js bar charts: focus duration and switch count (Top 10)
    - Full data table with all tracked processes (scrollable)
    - Recent media playback list (scrollable)

  Chart lifecycle: charts are destroyed and re-created on each data load.
  Chart.js requires explicit destroy() before replacing to avoid canvas conflicts
  and memory leaks from orphaned chart instances.

  Timezone: DB timestamps are UTC (EF Core strips DateTimeKind, so no 'Z' suffix).
  The toLocal() helper appends 'Z' before parsing, ensuring correct local-time display.
-->
<template>
  <div>
    <div class="row mb-3">
      <div class="col text-center">
        <input type="date" v-model="date" class="form-control d-inline-block w-auto" />
        <button class="btn btn-primary btn-sm ms-2" @click="loadSummary">查询</button>
      </div>
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
          <div class="card-header">统计概览</div>
          <div class="card-body" style="max-height:400px;overflow-y:auto">
            <table class="table table-sm table-striped">
              <thead><tr><th>程序</th><th>总时长</th><th>切换次数</th></tr></thead>
              <tbody>
                <tr v-for="d in summary" :key="d.processName">
                  <td><strong>{{ d.processName }}</strong></td>
                  <td>{{ fmtDuration(d.totalSeconds) }}</td>
                  <td>{{ d.switchCount }}</td>
                </tr>
                <tr v-if="!summary.length"><td colspan="3" class="text-muted">暂无数据</td></tr>
              </tbody>
            </table>
          </div>
        </div>
      </div>
      <div class="col-md-6">
        <div class="card mb-3">
          <div class="card-header">最近媒体播放</div>
          <div class="card-body" style="max-height:400px;overflow-y:auto;padding:0">
            <table class="table table-sm mb-0">
              <thead><tr><th>持续</th><th>状态</th><th>歌曲</th><th>艺术家</th></tr></thead>
              <tbody>
                <tr v-for="m in mediaTimeline" :key="m.id || m.timestamp"
                  :style="{borderLeft:'4px solid '+(m.playbackStatus==='Playing'?'#198754':'#dee2e6')}">
                  <td>{{ m.durationFmt }}</td>
                  <td>{{ m.playbackStatus === 'Playing' ? '▶' : '⏸' }}</td>
                  <td>{{ m.title }}</td>
                  <td>{{ m.artist }}</td>
                </tr>
                <tr v-if="!media.length"><td colspan="4" class="text-muted">暂无数据</td></tr>
              </tbody>
            </table>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, inject, onMounted, computed } from 'vue'
import { Chart, registerables } from 'chart.js'

Chart.register(...registerables)

const apiBase = inject('apiBase')

const date = ref(new Date().toISOString().slice(0, 10))
const summary = ref([])
const media = ref([])

// Duration computed as gap to next record (or now for latest).
const mediaWithDuration = computed(() => {
  const list = [...media.value].reverse()
  const now = Date.now()
  const r = list.map((m, i) => {
    const t1 = new Date((m.timestamp.endsWith('Z')?m.timestamp:m.timestamp+'Z')).getTime()
    const t2 = i+1 < list.length
      ? new Date((list[i+1].timestamp.endsWith('Z')?list[i+1].timestamp:list[i+1].timestamp+'Z')).getTime()
      : now
    return { ...m, durationSec: Math.max(1,Math.round((t2-t1)/1000)), durationFmt: fmtShortDur(Math.max(1,Math.round((t2-t1)/1000))) }
  })
  return r.reverse()
})
const mediaTimeline = computed(() => {
  const total = mediaWithDuration.value.reduce((s,m)=>s+m.durationSec,0)||1
  return mediaWithDuration.value.map(m=>({...m, pct: Math.max(1,(m.durationSec/total)*100)}))
})
function fmtShortDur(s) { return s<60?s+'s':s<3600?Math.round(s/60)+'m':(s/3600).toFixed(1)+'h' }

// Template refs for the two canvas elements
const focusChart = ref(null)
const switchChart = ref(null)
let fc = null, sc = null  // Chart.js instance references for destroy/recreate

onMounted(loadSummary)

async function loadSummary() {
  await Promise.all([fetchSummary(), fetchMedia()])
}

async function fetchSummary() {
  try {
    const r = await fetch(`${apiBase}/api/summary/today?date=${date.value}`)
    const data = await r.json()
    summary.value = data
    renderCharts(data)
  } catch (e) { console.error(e) }
}

async function fetchMedia() {
  try {
    const r = await fetch(`${apiBase}/api/media/history?limit=20`)
    media.value = await r.json()
  } catch {}
}

function renderCharts(data) {
  const top = data.slice(0, 10)
  const labels = top.map(d => d.processName)
  // Deterministic color rotation using HSL — same process always gets the same hue
  const colors = labels.map((_, i) => `hsl(${i * 37 % 360}, 60%, 55%)`)

  // Destroy previous chart instances before creating new ones.
  // If we don't, old charts persist in memory and the canvas becomes unresponsive.
  if (fc) fc.destroy()
  fc = new Chart(focusChart.value, {
    type: 'bar',
    data: {
      labels,
      datasets: [{
        label: '时长 (分钟)',
        data: top.map(d => +(d.totalSeconds / 60).toFixed(1)),
        backgroundColor: colors
      }]
    },
    options: {
      responsive: true,
      // Maintain aspect ratio prevents the chart from growing to fill height: 300
      maintainAspectRatio: false,
      plugins: { legend: { display: false } },
      scales: { y: { beginAtZero: true, title: { display: true, text: '分钟' } } }
    }
  })

  if (sc) sc.destroy()
  sc = new Chart(switchChart.value, {
    type: 'bar',
    data: {
      labels,
      datasets: [{
        label: '切换次数',
        data: top.map(d => d.switchCount),
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

// DB stores UTC; EF Core strips DateTimeKind so the JSON string has no 'Z' suffix.
// Append 'Z' so JavaScript parses as UTC, then format in local time.
function toLocal(ts) {
  if (!ts) return '-'
  return new Date(ts.endsWith('Z') ? ts : ts + 'Z').toLocaleTimeString()
}

// Human-readable duration formatting
function fmtDuration(s) {
  if (s < 60) return `${s.toFixed(0)}秒`
  if (s < 3600) return `${(s / 60).toFixed(1)}分钟`
  return `${(s / 3600).toFixed(1)}小时`
}
</script>
