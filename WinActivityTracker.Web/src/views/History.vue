<!--
  History view — query focus data over a custom date range.
  Aggregates all FocusChange records between two dates, grouped by process.
  Results are sorted by total duration descending.

  Timezone: DB timestamps are UTC without 'Z' suffix (EF Core strips Kind).
  The toLocal() helper corrects for this.

  The data table is scrollable (max-height: 500px) for long result sets.
-->
<template>
  <div>
    <div class="row mb-3">
      <div class="col-md-3">
        <label class="form-label">开始日期</label>
        <input type="date" v-model="fromDate" class="form-control" />
      </div>
      <div class="col-md-3">
        <label class="form-label">结束日期</label>
        <input type="date" v-model="toDate" class="form-control" />
      </div>
      <div class="col-md-2 d-flex align-items-end">
        <button class="btn btn-primary" @click="loadData">查询</button>
      </div>
    </div>

    <div class="card mb-3">
      <div class="card-header">时段汇总</div>
      <div class="card-body" style="max-height:600px;overflow-y:auto">
        <div class="table-responsive">
          <table class="table table-striped table-hover">
            <thead>
              <tr><th>程序</th><th>总时长</th><th>切换次数</th></tr>
            </thead>
            <tbody>
              <tr v-for="d in data" :key="d.processName">
                <td><strong>{{ d.processName }}</strong></td>
                <td>{{ fmtDuration(d.totalSeconds) }}</td>
                <td>{{ d.switchCount }}</td>
              </tr>
              <tr v-if="!data.length"><td colspan="3" class="text-muted">暂无数据 — 请选择一个有数据的日期范围</td></tr>
            </tbody>
          </table>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, inject, onMounted } from 'vue'

const apiBase = inject('apiBase')

// Default range: start of current month → today
const fromDate = ref(new Date(new Date().getFullYear(), new Date().getMonth(), 1).toISOString().slice(0, 10))
const toDate = ref(new Date().toISOString().slice(0, 10))
const data = ref([])

onMounted(loadData)

async function loadData() {
  try {
    // Append T23:59:59 so the query includes the full end-date
    const r = await fetch(`${apiBase}/api/summary/range?from=${fromDate.value}&to=${toDate.value}T23:59:59`)
    // Sort client-side: API returns by TotalSeconds desc, but a second sort ensures consistency
    data.value = (await r.json()).sort((a, b) => b.totalSeconds - a.totalSeconds)
  } catch (e) { console.error(e) }
}

function fmtDuration(s) {
  if (s < 60) return `${s.toFixed(0)}秒`
  if (s < 3600) return `${(s / 60).toFixed(1)}分钟`
  return `${(s / 3600).toFixed(1)}小时`
}
</script>
