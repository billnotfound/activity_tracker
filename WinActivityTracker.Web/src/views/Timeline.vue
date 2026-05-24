<!--
  Timeline view — chronological focus change log + live window list.
  Features:
    - Datetime-local pickers for precise time range filtering
    - Focus change table (scrollable) showing every recorded window switch with duration
    - Live "current windows" panel (scrollable) that auto-refreshes every 3 seconds
      (this panel uses the real-time /api/windows/current endpoint, not the DB)

  Timezone: DB timestamps are UTC without 'Z' suffix (EF Core strips Kind).
  toLocal() converts to local time; for datetime-local inputs we use local values directly.

  The live panel polls every 3s — this matches the default WindowTracker interval.
  More frequent polling would just repeat the same data.
-->
<template>
  <div>
    <div class="row mb-3">
      <div class="col-md-3">
        <label class="form-label">开始时间</label>
        <input type="datetime-local" v-model="fromTime" class="form-control" />
      </div>
      <div class="col-md-3">
        <label class="form-label">结束时间</label>
        <input type="datetime-local" v-model="toTime" class="form-control" />
      </div>
      <div class="col-md-2 d-flex align-items-end">
        <button class="btn btn-primary" @click="loadTimeline">查询</button>
      </div>
    </div>

    <div class="row">
      <div class="col-md-8">
        <div class="card mb-3">
          <div class="card-header">焦点窗口时间线</div>
          <div class="card-body" style="max-height:600px;overflow-y:auto">
            <table class="table table-sm table-hover">
              <thead>
                <tr><th>时间</th><th>程序</th><th>窗口标题</th><th>持续</th></tr>
              </thead>
              <tbody>
                <tr v-for="d in timeline" :key="d.timestamp">
                  <td>{{ toLocal(d.timestamp) }}</td>
                  <td><strong>{{ d.processName }}</strong></td>
                  <td>{{ d.windowTitle }}</td>
                  <td>{{ d.durationSeconds.toFixed(1) }}s</td>
                </tr>
                <tr v-if="!timeline.length"><td colspan="4" class="text-muted">暂无数据 — 请切换到有活动的时间范围</td></tr>
              </tbody>
            </table>
          </div>
        </div>
      </div>
      <div class="col-md-4">
        <div class="card mb-3">
          <div class="card-header">当前可见窗口 <small class="text-muted">(每 3s 刷新)</small></div>
          <div class="card-body" style="max-height:600px;overflow-y:auto">
            <table class="table table-sm">
              <thead><tr><th>程序</th><th>标题</th><th>焦点</th></tr></thead>
              <tbody>
                <tr v-for="w in windows" :key="w.title" :class="{ 'table-primary': w.isFocused }">
                  <td>{{ w.processName }}</td>
                  <td>{{ w.title }}</td>
                  <td>{{ w.isFocused ? '✅' : '' }}</td>
                </tr>
                <tr v-if="!windows.length"><td colspan="3" class="text-muted">加载中...</td></tr>
              </tbody>
            </table>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, inject, onMounted, onUnmounted } from 'vue'

const apiBase = inject('apiBase')

// Default time range: last 1 hour (in local time)
const fromTime = ref(new Date(Date.now() - 3600000).toISOString().slice(0, 16))
const toTime = ref(new Date().toISOString().slice(0, 16))
const timeline = ref([])
const windows = ref([])

let timer = null

onMounted(() => {
  loadTimeline()
  loadWindows()
  // Live refresh the current-windows panel every 3s
  timer = setInterval(loadWindows, 3000)
})

// Clean up the interval to prevent memory leaks when navigating away
onUnmounted(() => clearInterval(timer))

async function loadTimeline() {
  try {
    const r = await fetch(`${apiBase}/api/windows/timeline?from=${fromTime.value}&to=${toTime.value}`)
    timeline.value = await r.json()
  } catch {}
}

async function loadWindows() {
  try {
    const r = await fetch(`${apiBase}/api/windows/current`)
    windows.value = await r.json()
  } catch {}
}

// DB timestamps are UTC without 'Z' suffix (EF Core strips DateTimeKind).
// Append 'Z' so JavaScript parses as UTC, then display in local time.
function toLocal(ts) {
  if (!ts) return '-'
  return new Date(ts.endsWith('Z') ? ts : ts + 'Z').toLocaleTimeString()
}
</script>
