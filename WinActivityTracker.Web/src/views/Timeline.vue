<!--
  Timeline view — focus change log + live window list. Auto-refreshes every 2s.
-->
<template>
  <div>
    <div class="row mb-3">
      <div class="col-md-3">
        <label class="form-label">{{ t('timeline.startTime') }}</label>
        <input type="datetime-local" v-model="fromTime" class="form-control" />
      </div>
      <div class="col-md-3">
        <label class="form-label">{{ t('timeline.endTime') }}</label>
        <input type="datetime-local" v-model="toTime" class="form-control" />
      </div>
      <div class="col-md-2 d-flex align-items-end">
        <button class="btn btn-primary" @click="loadTimeline">{{ t('common.query') }}</button>
      </div>
      <div class="col-md-2 d-flex align-items-end">
        <button class="btn btn-sm btn-outline-secondary" @click="toggleSort">
          {{ sortAsc ? t('timeline.sortAsc') : t('timeline.sortDesc') }}
        </button>
      </div>
    </div>

    <div v-if="error" class="alert alert-danger alert-dismissible fade show" role="alert">
      {{ error }}
      <button type="button" class="btn-close" @click="error=''"></button>
    </div>

    <div class="row">
      <div class="col-md-8">
        <div class="card mb-3">
          <div class="card-header">{{ t('timeline.card.focusTimeline') }}</div>
          <div class="card-body" style="max-height:600px;overflow-y:auto">
            <table class="table table-sm table-hover">
              <thead>
                <tr><th>{{ t('timeline.table.time') }}</th><th>{{ t('timeline.table.process') }}</th><th>{{ t('timeline.table.windowTitle') }}</th><th>{{ t('timeline.table.duration') }}</th></tr>
              </thead>
              <tbody>
                <tr v-for="d in sortedTimeline" :key="d.timestamp + '|' + d.processName">
                  <td>{{ toLocal(d.timestamp) }}</td>
                  <td><strong>{{ d.processName }}</strong></td>
                  <td>{{ d.windowTitle }}</td>
                  <td>{{ d.durationSeconds.toFixed(1) }}s</td>
                </tr>
                <tr v-if="!timeline.length"><td colspan="4" class="text-muted">{{ t('timeline.noData') }}</td></tr>
              </tbody>
            </table>
          </div>
        </div>
      </div>
      <div class="col-md-4">
        <div class="card mb-3">
          <div class="card-header">{{ t('timeline.card.visibleWindows') }} <small class="text-muted">{{ t('timeline.card.visibleWindowsRefresh') }}</small></div>
          <div class="card-body" style="max-height:600px;overflow-y:auto">
            <table class="table table-sm">
              <thead><tr><th>{{ t('timeline.table.process') }}</th><th>{{ t('timeline.table.title') }}</th><th>{{ t('timeline.table.focus') }}</th></tr></thead>
              <tbody>
                <tr v-for="w in windows" :key="w.processName + '|' + w.title" :class="{ 'table-primary': w.isFocused }">
                  <td>{{ w.processName }}</td>
                  <td>{{ w.title }}</td>
                  <td>{{ w.isFocused ? '✅' : '' }}</td>
                </tr>
                <tr v-if="!windows.length"><td colspan="3" class="text-muted">{{ t('common.loading') }}</td></tr>
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
import { toLocalTime as toLocal, toLocalDatetimeString } from '../utils/time.js'
import { useI18n } from '../i18n/index.js'

const apiBase = inject('apiBase')
const { t } = useI18n()

const fromTime = ref(toLocalDatetimeString(new Date(Date.now() - 3600000)))
const toTime = ref(toLocalDatetimeString(new Date()))
const timeline = ref([])
const windows = ref([])
const sortAsc = ref(true)
const error = ref('')

let timer = null

onMounted(() => {
  loadTimeline()
  loadWindows()
  timer = setInterval(() => { loadTimeline(); loadWindows() }, 2000)
})

onUnmounted(() => clearInterval(timer))

const sortedTimeline = computed(() =>
  sortAsc.value ? timeline.value : [...timeline.value].reverse()
)

function toggleSort() {
  sortAsc.value = !sortAsc.value
}

async function loadTimeline() {
  try {
    const r = await fetch(`${apiBase}/api/windows/timeline?from=${fromTime.value}&to=${toTime.value}&limit=2000`)
    if (!r.ok) throw new Error(`API ${r.status}`)
    const resp = await r.json()
    timeline.value = resp.data || resp
    error.value = ''
  } catch (e) { console.error(e); error.value = t('timeline.error.loadFailed', { message: e.message }) }
}

async function loadWindows() {
  try {
    const r = await fetch(`${apiBase}/api/windows/current`)
    if (!r.ok) throw new Error(`API ${r.status}`)
    windows.value = await r.json()
  } catch (e) { console.error('Failed to load windows:', e) }
}
</script>
