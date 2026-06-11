<!--
  History view — query focus data over a custom date range.
  Aggregates all FocusChange records between two dates, grouped by process.
  Supports ascending/descending sort by duration.
-->
<template>
  <div>
    <div class="row mb-3">
      <div class="col-md-3">
        <label class="form-label">{{ t('history.startDate') }}</label>
        <input type="date" v-model="fromDate" class="form-control" />
      </div>
      <div class="col-md-3">
        <label class="form-label">{{ t('history.endDate') }}</label>
        <input type="date" v-model="toDate" class="form-control" />
      </div>
      <div class="col-md-2 d-flex align-items-end">
        <button class="btn btn-primary" @click="loadData">{{ t('common.query') }}</button>
      </div>
      <div class="col-md-2 d-flex align-items-end">
        <button class="btn btn-sm btn-outline-secondary" @click="toggleSort">
          {{ sortAsc ? t('history.sortAsc') : t('history.sortDesc') }}
        </button>
      </div>
    </div>

    <div class="card mb-3">
      <div class="card-header">{{ t('history.card.periodSummary') }} <small class="text-muted">{{ t('history.card.programCount', { count: data.length }) }}</small>
        <small class="text-muted ms-2" v-if="totalSleepSeconds > 0">{{ t('history.sleepOff', { duration: fmtDuration(totalSleepSeconds) }) }}</small>
      </div>
      <div class="card-body" style="max-height:600px;overflow-y:auto">
        <div class="table-responsive">
          <table class="table table-striped table-hover">
            <thead>
              <tr><th>{{ t('history.table.process') }}</th><th>{{ t('history.table.totalDuration') }}</th><th>{{ t('history.table.switches') }}</th></tr>
            </thead>
            <tbody>
              <tr v-for="d in sortedData" :key="d.processName">
                <td><strong>{{ d.processName }}</strong></td>
                <td>{{ fmtDuration(d.totalSeconds) }}</td>
                <td>{{ mergeSameProcess ? (d.adjustedSwitchCount ?? d.switchCount) : d.switchCount }}</td>
              </tr>
              <tr v-if="!data.length"><td colspan="3" class="text-muted">{{ t('history.noData') }}</td></tr>
            </tbody>
          </table>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, inject, onMounted, computed } from 'vue'
import { toLocalDateString, fmtDuration } from '../utils/time.js'
import { useI18n } from '../i18n/index.js'

const apiBase = inject('apiBase')
const { t } = useI18n()

const fromDate = ref(toLocalDateString(new Date(new Date().getFullYear(), new Date().getMonth(), 1)))
const toDate = ref(toLocalDateString(new Date()))
const data = ref([])
const sortAsc = ref(false)
const mergeSameProcess = ref(true)
const totalSleepSeconds = ref(0)

onMounted(async () => {
  try {
    const r = await fetch(`${apiBase}/api/settings`)
    if (r.ok) {
      const s = await r.json()
      mergeSameProcess.value = s.mergeSameProcessSwitches ?? true
    }
  } catch (e) { console.error('Failed to load settings for history:', e) }
  await loadData()
})

const sortedData = computed(() => {
  const arr = [...data.value]
  arr.sort((a, b) => sortAsc.value
    ? a.totalSeconds - b.totalSeconds
    : b.totalSeconds - a.totalSeconds)
  return arr
})

function toggleSort() {
  sortAsc.value = !sortAsc.value
}

async function loadData() {
  try {
    const r = await fetch(`${apiBase}/api/summary/range?from=${fromDate.value}&to=${toDate.value}T23:59:59`)
    if (!r.ok) throw new Error(`API ${r.status}`)
    const res = await r.json()
    data.value = Array.isArray(res) ? res : (res.items || [])
    totalSleepSeconds.value = Array.isArray(res) ? 0 : (res.totalSleepSeconds || 0)
  } catch (e) { console.error(e) }
}
</script>
