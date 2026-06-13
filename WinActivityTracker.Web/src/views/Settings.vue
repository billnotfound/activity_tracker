<!--
  Settings page — backend config + theme settings
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
      <TabPanel :header="'外观'">
        <MemphisCard>
          <h3 class="section-title">主题配色</h3>
          <div class="theme-selector">
            <div
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
            </div>
          </div>

          <div class="divider"></div>

          <h3 class="section-title">深色模式</h3>
          <div class="toggle-row">
            <InputSwitch v-model="theme.isDark" @change="theme.toggleDark" />
            <span class="toggle-label">{{ theme.isDark ? '深色' : '浅色' }}</span>
          </div>

          <div class="divider"></div>

          <h3 class="section-title">页面切换动画</h3>
          <div class="radio-group">
            <div
              class="radio-option"
              :class="{ active: theme.pageTransition === 'slide' }"
              @click="theme.setPageTransition('slide')"
            >
              <i class="pi pi-arrow-right"></i>
              <span>滑动</span>
            </div>
            <div
              class="radio-option"
              :class="{ active: theme.pageTransition === 'geometric' }"
              @click="theme.setPageTransition('geometric')"
            >
              <i class="pi pi-th-large"></i>
              <span>几何</span>
            </div>
          </div>

          <div class="divider"></div>

          <h3 class="section-title">自动配色</h3>
          <div class="toggle-row">
            <InputSwitch v-model="theme.autoColor" @change="v => theme.setAutoColor(v)" />
            <span class="toggle-label">根据今日使用最多的应用自动选择主题色</span>
          </div>
        </MemphisCard>
      </TabPanel>

      <!-- Tracking Tab -->
      <TabPanel :header="'追踪'">
        <MemphisCard>
          <h3 class="section-title">追踪状态</h3>
          <div class="toggle-row mb-3">
            <InputSwitch v-model="form.trackingEnabled" />
            <span class="toggle-label">
              <strong>{{ form.trackingEnabled ? t('settings.trackingEnabled') : t('settings.trackingPaused') }}</strong>
            </span>
          </div>
          <p class="help-text">{{ t('settings.trackingDescription') }}</p>

          <div class="divider"></div>

          <h3 class="section-title">轮询间隔</h3>
          <div class="input-grid">
            <div class="input-field">
              <label>{{ t('settings.windowPollLabel') }}</label>
              <InputNumber v-model="form.windowPollSeconds" :min="1" suffix=" 秒" />
              <small>{{ t('settings.windowPollHelp') }}</small>
            </div>
            <div class="input-field">
              <label>{{ t('settings.processPollLabel') }}</label>
              <InputNumber v-model="form.processPollSeconds" :min="5" suffix=" 秒" />
              <small>{{ t('settings.processPollHelp') }}</small>
            </div>
            <div class="input-field">
              <label>{{ t('settings.mediaPollLabel') }}</label>
              <InputNumber v-model="form.mediaPollSeconds" :min="1" suffix=" 秒" />
              <small>{{ t('settings.mediaPollHelp') }}</small>
            </div>
          </div>

          <div class="divider"></div>

          <h3 class="section-title">闲置检测</h3>
          <div class="input-field">
            <label>{{ t('settings.idleThresholdLabel') }}</label>
            <InputNumber v-model="form.idleThresholdMinutes" :min="1" suffix=" 分钟" />
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
        </MemphisCard>
      </TabPanel>

      <!-- Database Tab -->
      <TabPanel :header="'数据库'">
        <MemphisCard>
          <h3 class="section-title">数据保留</h3>
          <div class="input-field">
            <label>{{ t('settings.retentionLabel') }}</label>
            <InputNumber v-model="form.dataRetentionDays" :min="1" suffix=" 天" />
            <small>{{ t('settings.retentionHelp') }}</small>
          </div>

          <div class="divider"></div>

          <h3 class="section-title">数据库操作</h3>
          <div class="button-row">
            <Button label="刷新统计" icon="pi pi-refresh" @click="loadDbStats" :loading="false" />
            <Button
              label="清除图标缓存"
              icon="pi pi-image"
              severity="secondary"
              @click="clearIconCache"
              :loading="clearingIcons"
            />
            <Button
              label="清理旧数据"
              icon="pi pi-trash"
              severity="warning"
              @click="runCleanup"
              :loading="cleaning"
            />
            <Button
              v-if="!resetConfirm"
              label="删除所有数据"
              icon="pi pi-times"
              severity="danger"
              @click="resetConfirm = true"
            />
            <template v-else>
              <Button label="确认删除" severity="danger" @click="runReset" :loading="resetting" />
              <Button label="取消" severity="secondary" text @click="resetConfirm = false" />
            </template>
          </div>

          <div v-if="dbStats" class="stats-table">
            <div class="stat-row">
              <span>焦点记录</span>
              <strong>{{ dbStats.focusChanges?.toLocaleString() }}</strong>
            </div>
            <div class="stat-row">
              <span>窗口会话</span>
              <strong>{{ dbStats.windowSessions?.toLocaleString() }}</strong>
            </div>
            <div class="stat-row">
              <span>进程会话</span>
              <strong>{{ dbStats.processSessions?.toLocaleString() }}</strong>
            </div>
            <div class="stat-row">
              <span>媒体记录</span>
              <strong>{{ dbStats.mediaRecords }}</strong>
            </div>
          </div>

          <div v-if="iconCacheResult" class="alert-banner success mt-3">
            {{ iconCacheResult }}
          </div>
          <div v-if="cleanupResult" class="alert-banner success mt-3">
            清理完成：删除 {{ cleanupResult.deleted.focusChanges }} 条焦点记录
          </div>
          <div v-if="resetResult" class="alert-banner warning mt-3">
            重置完成：已删除所有数据
          </div>
        </MemphisCard>
      </TabPanel>
    </TabView>

    <!-- Save Button -->
    <div class="save-section">
      <Button
        label="保存设置"
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
import { toLocalString } from '../utils/time.js'
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
  apiPort: 5200,
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
      apiPort: s.apiPort || 5200,
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
      const result = await r.json()
      iconCacheResult.value = result.message
    }
  } catch (e) {
    console.error('Clear icon cache failed:', e)
    iconCacheResult.value = '清除图标缓存失败'
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
  text-transform: uppercase;
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
  text-transform: uppercase;
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
  cursor: pointer;
  transition: all 0.2s ease;

  &:hover {
    border-color: var(--primary-color);
    transform: translateY(-2px);
  }

  &.active {
    border-color: var(--primary-color);
    border-width: 3px;
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
}

.theme-desc {
  font-size: 0.85rem;
  color: var(--surface-400);
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

.radio-group {
  display: flex;
  gap: 12px;
}

.radio-option {
  flex: 1;
  padding: 16px;
  border: 2px solid var(--surface-200);
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 8px;
  cursor: pointer;
  transition: all 0.2s ease;
  font-weight: 600;

  &:hover {
    border-color: var(--primary-color);
    transform: translateY(-2px);
  }

  &.active {
    border-color: var(--primary-color);
    background: var(--primary-color);
    color: var(--surface-card);
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
</style>
