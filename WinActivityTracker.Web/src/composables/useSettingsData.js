import { computed, reactive, ref } from 'vue'
import { initializeActivityDay } from './useActivityDay.js'

export function useSettingsData({ apiBase, t }) {
  const form = reactive({
    trackingEnabled: true,
    fullscreenBypassIdle: true,
    mergeSameProcessSwitches: true,
    windowPollSeconds: 3,
    processPollSeconds: 30,
    mediaPollSeconds: 5,
    idleThresholdMinutes: 2,
    dataRetentionDays: 365,
    apiPort: 32579,
    useNtp: true,
    timeServer: 'pool.ntp.org',
    timeSourceMode: 'Sntp',
    timeAnomalyThresholdSeconds: 180,
    ntpEpsilonSeconds: 5,
  })
  const timeSourceModes = [
    { value: 'Sntp', label: 'Sntp' },
    { value: 'Http', label: 'Http' },
  ]
  const excludeText = ref('')
  const loaded = ref(false)
  const feedback = ref('')
  const feedbackTone = ref('info')
  const iconCacheTone = ref('success')
  const saving = ref(false)
  const cleaning = ref(false)
  const clearingIcons = ref(false)
  const dbStats = ref(null)
  const cleanupResult = ref(null)
  const iconCacheResult = ref(null)
  const resetConfirm = ref(false)
  const resetting = ref(false)
  const resetResult = ref(null)
  const statusOk = ref(false)
  const statusClass = computed(() => (statusOk.value ? 'success' : 'warning'))
  const statusText = computed(() => statusOk.value
    ? t('settings.connectionStatus.connected')
    : t('settings.connectionStatus.disconnected'))

  async function loadSettings() {
    loaded.value = false
    try {
      const response = await fetch(`${apiBase}/api/status`)
      if (response.ok) {
        const status = await response.json()
        statusOk.value = true
        form.trackingEnabled = status.trackingEnabled
      } else {
        statusOk.value = false
      }
    } catch {
      statusOk.value = false
    }

    try {
      const response = await fetch(`${apiBase}/api/settings`)
      if (!response.ok) {
        statusOk.value = false
        return
      }
      const settings = await response.json()
      initializeActivityDay(settings)
      Object.assign(form, {
        trackingEnabled: settings.trackingEnabled,
        fullscreenBypassIdle: settings.fullscreenBypassIdle ?? true,
        mergeSameProcessSwitches: settings.mergeSameProcessSwitches ?? true,
        windowPollSeconds: settings.windowPollSeconds,
        processPollSeconds: settings.processPollSeconds,
        mediaPollSeconds: settings.mediaPollSeconds,
        idleThresholdMinutes: settings.idleThresholdMinutes,
        dataRetentionDays: settings.dataRetentionDays,
        apiPort: settings.apiPort || 32579,
        useNtp: settings.useNtp ?? true,
        timeServer: settings.timeServer || 'pool.ntp.org',
        timeSourceMode: settings.timeSourceMode || 'Sntp',
        timeAnomalyThresholdSeconds: settings.timeAnomalyThresholdSeconds ?? 180,
        ntpEpsilonSeconds: settings.ntpEpsilonSeconds ?? 5,
      })
      excludeText.value = (settings.excludedProcesses || []).join(', ')
      statusOk.value = true
      loaded.value = true
    } catch {
      statusOk.value = false
    }
  }

  async function saveSettings() {
    if (!loaded.value || saving.value) return
    feedback.value = ''
    saving.value = true
    try {
      const body = {
        ...form,
        excludedProcesses: excludeText.value.split(',').map(value => value.trim()).filter(Boolean),
      }
      const response = await fetch(`${apiBase}/api/settings`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body),
      })
      if (response.ok) {
        const settings = await response.json()
        for (const key of Object.keys(form)) if (settings[key] !== undefined) form[key] = settings[key]
        feedbackTone.value = 'success'
        feedback.value = t('settings.saved')
        excludeText.value = (settings.excludedProcesses || []).join(', ')
        statusOk.value = true
      } else {
        throw new Error(`HTTP ${response.status}`)
      }
    } catch (error) {
      feedbackTone.value = 'error'
      feedback.value = t('settings.operationFailed', { message: error.message })
    }
    saving.value = false
  }

  async function loadDbStats() {
    try {
      const response = await fetch(`${apiBase}/api/db/stats`)
      if (response.ok) dbStats.value = await response.json()
    } catch (error) {
      console.error('Failed to load DB stats:', error)
    }
  }

  async function runCleanup() {
    if (!loaded.value || !window.confirm(t('settings.cleanupConfirm', { days: form.dataRetentionDays }))) return
    cleaning.value = true
    cleanupResult.value = null
    try {
      const response = await fetch(`${apiBase}/api/db/cleanup?days=${form.dataRetentionDays}`, { method: 'POST' })
      if (!response.ok) throw new Error(`HTTP ${response.status}`)
      cleanupResult.value = await response.json()
    } catch (error) {
      feedbackTone.value = 'error'
      feedback.value = t('settings.operationFailed', { message: error.message })
    }
    cleaning.value = false
    await loadDbStats()
  }

  async function clearIconCache() {
    clearingIcons.value = true
    iconCacheResult.value = null
    try {
      const response = await fetch(`${apiBase}/api/icons/cache`, { method: 'DELETE' })
      iconCacheTone.value = response.ok ? 'success' : 'error'
      iconCacheResult.value = response.ok
        ? t('settings.database.iconCacheCleared')
        : t('settings.database.iconCacheClearFailed')
    } catch (error) {
      iconCacheTone.value = 'error'
      iconCacheResult.value = t('settings.database.iconCacheClearFailed')
    }
    clearingIcons.value = false
  }

  async function runReset() {
    if (!loaded.value || resetting.value) return
    resetting.value = true
    resetResult.value = null
    try {
      const status = await fetch(`${apiBase}/api/status`)
      if (!status.ok || (await status.json()).trackingEnabled) throw new Error(t('settings.resetHelp'))
      const response = await fetch(`${apiBase}/api/db/reset?confirm=true`, { method: 'POST' })
      if (!response.ok) throw new Error(`HTTP ${response.status}`)
      resetResult.value = await response.json()
      feedbackTone.value = 'warning'
      feedback.value = t('settings.resetDone')
    } catch (error) {
      feedbackTone.value = 'error'
      feedback.value = t('settings.operationFailed', { message: error.message })
    }
    resetting.value = false
    resetConfirm.value = false
    await loadDbStats()
  }

  return {
    loaded, feedback, feedbackTone, iconCacheTone, form,
    timeSourceModes,
    excludeText,
    saving,
    cleaning,
    clearingIcons,
    dbStats,
    cleanupResult,
    iconCacheResult,
    resetConfirm,
    resetting,
    resetResult,
    statusOk,
    statusClass,
    statusText,
    loadSettings,
    saveSettings,
    loadDbStats,
    runCleanup,
    clearIconCache,
    runReset,
  }
}
