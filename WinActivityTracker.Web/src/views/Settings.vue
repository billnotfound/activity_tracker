<!--
  Settings page — backend config + theme settings + language switcher
-->
<template>
  <div class="settings-page">
    <h2 class="page-title">{{ t('settings.pageTitle') }}</h2>

    <!-- Connection status -->
    <div class="status-banner mb-3" :class="statusClass">
      {{ statusText }}
      <span v-if="saving" class="spinner">⏳</span>
    </div>

    <!-- TabView -->
    <TabView class="memphis-tabview">
      <!-- Appearance Tab -->
      <TabPanel :header="t('settings.tab.appearance')">
        <MemphisCard>
          <h3 class="section-title">{{ t('settings.card.language') }}</h3>
          <div class="language-selector">
            <button
              v-for="lang in languages"
              :key="lang.code"
              class="option-button"
              :class="{ active: locale === lang.code }"
              @click="setLocale(lang.code)"
            >
              <span>{{ lang.name }}</span>
            </button>
          </div>

          <div class="divider"></div>

          <h3 class="section-title">{{ t('settings.appearance.themeColors') }}</h3>
          <div class="theme-selector">
            <button
              v-for="th in lightThemes"
              :key="th.id"
              class="theme-option"
              :class="{ active: !theme.isDark && theme.lightTheme === th.id }"
              @click="theme.setLightTheme(th.id)"
            >
              <div class="theme-preview">
                <div class="preview-bar" :style="{ background: th.colors['primary-color'] }"></div>
                <div class="preview-bar" :style="{ background: th.colors['secondary-color'] }"></div>
                <div class="preview-bar" :style="{ background: th.colors['accent-color'] }"></div>
              </div>
              <div class="theme-name">{{ th.name }}</div>
              <div class="theme-desc">{{ th.description }}</div>
            </button>
          </div>

          <div class="divider"></div>

          <h3 class="section-title">{{ t('settings.appearance.darkMode') }}</h3>
          <div class="toggle-row">
            <InputSwitch v-model="theme.isDark" />
            <span class="toggle-label">{{ theme.isDark ? t('settings.appearance.dark') : t('settings.appearance.light') }}</span>
          </div>

          <div class="divider"></div>

          <h3 class="section-title">{{ t('settings.appearance.pageTransition') }}</h3>
          <div class="button-group">
            <button
              class="option-button"
              :class="{ active: theme.pageTransition === 'slide' }"
              @click="theme.setPageTransition('slide')"
            >
              <i class="pi pi-arrow-right"></i>
              <span>{{ t('settings.appearance.slide') }}</span>
            </button>
            <button
              class="option-button"
              :class="{ active: theme.pageTransition === 'geometric' }"
              @click="theme.setPageTransition('geometric')"
            >
              <i class="pi pi-th-large"></i>
              <span>{{ t('settings.appearance.geometric') }}</span>
            </button>
          </div>

          <div class="divider"></div>

          <h3 class="section-title">{{ t('settings.appearance.autoColor') }}</h3>
          <div class="toggle-row">
            <InputSwitch v-model="theme.autoColor" />
            <span class="toggle-label">{{ t('settings.appearance.autoColorHelp') }}</span>
          </div>
        </MemphisCard>
      </TabPanel>

      <!-- Tracking Tab -->
      <TabPanel :header="t('settings.tab.tracking')">
        <MemphisCard>
          <h3 class="section-title">{{ t('settings.tracking.status') }}</h3>
          <div class="toggle-row mb-3">
            <InputSwitch v-model="form.trackingEnabled" />
            <span class="toggle-label">
              <strong>{{ form.trackingEnabled ? t('settings.trackingEnabled') : t('settings.trackingPaused') }}</strong>
            </span>
          </div>
          <p class="help-text">{{ t('settings.trackingDescription') }}</p>

          <div class="divider"></div>

          <h3 class="section-title">{{ t('settings.tracking.pollInterval') }}</h3>
          <div class="input-grid">
            <div class="input-field">
              <label>{{ t('settings.windowPollLabel') }}</label>
              <InputNumber v-model="form.windowPollSeconds" :min="1" :suffix="t('time.seconds.suffix')" />
              <small>{{ t('settings.windowPollHelp') }}</small>
            </div>
            <div class="input-field">
              <label>{{ t('settings.processPollLabel') }}</label>
              <InputNumber v-model="form.processPollSeconds" :min="5" :suffix="t('time.seconds.suffix')" />
              <small>{{ t('settings.processPollHelp') }}</small>
            </div>
            <div class="input-field">
              <label>{{ t('settings.mediaPollLabel') }}</label>
              <InputNumber v-model="form.mediaPollSeconds" :min="1" :suffix="t('time.seconds.suffix')" />
              <small>{{ t('settings.mediaPollHelp') }}</small>
            </div>
          </div>

          <div class="divider"></div>

          <h3 class="section-title">{{ t('settings.tracking.idleDetection') }}</h3>
          <div class="input-field">
            <label>{{ t('settings.idleThresholdLabel') }}</label>
            <InputNumber v-model="form.idleThresholdMinutes" :min="1" :suffix="t('time.minutes.suffix')" />
            <small>{{ t('settings.idleThresholdHelp') }}</small>
          </div>

          <div class="checkbox-list">
            <div class="checkbox-item">
              <Checkbox v-model="form.fullscreenBypassIdle" :binary="true" inputId="fullscreen" />
              <label for="fullscreen">{{ t('settings.fullscreenBypassLabel') }}</label>
            </div>
            <div class="checkbox-item">
              <Checkbox v-model="form.mergeSameProcessSwitches" :binary="true" inputId="merge" />
              <label for="merge">{{ t('settings.mergeSwitchesLabel') }}</label>
            </div>
          </div>

          <div class="divider"></div>

          <h3 class="section-title">{{ t('settings.exclusionsLabel') }}</h3>
          <div class="input-field">
            <textarea
              v-model="excludeText"
              class="exclude-input"
              rows="3"
              :placeholder="t('settings.exclusionsLabel')"
            ></textarea>
          </div>
        </MemphisCard>
      </TabPanel>

      <!-- Database Tab -->
      <TabPanel :header="t('settings.tab.database')">
        <MemphisCard>
          <h3 class="section-title">{{ t('settings.database.dataRetention') }}</h3>
          <div class="input-field">
            <label>{{ t('settings.retentionLabel') }}</label>
            <InputNumber v-model="form.dataRetentionDays" :min="1" :suffix="t('common.day.suffix')" />
            <small>{{ t('settings.retentionHelp') }}</small>
          </div>

          <div class="divider"></div>

          <h3 class="section-title">{{ t('settings.database.operations') }}</h3>
          <div class="button-row">
            <Button :label="t('settings.refreshStats')" icon="pi pi-refresh" @click="loadDbStats" :loading="false" />
            <Button
              :label="t('settings.database.clearIconCache')"
              icon="pi pi-image"
              severity="secondary"
              @click="clearIconCache"
              :loading="clearingIcons"
            />
            <Button
              :label="t('settings.cleanupNow')"
              icon="pi pi-trash"
              severity="warning"
              @click="runCleanup"
              :loading="cleaning"
            />
            <Button
              v-if="!resetConfirm"
              :label="t('settings.deleteAll')"
              icon="pi pi-times"
              severity="danger"
              @click="resetConfirm = true"
            />
            <template v-else>
              <Button :label="t('settings.confirmDelete')" severity="danger" @click="runReset" :loading="resetting" />
              <Button :label="t('settings.cancel')" severity="secondary" text @click="resetConfirm = false" />
            </template>
          </div>

          <div v-if="dbStats" class="stats-table">
            <div class="stat-row">
              <span>{{ t('settings.dbStats.focusChanges') }}</span>
              <strong>{{ dbStats.focusChanges?.toLocaleString() }}</strong>
            </div>
            <div class="stat-row">
              <span>{{ t('settings.dbStats.windowSessions') }}</span>
              <strong>{{ dbStats.windowSessions?.toLocaleString() }}</strong>
            </div>
            <div class="stat-row">
              <span>{{ t('settings.dbStats.processSessions') }}</span>
              <strong>{{ dbStats.processSessions?.toLocaleString() }}</strong>
            </div>
            <div class="stat-row">
              <span>{{ t('settings.dbStats.mediaRecords') }}</span>
              <strong>{{ dbStats.mediaRecords }}</strong>
            </div>
          </div>

          <div v-if="iconCacheResult" class="alert-banner success mt-3">
            {{ iconCacheResult }}
          </div>
          <div v-if="cleanupResult" class="alert-banner success mt-3">
            {{ t('settings.cleanupDone', { focus: cleanupResult.deleted.focusChanges ?? 0, windows: (cleanupResult.deleted.windowSessions ?? 0) + (cleanupResult.deleted.windowSnapshots ?? 0), processes: (cleanupResult.deleted.processSessions ?? 0) + (cleanupResult.deleted.processSnapshots ?? 0), media: cleanupResult.deleted.mediaRecords ?? 0 }) }}
          </div>
          <div v-if="resetResult" class="alert-banner warning mt-3">
            {{ t('settings.resetDone') }}
          </div>
        </MemphisCard>
      </TabPanel>

      <!-- Third-Party Licenses Tab -->
      <TabPanel :header="t('settings.tab.licenses')">
        <MemphisCard>
          <h3 class="section-title">{{ t('licenses.pageTitle') }}</h3>
          <p class="license-intro">{{ t('licenses.description') }}</p>

          <!-- Ubuntu Font -->
          <div class="license-section">
            <h4 class="license-title">Ubuntu Font Family</h4>
            <div class="license-info">
              <div class="info-row">
                <span class="label">{{ t('licenses.version') }}:</span>
                <span class="value">Ubuntu Font Licence 1.0</span>
              </div>
              <div class="info-row">
                <span class="label">{{ t('licenses.copyright') }}:</span>
                <span class="value">© 2010-2015 Canonical Ltd.</span>
              </div>
              <div class="info-row">
                <span class="label">{{ t('licenses.website') }}:</span>
                <a href="https://design.ubuntu.com/font" target="_blank" class="link">design.ubuntu.com/font</a>
              </div>
              <div class="info-row">
                <span class="label">{{ t('licenses.usage') }}:</span>
                <span class="value">{{ t('licenses.fontUsage') }}</span>
              </div>
            </div>

            <details class="license-details">
              <summary>{{ t('licenses.viewLicense') }}</summary>
              <pre class="license-text">-------------------------------
UBUNTU FONT LICENCE Version 1.0
-------------------------------

PREAMBLE
This licence allows the licensed fonts to be used, studied, modified and
redistributed freely. The fonts, including any derivative works, can be
bundled, embedded, and redistributed provided the terms of this licence
are met. The fonts and derivatives, however, cannot be released under
any other licence.

Permission is hereby granted, free of charge, to any person obtaining a
copy of the Font Software, to propagate the Font Software, subject to
the conditions specified in the full licence text.

For the complete licence text, see:
https://ubuntu.com/legal/font-licence</pre>
            </details>
          </div>
        </MemphisCard>
      </TabPanel>
    </TabView>

    <!-- Save Button -->
    <div class="save-section">
      <Button
        :label="t('settings.saveSettings')"
        icon="pi pi-check"
        size="large"
        @click="saveSettings"
        :loading="saving"
      />
    </div>
  </div>
</template>

<script setup>
import { ref, reactive, inject, onMounted, computed } from 'vue'
import { useI18n } from '../i18n/index.js'
import { useTheme } from '../composables/useTheme.js'
import MemphisCard from '../components/MemphisCard.vue'
import TabView from 'primevue/tabview'
import TabPanel from 'primevue/tabpanel'
import InputSwitch from 'primevue/inputswitch'
import InputNumber from 'primevue/inputnumber'
import Checkbox from 'primevue/checkbox'
import Button from 'primevue/button'

const apiBase = inject('apiBase')
const { t, locale, setLocale } = useI18n()
const theme = useTheme()

const languages = [
  { code: 'zh-CN', name: '中文' },
  { code: 'en-US', name: 'English' },
]

const lightThemes = computed(() =>
  Object.values(theme.themes).filter(t => !t.isDark)
)

const form = reactive({
  trackingEnabled: true,
  fullscreenBypassIdle: true,
  mergeSameProcessSwitches: true,
  windowPollSeconds: 3,
  processPollSeconds: 30,
  mediaPollSeconds: 5,
  idleThresholdMinutes: 2,
  dataRetentionDays: 90,
  apiPort: 32579,
})

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
const tagStatus = ref({})
const statusOk = ref(false)

const statusClass = computed(() => (statusOk.value ? 'success' : 'warning'))
const statusText = computed(() =>
  statusOk.value ? t('settings.connectionStatus.connected') : t('settings.connectionStatus.disconnected')
)

onMounted(async () => {
  await loadSettings()
  await loadDbStats()
})

async function loadSettings() {
  try {
    const r = await fetch(`${apiBase}/api/status`)
    if (!r.ok) return
    const s = await r.json()
    statusOk.value = true
    form.trackingEnabled = s.trackingEnabled
  } catch {
    statusOk.value = false
  }

  try {
    const r = await fetch(`${apiBase}/api/settings`)
    if (!r.ok) return
    const s = await r.json()
    Object.assign(form, {
      trackingEnabled: s.trackingEnabled,
      fullscreenBypassIdle: s.fullscreenBypassIdle ?? true,
      mergeSameProcessSwitches: s.mergeSameProcessSwitches ?? true,
      windowPollSeconds: s.windowPollSeconds,
      processPollSeconds: s.processPollSeconds,
      mediaPollSeconds: s.mediaPollSeconds,
      idleThresholdMinutes: s.idleThresholdMinutes,
      dataRetentionDays: s.dataRetentionDays,
      apiPort: s.apiPort || 32579,
    })
    excludeText.value = (s.excludedProcesses || []).join(', ')
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
      excludedProcesses: excludeText.value
        .split(',')
        .map(s => s.trim())
        .filter(s => s.length > 0),
    }
    const r = await fetch(`${apiBase}/api/settings`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    })
    if (r.ok) {
      const s = await r.json()
      excludeText.value = (s.excludedProcesses || []).join(', ')
      statusOk.value = true
    } else {
      console.error('Save failed:', r.status)
    }
  } catch (e) {
    console.error('Save error:', e)
  }
  saving.value = false
}

async function loadDbStats() {
  try {
    const r = await fetch(`${apiBase}/api/db/stats`)
    if (r.ok) dbStats.value = await r.json()
  } catch (e) {
    console.error('Failed to load DB stats:', e)
  }
}

async function runCleanup() {
  cleaning.value = true
  cleanupResult.value = null
  try {
    const r = await fetch(`${apiBase}/api/db/cleanup?days=${form.dataRetentionDays}`, { method: 'POST' })
    if (r.ok) cleanupResult.value = await r.json()
  } catch (e) {
    console.error('Cleanup failed:', e)
  }
  cleaning.value = false
  await loadDbStats()
}

async function clearIconCache() {
  clearingIcons.value = true
  iconCacheResult.value = null
  try {
    const r = await fetch(`${apiBase}/api/icons/cache`, { method: 'DELETE' })
    if (r.ok) {
      iconCacheResult.value = t('settings.database.iconCacheCleared')
    } else {
      iconCacheResult.value = t('settings.database.iconCacheClearFailed')
    }
  } catch (e) {
    console.error('Clear icon cache failed:', e)
    iconCacheResult.value = t('settings.database.iconCacheClearFailed')
  }
  clearingIcons.value = false
}

async function runReset() {
  resetting.value = true
  resetResult.value = null
  try {
    const r = await fetch(`${apiBase}/api/db/reset?confirm=true`, { method: 'POST' })
    if (r.ok) resetResult.value = await r.json()
  } catch (e) {
    console.error('Reset failed:', e)
  }
  resetting.value = false
  resetConfirm.value = false
  await loadDbStats()
}
</script>

<style lang="scss" scoped>
.settings-page {
  width: 100%;
}

.page-title {
  font-size: 1.5rem;
  font-weight: 700;
  letter-spacing: 1px;
  margin-bottom: 24px;
  color: var(--text-color);
}

.status-banner {
  padding: 12px 16px;
  border: 2px solid var(--border-color);
  font-weight: 600;
  display: flex;
  align-items: center;
  gap: 12px;

  &.success {
    background: var(--success-color);
    color: white;
  }

  &.warning {
    background: var(--warning-color);
    color: white;
  }
}

.section-title {
  font-size: 1rem;
  font-weight: 600;
  letter-spacing: 0.5px;
  margin-bottom: 16px;
  color: var(--text-color);
}

.divider {
  height: 2px;
  background: var(--surface-200);
  margin: 24px 0;
}

.theme-selector {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
  gap: 16px;
}

.theme-option {
  padding: 16px;
  border: 2px solid var(--surface-200);
  background: var(--surface-card);
  cursor: pointer;
  transition: all 0.2s ease;
  text-align: left;
  width: 100%;

  &:hover {
    border-color: var(--primary-color);
    transform: translateY(-2px);
    box-shadow: 0 4px 0 var(--primary-color);
  }

  &:active {
    transform: translateY(0);
    box-shadow: none;
  }

  &.active {
    border-color: var(--primary-color);
    border-width: 3px;
    box-shadow: 0 4px 0 rgba(0, 0, 0, 0.2);
  }
}

.theme-preview {
  display: flex;
  gap: 4px;
  margin-bottom: 12px;
}

.preview-bar {
  flex: 1;
  height: 40px;
  border: 2px solid var(--border-color);
}

.theme-name {
  font-weight: 600;
  margin-bottom: 4px;
  color: var(--text-color);
}

.theme-desc {
  font-size: 0.85rem;
  color: var(--text-color-secondary);
}

.toggle-row {
  display: flex;
  align-items: center;
  gap: 12px;
}

.toggle-label {
  font-weight: 600;
  color: var(--text-color);
}

.button-group {
  display: flex;
  gap: 12px;
}

.language-selector {
  display: flex;
  gap: 12px;
}

.option-button {
  flex: 1;
  padding: 16px;
  border: 2px solid var(--surface-200);
  background: transparent;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 8px;
  cursor: pointer;
  transition: all 0.2s ease;
  font-weight: 600;
  font-size: 1rem;
  color: var(--text-color);

  &:hover {
    border-color: var(--primary-color);
    transform: translateY(-2px);
    box-shadow: 0 4px 0 var(--primary-color);
  }

  &:active {
    transform: translateY(0);
    box-shadow: none;
  }

  &.active {
    border-color: var(--primary-color);
    background: var(--primary-color);
    color: var(--surface-card);
    box-shadow: 0 4px 0 rgba(0, 0, 0, 0.2);
  }

  i {
    font-size: 1.5rem;
  }
}

.input-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
  gap: 16px;
}

.input-field {
  display: flex;
  flex-direction: column;
  gap: 8px;

  label {
    font-weight: 600;
    font-size: 0.9rem;
    color: var(--text-color);
  }

  small {
    font-size: 0.85rem;
    color: var(--surface-400);
  }
}

.checkbox-list {
  display: flex;
  flex-direction: column;
  gap: 12px;
  margin-top: 16px;
}

.checkbox-item {
  display: flex;
  align-items: center;
  gap: 8px;

  label {
    font-weight: 600;
    color: var(--text-color);
  }
}

.exclude-input {
  width: 100%;
  padding: 8px 12px;
  border: 2px solid var(--surface-200);
  background: var(--surface-card);
  color: var(--text-color);
  font-family: inherit;
  font-size: 0.9rem;
  resize: vertical;

  &:focus {
    outline: none;
    border-color: var(--primary-color);
  }
}

.button-row {
  display: flex;
  gap: 12px;
  flex-wrap: wrap;
}

.stats-table {
  margin-top: 16px;
  border: 2px solid var(--surface-200);
}

.stat-row {
  display: flex;
  justify-content: space-between;
  padding: 12px 16px;
  border-bottom: 1px solid var(--surface-200);

  &:last-child {
    border-bottom: none;
  }

  span {
    color: var(--text-color);
  }

  strong {
    color: var(--primary-color);
    font-weight: 700;
  }
}

.alert-banner {
  padding: 12px 16px;
  border: 2px solid var(--border-color);
  font-weight: 600;

  &.success {
    background: var(--success-color);
    color: white;
  }

  &.warning {
    background: var(--warning-color);
    color: white;
  }
}

.save-section {
  margin-top: 32px;
  padding-top: 24px;
  border-top: 2px solid var(--surface-200);
}

.help-text {
  font-size: 0.9rem;
  color: var(--surface-400);
}

.mb-3 {
  margin-bottom: 24px;
}

.mt-3 {
  margin-top: 24px;
}

// Third-party licenses styles
.license-intro {
  margin-bottom: 24px;
  color: var(--text-color-secondary);
  line-height: 1.6;
}

.license-section {
  padding: 20px;
  background: var(--surface-100);
  border: 2px solid var(--surface-200);
  margin-bottom: 20px;

  &:last-child {
    margin-bottom: 0;
  }
}

.license-title {
  font-size: 1.3rem;
  font-weight: 700;
  margin-bottom: 16px;
  color: var(--primary-color);
}

.license-info {
  margin-bottom: 16px;
}

.info-row {
  display: flex;
  gap: 12px;
  margin-bottom: 8px;
  align-items: baseline;

  .label {
    font-weight: 600;
    color: var(--text-color);
    min-width: 80px;
  }

  .value {
    color: var(--text-color-secondary);
  }

  .link {
    color: var(--primary-color);
    text-decoration: none;
    font-weight: 600;

    &:hover {
      text-decoration: underline;
    }
  }
}

.license-details {
  margin-top: 16px;
  border: 2px solid var(--surface-200);
  background: var(--surface-card);

  summary {
    padding: 12px 16px;
    font-weight: 600;
    cursor: pointer;
    color: var(--primary-color);
    user-select: none;
    transition: background 0.2s;

    &:hover {
      background: var(--surface-100);
    }

    &::marker {
      color: var(--primary-color);
    }
  }
}

.license-text {
  padding: 20px;
  margin: 0;
  background: var(--surface-card);
  color: var(--text-color);
  font-family: 'Ubuntu Mono', 'Consolas', monospace;
  font-size: 0.85rem;
  line-height: 1.6;
  overflow-x: auto;
  white-space: pre-wrap;
  word-wrap: break-word;
}
</style>
