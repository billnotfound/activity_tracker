<!--
  Settings page — full configuration UI for the backend.
-->
<template>
  <div>
    <h4 class="mb-3">{{ t('settings.pageTitle') }}</h4>

    <div class="alert" :class="statusClass" role="alert">
      {{ statusText }}
      <span v-if="saving" class="spinner-border spinner-border-sm ms-2"></span>
    </div>

    <div v-if="tagStatus.tags?.error" class="alert alert-danger small" role="alert">
      <strong>{{ t('settings.tagsError') }}</strong> {{ tagStatus.tags.error }}
    </div>
    <div v-if="tagStatus.titleRules?.error" class="alert alert-danger small" role="alert">
      <strong>{{ t('settings.titleRulesError') }}</strong> {{ tagStatus.titleRules.error }}
    </div>

    <div class="row">
      <div class="col-lg-8">
        <div class="card mb-3">
          <div class="card-header">{{ t('settings.card.language') }}</div>
          <div class="card-body">
            <select class="form-select w-auto" :value="locale" @change="onLangChange($event.target.value)">
              <option value="zh-CN">中文</option>
              <option value="en-US">English</option>
            </select>
          </div>
        </div>

        <div class="card mb-3">
          <div class="card-header">{{ t('settings.card.tracking') }}</div>
          <div class="card-body">
            <div class="form-check form-switch mb-3">
              <input class="form-check-input" type="checkbox" id="trackingEnabled" v-model="form.trackingEnabled" />
              <label class="form-check-label" for="trackingEnabled">
                <strong>{{ form.trackingEnabled ? t('settings.trackingEnabled') : t('settings.trackingPaused') }}</strong>
              </label>
              <div class="form-text">{{ t('settings.trackingDescription') }}</div>
            </div>

            <div class="row">
              <div class="col-md-4 mb-3">
                <label class="form-label">{{ t('settings.windowPollLabel') }}</label>
                <input type="number" class="form-control" v-model.number="form.windowPollSeconds" min="1" />
                <div class="form-text">{{ t('settings.windowPollHelp') }}</div>
              </div>
              <div class="col-md-4 mb-3">
                <label class="form-label">{{ t('settings.processPollLabel') }}</label>
                <input type="number" class="form-control" v-model.number="form.processPollSeconds" min="5" />
                <div class="form-text">{{ t('settings.processPollHelp') }}</div>
              </div>
              <div class="col-md-4 mb-3">
                <label class="form-label">{{ t('settings.mediaPollLabel') }}</label>
                <input type="number" class="form-control" v-model.number="form.mediaPollSeconds" min="1" />
                <div class="form-text">{{ t('settings.mediaPollHelp') }}</div>
              </div>
            </div>

            <div class="mb-3">
              <label class="form-label">{{ t('settings.idleThresholdLabel') }}</label>
              <input type="number" class="form-control w-auto" v-model.number="form.idleThresholdMinutes" min="1" />
              <div class="form-text">{{ t('settings.idleThresholdHelp') }}</div>
            </div>
            <div class="form-check mb-2">
              <input class="form-check-input" type="checkbox" id="fullscreenBypass" v-model="form.fullscreenBypassIdle" />
              <label class="form-check-label" for="fullscreenBypass">
                {{ t('settings.fullscreenBypassLabel') }}
              </label>
              <div class="form-text">{{ t('settings.fullscreenBypassHelp') }}</div>
            </div>
            <div class="form-check mb-2">
              <input class="form-check-input" type="checkbox" id="mergeSwitches" v-model="form.mergeSameProcessSwitches" />
              <label class="form-check-label" for="mergeSwitches">
                {{ t('settings.mergeSwitchesLabel') }}
              </label>
              <div class="form-text">{{ t('settings.mergeSwitchesHelp') }}</div>
            </div>
          </div>
        </div>

        <div class="card mb-3">
          <div class="card-header">{{ t('settings.card.exclusions') }}</div>
          <div class="card-body">
            <label class="form-label">{{ t('settings.exclusionsLabel') }}</label>
            <input type="text" class="form-control" v-model="excludeText" placeholder="explorer, SearchApp, TextInputHost" />
            <div class="form-text">{{ t('settings.exclusionsHelp') }}</div>
          </div>
        </div>

        <div class="card mb-3">
          <div class="card-header">{{ t('settings.card.server') }}</div>
          <div class="card-body">
            <div class="mb-3">
              <label class="form-label">{{ t('settings.apiPortLabel') }}</label>
              <input type="number" class="form-control w-auto" v-model.number="form.apiPort" min="1024" max="65535" />
              <div class="form-text">{{ t('settings.apiPortHelp') }}</div>
            </div>
          </div>
        </div>

        <div class="card mb-3">
          <div class="card-header">{{ t('settings.card.paths') }}</div>
          <div class="card-body">
            <div class="mb-3">
              <label class="form-label">{{ t('settings.configDirLabel') }}</label>
              <div class="input-group">
                <input type="text" class="form-control form-control-sm" v-model="pathForm.configDir"
                  :placeholder="pathCurrent.configDir" />
                <button class="btn btn-outline-secondary btn-sm" type="button" @click="pathForm.configDir = ''">{{ t('common.reset') }}</button>
              </div>
              <div class="form-text">{{ t('settings.pathCurrent', { path: pathCurrent.configDir }) }}</div>
            </div>
            <div class="mb-3">
              <label class="form-label">{{ t('settings.dataDirLabel') }}</label>
              <div class="input-group">
                <input type="text" class="form-control form-control-sm" v-model="pathForm.dataDir"
                  :placeholder="pathCurrent.dataDir" />
                <button class="btn btn-outline-secondary btn-sm" type="button" @click="pathForm.dataDir = ''">{{ t('common.reset') }}</button>
              </div>
              <div class="form-text">{{ t('settings.pathCurrent', { path: pathCurrent.dataDir }) }}</div>
            </div>
            <button class="btn btn-outline-primary btn-sm" @click="savePaths" :disabled="savingPaths">
              {{ savingPaths ? t('common.saving') : t('settings.savePaths') }}
            </button>
            <div v-if="pathSaved" class="alert alert-success small mt-2">{{ pathSaved }}</div>
            <div v-if="pathError" class="alert alert-danger small mt-2">{{ pathError }}</div>
          </div>
        </div>

        <div class="card mb-3">
          <div class="card-header">{{ t('settings.card.database') }}</div>
          <div class="card-body">
            <div class="mb-3">
              <label class="form-label">{{ t('settings.retentionLabel') }}</label>
              <input type="number" class="form-control w-auto" v-model.number="form.dataRetentionDays" min="1" />
              <div class="form-text">{{ t('settings.retentionHelp') }}</div>
            </div>
            <div class="mb-3">
              <button class="btn btn-outline-secondary btn-sm me-2" @click="loadDbStats">
                {{ t('settings.refreshStats') }}
              </button>
              <button class="btn btn-outline-danger btn-sm me-2" @click="runCleanup" :disabled="cleaning">
                {{ cleaning ? t('settings.cleaning') : t('settings.cleanupNow') }}
              </button>
              <button v-if="!resetConfirm" class="btn btn-outline-danger btn-sm" @click="resetConfirm = true">
                {{ t('settings.deleteAll') }}
              </button>
              <span v-else>
                <button class="btn btn-danger btn-sm me-1" @click="runReset" :disabled="resetting">
                  {{ resetting ? t('settings.deleting') : t('settings.confirmDelete') }}
                </button>
                <button class="btn btn-outline-secondary btn-sm" @click="resetConfirm = false">{{ t('common.cancel') }}</button>
              </span>
            </div>
            <div v-if="dbStats" class="small">
              <table class="table table-sm">
                <tr><td>{{ t('settings.dbStats.focusChanges') }}</td><td>{{ dbStats.focusChanges?.toLocaleString() }}</td></tr>
                <tr><td>{{ t('settings.dbStats.windowSnapshots') }}</td><td>{{ dbStats.windowSnapshots?.toLocaleString() }}</td></tr>
                <tr><td>{{ t('settings.dbStats.windowSessions') }}</td><td>{{ dbStats.windowSessions?.toLocaleString() }}</td></tr>
                <tr><td>{{ t('settings.dbStats.processSessions') }}</td><td>{{ dbStats.processSessions?.toLocaleString() }}</td></tr>
                <tr><td>{{ t('settings.dbStats.mediaRecords') }}</td><td>{{ dbStats.mediaRecords }}</td></tr>
                <tr><td>{{ t('settings.dbStats.oldestRecord') }}</td><td>{{ fmtLocal(dbStats.oldestRecord) }}</td></tr>
              </table>
            </div>
            <div v-if="cleanupResult" class="alert alert-success small mt-2">
              {{ t('settings.cleanupDone', {
                focus: cleanupResult.deleted.focusChanges,
                windows: cleanupResult.deleted.windowSessions,
                processes: cleanupResult.deleted.processSessions,
                media: cleanupResult.deleted.mediaRecords
              }) }}
            </div>
            <div v-if="resetResult" class="alert alert-warning small mt-2">
              {{ t('settings.resetDone', {
                focus: resetResult.deleted.focusChanges,
                windows: resetResult.deleted.windowSessions,
                processes: resetResult.deleted.processSessions,
                media: resetResult.deleted.mediaRecords
              }) }}
            </div>
          </div>
        </div>

        <button class="btn btn-primary btn-lg" @click="saveSettings" :disabled="saving">
          {{ saving ? t('common.saving') : t('settings.saveSettings') }}
        </button>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, reactive, inject, onMounted, computed } from 'vue'
import { toLocalString as fmtLocal } from '../utils/time.js'
import { useI18n } from '../i18n/index.js'

const apiBase = inject('apiBase')
const { t, locale, setLocale } = useI18n()

function onLangChange(lang) { setLocale(lang) }

const form = reactive({
  trackingEnabled: true,
  fullscreenBypassIdle: true,
  mergeSameProcessSwitches: true,
  windowPollSeconds: 3,
  processPollSeconds: 30,
  mediaPollSeconds: 5,
  idleThresholdMinutes: 2,
  dataRetentionDays: 90,
  apiPort: 5200,
  autoCleanup: true,
  autoStartEnabled: false
})
const excludeText = ref('')
const saving = ref(false)
const cleaning = ref(false)
const dbStats = ref(null)
const cleanupResult = ref(null)
const resetConfirm = ref(false)
const resetting = ref(false)
const resetResult = ref(null)
const tagStatus = ref({})
const pathCurrent = ref({ configDir: '', dataDir: '' })
const pathForm = reactive({ configDir: '', dataDir: '' })
const savingPaths = ref(false)
const pathSaved = ref('')
const pathError = ref('')
const statusOk = ref(false)

const statusClass = computed(() => statusOk.value ? 'alert-success' : 'alert-warning')
const statusText = computed(() => statusOk.value ? t('settings.connectionStatus.connected') : t('settings.connectionStatus.disconnected'))

onMounted(async () => {
  await loadSettings()
  await loadDbStats()
  await loadTagStatus()
  await loadPaths()
})

async function loadSettings() {
  try {
    const r = await fetch(`${apiBase}/api/status`)
    if (!r.ok) return
    const s = await r.json()
    statusOk.value = true
    form.trackingEnabled = s.trackingEnabled
  } catch { statusOk.value = false }

  try {
    const r = await fetch(`${apiBase}/api/settings`)
    if (!r.ok) return
    const s = await r.json()
    form.trackingEnabled = s.trackingEnabled
    form.fullscreenBypassIdle = s.fullscreenBypassIdle ?? true
    form.mergeSameProcessSwitches = s.mergeSameProcessSwitches ?? true
    form.windowPollSeconds = s.windowPollSeconds
    form.processPollSeconds = s.processPollSeconds
    form.mediaPollSeconds = s.mediaPollSeconds
    form.idleThresholdMinutes = s.idleThresholdMinutes
    form.dataRetentionDays = s.dataRetentionDays
    form.apiPort = s.apiPort || 5200
    form.autoCleanup = s.autoCleanup ?? true
    form.autoStartEnabled = s.autoStartEnabled ?? false
    excludeText.value = (s.excludedProcesses || []).join(', ')
    statusOk.value = true
  } catch { statusOk.value = false }
}

async function saveSettings() {
  saving.value = true
  cleanupResult.value = null
  resetResult.value = null
  try {
    const body = {
      ...form,
      excludedProcesses: excludeText.value
        .split(',')
        .map(s => s.trim())
        .filter(s => s.length > 0)
    }
    const r = await fetch(`${apiBase}/api/settings`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body)
    })
    if (r.ok) {
      const s = await r.json()
      excludeText.value = (s.excludedProcesses || []).join(', ')
      statusOk.value = true
    } else {
      const err = await r.text()
      console.error('Save failed:', r.status, err)
      alert(t('settings.error.saveSettings', { status: r.status, detail: err }))
    }
  } catch (e) {
    console.error('Save error:', e)
    alert(t('error.backendUnreachable', { message: e.message }))
  }
  saving.value = false
}

async function loadDbStats() {
  try {
    const r = await fetch(`${apiBase}/api/db/stats`)
    if (r.ok) dbStats.value = await r.json()
  } catch (e) { console.error('Failed to load DB stats:', e) }
}

async function runCleanup() {
  cleaning.value = true
  cleanupResult.value = null
  try {
    const r = await fetch(`${apiBase}/api/db/cleanup?days=${form.dataRetentionDays}`, { method: 'POST' })
    if (r.ok) cleanupResult.value = await r.json()
  } catch (e) { console.error('Cleanup failed:', e) }
  cleaning.value = false
  await loadDbStats()
}

async function runReset() {
  resetting.value = true
  resetResult.value = null
  try {
    const r = await fetch(`${apiBase}/api/db/reset?confirm=true`, { method: 'POST' })
    if (r.ok) resetResult.value = await r.json()
  } catch (e) { console.error('Reset failed:', e) }
  resetting.value = false
  resetConfirm.value = false
  await loadDbStats()
}

async function loadTagStatus() {
  try {
    const r = await fetch(`${apiBase}/api/tags/status`)
    if (r.ok) tagStatus.value = await r.json()
  } catch (e) { console.error('Failed to load tag status:', e) }
}

async function loadPaths() {
  try {
    const r = await fetch(`${apiBase}/api/paths`)
    if (r.ok) {
      const data = await r.json()
      pathCurrent.value = { configDir: data.configDir, dataDir: data.dataDir }
      pathForm.configDir = data.registry?.configDir || data.configDir || ''
      pathForm.dataDir = data.registry?.dataDir || data.dataDir || ''
    }
  } catch (e) { console.error('loadPaths:', e) }
}

async function savePaths() {
  savingPaths.value = true
  pathSaved.value = ''
  pathError.value = ''
  try {
    const r = await fetch(`${apiBase}/api/paths`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        configDir: pathForm.configDir || null,
        dataDir: pathForm.dataDir || null
      })
    })
    if (r.ok) {
      const data = await r.json()
      pathSaved.value = data.message
    } else {
      const data = await r.json().catch(() => ({}))
      pathError.value = data.error || t('settings.error.savePaths', { status: r.status })
    }
  } catch (e) { pathError.value = t('error.backendUnreachable', { message: e.message }) }
  savingPaths.value = false
}
</script>
