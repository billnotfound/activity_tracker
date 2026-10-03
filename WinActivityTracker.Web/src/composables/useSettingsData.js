import { computed, reactive, ref } from 'vue'

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
    } catch {
      statusOk.value = false
    }
  }

  async function saveSettings() {
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
        excludeText.value = (settings.excludedProcesses || []).join(', ')
        statusOk.value = true
      } else {
        console.error('Save failed:', response.status)
      }
    } catch (error) {
      console.error('Save error:', error)
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
    cleaning.value = true
    cleanupResult.value = null
    try {
      const response = await fetch(`${apiBase}/api/db/cleanup?days=${form.dataRetentionDays}`, { method: 'POST' })
      if (response.ok) cleanupResult.value = await response.json()
    } catch (error) {
      console.error('Cleanup failed:', error)
    }
    cleaning.value = false
    await loadDbStats()
  }

  async function clearIconCache() {
    clearingIcons.value = true
    iconCacheResult.value = null
    try {
      const response = await fetch(`${apiBase}/api/icons/cache`, { method: 'DELETE' })
      iconCacheResult.value = response.ok
        ? t('settings.database.iconCacheCleared')
        : t('settings.database.iconCacheClearFailed')
    } catch (error) {
      console.error('Clear icon cache failed:', error)
      iconCacheResult.value = t('settings.database.iconCacheClearFailed')
    }
    clearingIcons.value = false
  }

  async function runReset() {
    resetting.value = true
    resetResult.value = null
    try {
      const response = await fetch(`${apiBase}/api/db/reset?confirm=true`, { method: 'POST' })
      if (response.ok) resetResult.value = await response.json()
    } catch (error) {
      console.error('Reset failed:', error)
    }
    resetting.value = false
    resetConfirm.value = false
    await loadDbStats()
  }

  return {
    form,
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
