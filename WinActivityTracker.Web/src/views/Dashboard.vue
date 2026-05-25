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
          <div class="card-body" style="max-height:400px;overflow-y:auto">
            <table class="table table-sm">
              <thead><tr><th>时间</th><th>歌曲</th><th>艺术家</th></tr></thead>
              <tbody>
                <tr v-for="m in mergedMedia" :key="m.key">
                  <td>{{ toLocal(m.timestamp) }}</td>
                  <td>{{ m.title }}</td>
                  <td>{{ m.artist }}</td>
                </tr>
                <tr v-if="!mergedMedia.length"><td colspan="3" class="text-muted">暂无数据</td></tr>
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

// Merge consecutive non-playing media entries into single "paused" rows.
// The API returns records in time-desc order; we reverse for chronological processing
// then reverse back so newest entries appear first.
const mergedMedia = computed(() => {
  const raw = [...media.value].reverse()  // chronological order
  const merged = []
  let pauseStart = null
  let pauseEnd = null

  for (const m of raw) {
    const isPlaying = m.playbackStatus === 'Playing'
    if (isPlaying) {
      if (pauseStart) {
        merged.push({
          key: `pause-${pauseStart}`, timestamp: pauseStart,
          title: '— 未播放 —', artist: `${fmtRange(pauseStart, pauseEnd || pauseStart)}`,
        })
        pauseStart = null; pauseEnd = null
      }
      merged.push({ ...m, key: `m-${m.id || m.timestamp}` })
    } else {
      if (!pauseStart) pauseStart = m.timestamp
      pauseEnd = m.timestamp
    }
  }
  // trailing pause period
  if (pauseStart) {
    merged.push({
      key: `pause-${pauseStart}`, timestamp: pauseStart,
      title: '— 未播放 —', artist: `${fmtRange(pauseStart, pauseEnd || pauseStart)}`,
    })
  }
  return merged.reverse()  // back to newest-first
})

function fmtRange(a, b) {
  if (a === b) return new Date(a + 'Z').toLocaleTimeString()
  return `${new Date(b + 'Z').toLocaleTimeString()} ~ ${new Date(a + 'Z').toLocaleTimeString()}`
}

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
