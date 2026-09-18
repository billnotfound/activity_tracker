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

    <!-- Tabs -->
    <div
      ref="settingsTabsRef"
      class="settings-tabs-nav"
      @mousemove="onSettingsTabMouseMove"
      @mouseleave="onSettingsTabMouseLeave"
    >
      <div class="settings-tab-frame" :class="{ moving: settingsTabMoving }" :style="settingsTabFrameStyle"></div>
      <Tabs v-model:value="activeTab" class="memphis-tabs">
      <TabList>
        <Tab value="appearance">{{ t('settings.tab.appearance') }}</Tab>
        <Tab value="tracking">{{ t('settings.tab.tracking') }}</Tab>
        <Tab value="tags">{{ t('settings.tab.tags') }}</Tab>
        <Tab value="time">{{ t('settings.tab.time') }}</Tab>
        <Tab value="database">{{ t('settings.tab.database') }}</Tab>
        <Tab value="licenses">{{ t('settings.tab.licenses') }}</Tab>
      </TabList>
      <TabPanels>
      <!-- Appearance Tab -->
      <TabPanel value="appearance">
        <MemphisCard>
          <h3 class="section-title">{{ t('settings.card.language') }}</h3>
          <select :value="locale" class="memphis-select language-select" @change="setLocale($event.target.value)">
            <option v-for="lang in languages" :key="lang.code" :value="lang.code">{{ lang.name }}</option>
          </select>

          <div class="divider"></div>

          <h3 class="section-title">{{ t('settings.appearance.themeColors') }}</h3>
          <div class="theme-groups">
            <div class="theme-group">
              <span class="theme-group-label">{{ t('settings.appearance.light') }}</span>
              <div class="theme-selector">
                <button
                  v-for="th in lightThemes"
                  :key="th.id"
                  class="theme-option"
                  :class="{ active: !isDark && lightTheme === th.id }"
                  @click="selectTheme(th)"
                >
                  <span class="theme-orb" :style="{ background: themePreview(th) }" aria-hidden="true"></span>
                  <span class="theme-name">{{ th.name }}</span>
                </button>
              </div>
            </div>
            <div class="theme-group">
              <span class="theme-group-label">{{ t('settings.appearance.dark') }}</span>
              <div class="theme-selector">
                <button
                  v-for="th in darkThemes"
                  :key="th.id"
                  class="theme-option"
                  :class="{ active: isDark && darkTheme === th.id }"
                  @click="selectTheme(th)"
                >
                  <span class="theme-orb" :style="{ background: themePreview(th) }" aria-hidden="true"></span>
                  <span class="theme-name">{{ th.name }}</span>
                </button>
              </div>
            </div>
          </div>

          <div class="custom-palette">
            <div class="custom-palette-head">
              <span>{{ t('settings.appearance.customPalette') }}</span>
              <button
                class="custom-palette-toggle"
                :aria-expanded="showCustomPalette"
                :title="t('settings.appearance.customPalette')"
                @click="showCustomPalette = !showCustomPalette"
              >
                <X v-if="showCustomPalette" :size="17" />
                <Plus v-else :size="17" />
              </button>
            </div>
            <Transition name="custom-expand">
              <div v-if="showCustomPalette" class="custom-theme-grid">
                <section v-for="mode in customModes" :key="mode" class="custom-theme-editor">
                  <div class="custom-theme-editor-head">
                    <strong>{{ t(`settings.appearance.custom.${mode}Scheme`) }}</strong>
                    <span class="theme-orb custom-orb" :style="{ background: customPreview(mode) }" aria-hidden="true"></span>
                  </div>
                  <input
                    v-model.trim="customDrafts[mode].name"
                    type="text"
                    class="custom-theme-name"
                    maxlength="40"
                    :placeholder="t('settings.appearance.custom.schemeName')"
                  />
                  <div class="custom-color-list">
                    <label v-for="(key, index) in customColorKeys" :key="key" class="custom-color-field">
                      <span>{{ t(`settings.appearance.custom.${key}`) }}</span>
                      <input v-model="customDrafts[mode].colors[index]" type="color" class="color-dot" :aria-label="t(`settings.appearance.custom.${key}`)" />
                      <input v-model.trim="customDrafts[mode].colors[index]" type="text" class="custom-hex-input" maxlength="7" spellcheck="false" />
                    </label>
                  </div>
                  <button class="custom-palette-save" :disabled="!customDraftValid(mode)" @click="saveCustomScheme(mode)">
                    <Check :size="16" />
                    <span>{{ customSaved[mode] ? t('settings.appearance.paletteSaved') : t('settings.appearance.savePalette') }}</span>
                  </button>
                </section>
              </div>
            </Transition>
          </div>

          <div class="divider"></div>

          <h3 class="section-title">{{ t('settings.appearance.darkMode') }}</h3>
          <div class="toggle-row">
            <button
              class="icon-toggle"
              :class="{ on: isDark }"
              @click="theme.toggleDark()"
              :aria-pressed="isDark"
              :title="isDark ? t('settings.appearance.dark') : t('settings.appearance.light')"
            >
              <Check v-if="isDark" :size="18" />
              <X v-else :size="18" />
            </button>
            <span class="toggle-label">{{ isDark ? t('settings.appearance.dark') : t('settings.appearance.light') }}</span>
          </div>

          <div class="divider"></div>

          <h3 class="section-title">{{ t('settings.appearance.pageTransition') }}</h3>
          <div class="button-group">
            <button
              class="option-button"
              :class="{ active: pageTransition === 'slide' }"
              @click="theme.setPageTransition('slide')"
            >
              <ArrowRight :size="24" />
              <span>{{ t('settings.appearance.slide') }}</span>
            </button>
            <button
              class="option-button"
              :class="{ active: pageTransition === 'geometric' }"
              @click="theme.setPageTransition('geometric')"
            >
              <Grid :size="24" />
              <span>{{ t('settings.appearance.geometric') }}</span>
            </button>
          </div>

          <div class="divider"></div>

          <h3 class="section-title">{{ t('settings.appearance.autoColor') }}</h3>
          <div class="toggle-row">
            <button
              class="icon-toggle"
              :class="{ on: autoColor }"
              @click="theme.setAutoColor(!autoColor)"
              :aria-pressed="autoColor"
              :title="autoColor ? t('common.enabled') : t('common.disabled')"
            >
              <Check v-if="autoColor" :size="18" />
              <X v-else :size="18" />
            </button>
            <span class="toggle-label">{{ t('settings.appearance.autoColorHelp') }}</span>
          </div>
        </MemphisCard>
      </TabPanel>

      <!-- Tracking Tab -->
      <TabPanel value="tracking">
        <MemphisCard>
          <h3 class="section-title">{{ t('settings.tracking.status') }}</h3>
          <div class="toggle-row mb-3">
            <button
              class="icon-toggle"
              :class="{ on: form.trackingEnabled }"
              @click="form.trackingEnabled = !form.trackingEnabled"
              :aria-pressed="form.trackingEnabled"
              :title="form.trackingEnabled ? t('settings.trackingEnabled') : t('settings.trackingPaused')"
            >
              <Check v-if="form.trackingEnabled" :size="18" />
              <X v-else :size="18" />
            </button>
            <span class="toggle-label">
              <strong>{{ form.trackingEnabled ? t('settings.trackingEnabled') : t('settings.trackingPaused') }}</strong>
            </span>
          </div>
          <p class="help-text">{{ t('settings.trackingDescription') }}</p>

          <div class="divider"></div>

          <h3 class="section-title">{{ t('settings.tracking.pollInterval') }}</h3>
          <div class="input-grid">
            <div class="input-field">
              <label>
                {{ t('settings.windowPollLabel') }}
                <CircleHelp :size="14" class="help-icon" :title="t('settings.windowPollHelp')" />
              </label>
              <div class="unit-control"><InputNumber v-model="form.windowPollSeconds" :min="1" :placeholder="t('settings.windowPollPlaceholder')" /><span>{{ t('time.seconds.suffix') }}</span></div>
            </div>
            <div class="input-field">
              <label>
                {{ t('settings.processPollLabel') }}
                <CircleHelp :size="14" class="help-icon" :title="t('settings.processPollHelp')" />
              </label>
              <div class="unit-control"><InputNumber v-model="form.processPollSeconds" :min="5" :placeholder="t('settings.processPollPlaceholder')" /><span>{{ t('time.seconds.suffix') }}</span></div>
            </div>
            <div class="input-field">
              <label>
                {{ t('settings.mediaPollLabel') }}
                <CircleHelp :size="14" class="help-icon" :title="t('settings.mediaPollHelp')" />
              </label>
              <div class="unit-control"><InputNumber v-model="form.mediaPollSeconds" :min="1" :placeholder="t('settings.mediaPollPlaceholder')" /><span>{{ t('time.seconds.suffix') }}</span></div>
            </div>
          </div>

          <div class="divider"></div>

          <h3 class="section-title">{{ t('settings.tracking.idleDetection') }}</h3>
          <div class="input-field">
            <label>
              {{ t('settings.idleThresholdLabel') }}
              <CircleHelp :size="14" class="help-icon" :title="t('settings.idleThresholdHelp')" />
            </label>
            <div class="unit-control"><InputNumber v-model="form.idleThresholdMinutes" :min="1" :placeholder="t('settings.idleThresholdPlaceholder')" /><span>{{ t('time.minutes.suffix') }}</span></div>
          </div>

          <div class="checkbox-list">
            <div class="checkbox-item">
              <button
                class="icon-toggle"
                :class="{ on: form.fullscreenBypassIdle }"
                @click="form.fullscreenBypassIdle = !form.fullscreenBypassIdle"
                :aria-pressed="form.fullscreenBypassIdle"
                :title="form.fullscreenBypassIdle ? t('common.enabled') : t('common.disabled')"
              >
                <Check v-if="form.fullscreenBypassIdle" :size="18" />
                <X v-else :size="18" />
              </button>
              <label @click="form.fullscreenBypassIdle = !form.fullscreenBypassIdle">
                {{ t('settings.fullscreenBypassLabel') }}
                <CircleHelp :size="14" class="help-icon" :title="t('settings.fullscreenBypassHelp')" />
              </label>
            </div>
            <div class="checkbox-item">
              <button
                class="icon-toggle"
                :class="{ on: form.mergeSameProcessSwitches }"
                @click="form.mergeSameProcessSwitches = !form.mergeSameProcessSwitches"
                :aria-pressed="form.mergeSameProcessSwitches"
                :title="form.mergeSameProcessSwitches ? t('common.enabled') : t('common.disabled')"
              >
                <Check v-if="form.mergeSameProcessSwitches" :size="18" />
                <X v-else :size="18" />
              </button>
              <label @click="form.mergeSameProcessSwitches = !form.mergeSameProcessSwitches">
                {{ t('settings.mergeSwitchesLabel') }}
                <CircleHelp :size="14" class="help-icon" :title="t('settings.mergeSwitchesHelp')" />
              </label>
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

      <TabPanel value="tags">
        <TagsView embedded />
      </TabPanel>

      <TabPanel value="time">
        <MemphisCard class="mb-3">
          <h3 class="section-title">{{ t('settings.timeSection.title') }}</h3>
          <div class="toggle-row mb-3">
            <button class="icon-toggle" :class="{ on: form.useNtp }" @click="form.useNtp = !form.useNtp" :aria-pressed="form.useNtp" :title="form.useNtp ? t('common.enabled') : t('common.disabled')">
              <Check v-if="form.useNtp" :size="18" /><X v-else :size="18" />
            </button>
            <span class="toggle-label"><strong>{{ t('settings.useNtp') }}</strong></span>
          </div>
          <div class="input-grid">
            <div class="input-field"><label>{{ t('settings.timeServer') }}</label><input v-model="form.timeServer" type="text" class="memphis-text-input" placeholder="pool.ntp.org" /></div>
            <div class="input-field">
              <label>{{ t('settings.timeSourceMode') }}</label>
              <select v-model="form.timeSourceMode" class="memphis-select"><option v-for="m in timeSourceModes" :key="m.value" :value="m.value">{{ m.label }}</option></select>
            </div>
            <div class="input-field"><label>{{ t('settings.timeAnomalyThresholdSeconds') }} <CircleHelp :size="14" class="help-icon" :title="t('settings.timeAnomalyThresholdSeconds')" /></label><div class="unit-control"><InputNumber v-model="form.timeAnomalyThresholdSeconds" :min="30" /><span>{{ t('time.seconds.suffix') }}</span></div></div>
            <div class="input-field"><label>{{ t('settings.ntpEpsilonSeconds') }} <CircleHelp :size="14" class="help-icon" :title="t('settings.ntpEpsilonSeconds')" /></label><div class="unit-control"><InputNumber v-model="form.ntpEpsilonSeconds" :min="1" /><span>{{ t('time.seconds.suffix') }}</span></div></div>
          </div>
        </MemphisCard>
        <TimeAnomalyView embedded />
      </TabPanel>

      <!-- Database Tab -->
      <TabPanel value="database">
        <MemphisCard>
          <h3 class="section-title">{{ t('settings.database.dataRetention') }}</h3>
          <div class="input-field">
            <label>
              {{ t('settings.retentionLabel') }}
              <CircleHelp :size="14" class="help-icon" :title="t('settings.retentionHelp')" />
            </label>
            <div class="unit-control"><InputNumber v-model="form.dataRetentionDays" :min="1" :placeholder="t('settings.retentionPlaceholder')" /><span>{{ t('common.day.suffix') }}</span></div>
          </div>

          <div class="divider"></div>

          <h3 class="section-title">{{ t('settings.database.operations') }}</h3>
          <div class="button-row">
            <button class="icon-btn" @click="loadDbStats" :title="t('settings.refreshStats')" :aria-label="t('settings.refreshStats')">
              <RefreshCw :size="18" />
            </button>
            <button
              class="icon-btn secondary"
              @click="clearIconCache"
              :disabled="clearingIcons"
              :title="t('settings.database.clearIconCache')"
              :aria-label="t('settings.database.clearIconCache')"
            >
              <Image :size="18" :class="{ spin: clearingIcons }" />
            </button>
            <button
              class="icon-btn warning"
              @click="runCleanup"
              :disabled="cleaning"
              :title="t('settings.cleanupNow')"
              :aria-label="t('settings.cleanupNow')"
            >
              <Trash2 :size="18" :class="{ spin: cleaning }" />
            </button>
            <Button
              v-if="!resetConfirm"
              :label="t('settings.deleteAll')"
              severity="danger"
              @click="resetConfirm = true"
            >
              <template #icon><Trash2 :size="16" /></template>
            </Button>
            <template v-else>
              <Button :label="t('settings.confirmDelete')" severity="danger" @click="runReset" :loading="resetting">
                <template #icon><Trash2 :size="16" /></template>
              </Button>
              <Button :label="t('settings.cancel')" severity="secondary" text @click="resetConfirm = false">
                <template #icon><X :size="16" /></template>
              </Button>
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
      <TabPanel value="licenses">
        <MemphisCard>
          <h3 class="section-title">{{ t('licenses.pageTitle') }}</h3>
          <p class="license-intro">{{ t('licenses.description') }}</p>

          <div class="resource-grid">
            <a
              v-for="resource in thirdPartyResources"
              :key="resource.name"
              :href="resource.url"
              target="_blank"
              rel="noreferrer"
              class="resource-card"
            >
              <span class="resource-name">{{ resource.name }}</span>
              <span class="resource-license">{{ resource.license }}</span>
            </a>
          </div>

        </MemphisCard>
      </TabPanel>
      </TabPanels>
      </Tabs>
    </div>

    <!-- Save Button -->
    <div class="save-section">
      <Button
        :label="t('settings.saveSettings')"
        size="large"
        @click="saveSettings"
        :loading="saving"
      >
        <template #icon><Check :size="16" /></template>
      </Button>
    </div>
  </div>
</template>

<script setup>
import { ref, reactive, inject, onMounted, onBeforeUnmount, computed, watch, nextTick } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useI18n } from '../i18n/index.js'
import { useTheme } from '../composables/useTheme.js'
import MemphisCard from '../components/MemphisCard.vue'
import TagsView from './Tags.vue'
import TimeAnomalyView from './TimeAnomaly.vue'
import Tabs from 'primevue/tabs'
import TabList from 'primevue/tablist'
import Tab from 'primevue/tab'
import TabPanels from 'primevue/tabpanels'
import TabPanel from 'primevue/tabpanel'
import InputNumber from 'primevue/inputnumber'
import Button from 'primevue/button'
import { ArrowRight, Grid, RefreshCw, Image, Trash2, X, Check, CircleHelp, Plus } from '@lucide/vue'

const apiBase = inject('apiBase')
const { t, locale, setLocale } = useI18n()
const theme = useTheme()
const route = useRoute()
const router = useRouter()
const validTabs = new Set(['appearance', 'tracking', 'tags', 'time', 'database', 'licenses'])
const activeTab = ref(validTabs.has(String(route.query.section)) ? String(route.query.section) : 'appearance')
const settingsTabsRef = ref(null)
const settingsTabFrameStyle = ref({ opacity: 0 })
const settingsTabMoving = ref(false)
let settingsTabTarget = null
let settingsTabTimer = null

watch(activeTab, value => {
  const nextQuery = { ...route.query, section: value === 'appearance' ? undefined : value }
  router.replace({ query: nextQuery })
  nextTick(() => moveSettingsTabFrame(activeSettingsTab()))
})

function settingsTabItems() {
  return Array.from(settingsTabsRef.value?.querySelectorAll('.p-tab') || [])
}

function activeSettingsTab() {
  return settingsTabsRef.value?.querySelector('.p-tab[aria-selected="true"]') || settingsTabItems()[0]
}

function moveSettingsTabFrame(element) {
  const host = settingsTabsRef.value
  if (!host || !element) return
  settingsTabTarget = element
  const hostRect = host.getBoundingClientRect()
  const itemRect = element.getBoundingClientRect()
  settingsTabMoving.value = true
  settingsTabFrameStyle.value = {
    opacity: 1,
    left: `${itemRect.left - hostRect.left}px`,
    top: `${itemRect.top - hostRect.top}px`,
    width: `${itemRect.width}px`,
    height: `${itemRect.height}px`,
  }
  clearTimeout(settingsTabTimer)
  settingsTabTimer = setTimeout(() => { settingsTabMoving.value = false }, 320)
}

function onSettingsTabMouseMove(event) {
  const tabList = settingsTabsRef.value?.querySelector('.p-tablist')
  if (!tabList?.contains(event.target)) {
    const active = activeSettingsTab()
    if (active && active !== settingsTabTarget) moveSettingsTabFrame(active)
    return
  }
  const items = settingsTabItems()
  if (!items.length) return
  let nearest = items[0]
  let distance = Infinity
  for (const item of items) {
    const rect = item.getBoundingClientRect()
    const nextDistance = Math.abs(event.clientX - (rect.left + rect.width / 2))
    if (nextDistance < distance) {
      nearest = item
      distance = nextDistance
    }
  }
  if (nearest !== settingsTabTarget) moveSettingsTabFrame(nearest)
}

function onSettingsTabMouseLeave() {
  moveSettingsTabFrame(activeSettingsTab())
}

// useTheme() exposes refs; unwrap for template binding (templates don't
// unwrap refs nested inside plain objects).
const isDark = computed(() => theme.isDark.value)
const autoColor = computed(() => theme.autoColor.value)
const lightTheme = computed(() => theme.lightTheme.value)
const darkTheme = computed(() => theme.darkTheme.value)
const pageTransition = computed(() => theme.pageTransition.value)
const customColorKeys = ['primary', 'secondary', 'accent']
const customModes = ['light', 'dark']
const showCustomPalette = ref(false)
const customDrafts = reactive({
  light: {
    name: theme.customThemes.value.light?.name || '',
    colors: [...(theme.customThemes.value.light?.colors || ['#C87773', '#77A67B', '#B7E6F0'])],
  },
  dark: {
    name: theme.customThemes.value.dark?.name || '',
    colors: [...(theme.customThemes.value.dark?.colors || ['#FEB4C1', '#5A6CBC', '#3B1E3D'])],
  },
})
const customSaved = reactive({ light: false, dark: false })
const customPaletteTimers = { light: null, dark: null }

const languages = [
  { code: 'zh-CN', name: '中文' },
  { code: 'en-US', name: 'English' },
]

const lightThemes = computed(() =>
  Object.values(theme.themes.value).filter(t => !t.isDark)
)
const darkThemes = computed(() =>
  Object.values(theme.themes.value).filter(t => t.isDark)
)

function selectTheme(selected) {
  theme.setAutoColor(false)
  theme.clearOverrides()
  if (selected.isDark) {
    theme.setDarkTheme(selected.id)
    if (!isDark.value) theme.toggleDark()
  } else {
    theme.setLightTheme(selected.id)
    if (isDark.value) theme.toggleDark()
  }
}

function themePreview(selected) {
  const active = selected.id === (isDark.value ? darkTheme.value : lightTheme.value)
  const colors = active
    ? { ...selected.colors, ...theme.overrides.value }
    : selected.colors
  return `conic-gradient(${colors['primary-color']} 0 50%, ${colors['secondary-color']} 50% 75%, ${colors['accent-color']} 75% 100%)`
}

function customDraftValid(mode) {
  const draft = customDrafts[mode]
  return Boolean(draft.name.trim())
    && draft.colors.length === 3
    && draft.colors.every(color => /^#[0-9a-f]{6}$/i.test(color))
}

function customPreview(mode) {
  const colors = customDrafts[mode].colors
  const safe = colors.every(color => /^#[0-9a-f]{6}$/i.test(color))
    ? colors
    : (mode === 'dark' ? ['#FEB4C1', '#5A6CBC', '#3B1E3D'] : ['#C87773', '#77A67B', '#B7E6F0'])
  return `conic-gradient(${safe[0]} 0 50%, ${safe[1]} 50% 75%, ${safe[2]} 75% 100%)`
}

function saveCustomScheme(mode) {
  if (!customDraftValid(mode)) return
  const draft = customDrafts[mode]
  const colors = draft.colors.map(color => color.toUpperCase())
  if (!theme.saveCustomTheme(mode, draft.name, colors)) return
  draft.colors = colors
  customSaved[mode] = true
  clearTimeout(customPaletteTimers[mode])
  customPaletteTimers[mode] = setTimeout(() => { customSaved[mode] = false }, 1400)
}

const thirdPartyResources = [
  { name: 'Vue.js', license: 'MIT', url: 'https://vuejs.org/' },
  { name: 'Vue Router', license: 'MIT', url: 'https://router.vuejs.org/' },
  { name: 'Vite', license: 'MIT', url: 'https://vite.dev/' },
  { name: 'PrimeVue', license: 'MIT', url: 'https://primevue.org/' },
  { name: 'PrimeIcons', license: 'MIT', url: 'https://primevue.org/icons/' },
  { name: 'Apache ECharts', license: 'Apache-2.0', url: 'https://echarts.apache.org/' },
  { name: 'Lucide', license: 'ISC', url: 'https://lucide.dev/' },
  { name: 'Sass', license: 'MIT', url: 'https://sass-lang.com/' },
  { name: '.NET / ASP.NET Core', license: 'MIT', url: 'https://dotnet.microsoft.com/' },
  { name: 'Entity Framework Core', license: 'MIT', url: 'https://learn.microsoft.com/ef/core/' },
  { name: 'Windows Services Hosting', license: 'MIT', url: 'https://www.nuget.org/packages/Microsoft.Extensions.Hosting.WindowsServices' },
  { name: 'SQLite', license: 'Public Domain', url: 'https://sqlite.org/' },
  { name: 'SQLitePCLRaw', license: 'MIT', url: 'https://github.com/ericsink/SQLitePCL.raw' },
  { name: 'CommunityToolkit Notifications', license: 'MIT', url: 'https://github.com/CommunityToolkit/Labs-Windows' },
  { name: 'Ubuntu Font Family', license: 'Ubuntu Font Licence 1.0', url: 'https://design.ubuntu.com/font' },
  { name: 'NSIS', license: 'zlib/libpng', url: 'https://nsis.sourceforge.io/' },
]

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
const tagStatus = ref({})
const statusOk = ref(false)

const statusClass = computed(() => (statusOk.value ? 'success' : 'warning'))
const statusText = computed(() =>
  statusOk.value ? t('settings.connectionStatus.connected') : t('settings.connectionStatus.disconnected')
)

onMounted(async () => {
  await nextTick()
  moveSettingsTabFrame(activeSettingsTab())
  await loadSettings()
  await loadDbStats()
})

onBeforeUnmount(() => {
  clearTimeout(settingsTabTimer)
  clearTimeout(customPaletteTimers.light)
  clearTimeout(customPaletteTimers.dark)
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
      useNtp: s.useNtp ?? true,
      timeServer: s.timeServer || 'pool.ntp.org',
      timeSourceMode: s.timeSourceMode || 'Sntp',
      timeAnomalyThresholdSeconds: s.timeAnomalyThresholdSeconds ?? 180,
      ntpEpsilonSeconds: s.ntpEpsilonSeconds ?? 5,
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

.settings-tabs-nav {
  position: relative;
}

.memphis-tabs {
  position: relative;
  z-index: 1;
}

.settings-tab-frame {
  position: absolute;
  z-index: 2;
  pointer-events: none;
  border: 3px solid color-mix(in srgb, var(--text-color) 80%, transparent);
  transition:
    left 0.28s cubic-bezier(0.4, 0, 0.2, 1),
    top 0.28s cubic-bezier(0.4, 0, 0.2, 1),
    width 0.28s cubic-bezier(0.4, 0, 0.2, 1),
    height 0.28s cubic-bezier(0.4, 0, 0.2, 1),
    transform 0.12s ease-out,
    box-shadow 0.15s ease-out,
    opacity 0.15s ease;

  &.moving {
    box-shadow: none !important;
  }
}

.settings-tabs-nav:has(.p-tablist:hover) .settings-tab-frame:not(.moving) {
  border-color: var(--text-color);
  transform: translateY(-2px);
  box-shadow: 4px 4px 0 color-mix(in srgb, var(--primary-color) 80%, transparent);
}

:deep(.p-tablist),
:deep(.p-tablist-tab-list) {
  border: 0 !important;
  background: transparent !important;
  box-shadow: none !important;
}

:deep(.p-tablist-tab-list) {
  gap: 18px;
  flex-wrap: wrap;
  padding: 0 !important;
}

:deep(.p-tab) {
  position: relative;
  z-index: 3;
  border: 2px solid transparent !important;
  background: transparent !important;
  box-shadow: none !important;
  color: var(--text-color) !important;
}

:deep(.p-tab:hover),
:deep(.p-tab-active) {
  border-color: transparent !important;
  background: transparent !important;
  box-shadow: none !important;
}

:global(.settings-tabs-nav .p-tab),
:global(.settings-tabs-nav .p-tab:hover),
:global(.settings-tabs-nav .p-tab[data-p-active="true"]),
:global(.settings-tabs-nav .p-tab[aria-selected="true"]) {
  border-color: transparent !important;
  background: transparent !important;
  box-shadow: none !important;
  color: var(--text-color) !important;
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
  display: flex;
  flex-wrap: wrap;
  gap: 12px;
}

.theme-groups {
  display: grid;
  gap: 18px;
}

.theme-group {
  display: grid;
  grid-template-columns: 64px minmax(0, 1fr);
  align-items: center;
  gap: 14px;
}

.theme-group-label {
  color: var(--surface-400);
  font-size: 0.85rem;
  font-weight: 700;
}

.theme-option {
  min-width: 148px;
  padding: 10px 14px;
  border: 2px solid var(--surface-200);
  background: var(--surface-card);
  cursor: pointer;
  transition: transform 0.16s ease, border-color 0.16s ease, box-shadow 0.16s ease;
  color: var(--text-color);
  display: inline-flex;
  align-items: center;
  gap: 10px;

  &:hover {
    border-color: var(--primary-color);
    transform: translateY(-2px);
    box-shadow: 0 3px 0 var(--primary-color);
  }

  &:active {
    transform: translateY(1px) scale(0.98);
    box-shadow: 0 1px 0 var(--primary-color);
  }

  &.active {
    border-color: var(--primary-color);
    background: color-mix(in srgb, var(--primary-color) 10%, var(--surface-card));
  }
}

.theme-orb {
  width: 38px;
  height: 38px;
  flex: 0 0 38px;
  border-radius: 50%;
  clip-path: circle(50% at 50% 50%);
  overflow: hidden;
  background-clip: padding-box;
  border: 2px solid color-mix(in srgb, var(--border-color) 55%, transparent);
}

.theme-name {
  font-weight: 600;
  color: var(--text-color);
}

.custom-palette {
  margin-top: 18px;
  padding-top: 18px;
  border-top: 1px solid var(--surface-200);
}

.custom-palette-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  color: var(--text-color);
  font-size: 0.9rem;
  font-weight: 700;
}

.custom-palette-toggle {
  width: 31px;
  height: 31px;
  padding: 0;
  border: 2px solid var(--primary-color);
  background: transparent;
  color: var(--text-color);
  cursor: pointer;
  display: grid;
  place-items: center;
  transition: transform 0.16s ease, background-color 0.16s ease;

  &:hover {
    background: color-mix(in srgb, var(--primary-color) 16%, transparent);
    transform: translateY(-1px);
  }

  &:active { transform: translateY(1px) scale(0.94); }
}

.custom-theme-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 14px;
  margin-top: 14px;
}

.custom-theme-editor {
  min-width: 0;
  padding: 14px;
  border: 2px solid var(--surface-200);
  background: var(--surface-card);
  display: grid;
  gap: 12px;
}

.custom-theme-editor-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  color: var(--text-color);
}

.custom-theme-name {
  width: min(100%, 280px);
  height: 34px;
  padding: 6px 9px;
  border: 2px solid var(--surface-200);
  background: var(--settings-input-bg);
  color: var(--settings-input-text);
  caret-color: var(--settings-input-text);

  &::placeholder { color: var(--settings-input-placeholder); opacity: 1; }
  &:focus { outline: none; border-color: var(--primary-color); }
}

.custom-orb {
  width: 26px;
  height: 26px;
  flex-basis: 26px;
}

.custom-color-list {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  align-items: end;
  gap: 9px;
}

.custom-color-field {
  display: grid;
  grid-template-columns: 26px minmax(0, 1fr);
  gap: 7px;
  align-items: center;

  > span {
    grid-column: 1 / -1;
    color: var(--surface-400);
    font-size: 0.76rem;
    font-weight: 700;
  }
}

.color-dot {
  width: 26px;
  height: 26px;
  padding: 0;
  border: 2px solid var(--border-color);
  border-radius: 50%;
  clip-path: circle(50% at 50% 50%);
  appearance: none;
  background: transparent;
  cursor: pointer;
  overflow: hidden;

  &::-webkit-color-swatch-wrapper { padding: 0; }
  &::-webkit-color-swatch { border: 0; border-radius: 50%; }
  &::-moz-color-swatch { border: 0; border-radius: 50%; }
}

.custom-hex-input {
  width: 100%;
  height: 32px;
  padding: 6px 9px;
  border: 2px solid var(--surface-200);
  background: var(--settings-input-bg);
  color: var(--settings-input-text);
  caret-color: var(--settings-input-text);
  font-family: 'Ubuntu Mono', 'Consolas', monospace;
  text-transform: uppercase;

  &:focus {
    outline: none;
    border-color: var(--primary-color);
  }
}

.custom-palette-save {
  justify-self: start;
  min-height: 32px;
  padding: 6px 12px;
  border: 2px solid var(--primary-color);
  background: transparent;
  color: var(--text-color);
  font-weight: 700;
  cursor: pointer;
  display: inline-flex;
  align-items: center;
  gap: 7px;
  transition: transform 0.16s ease, background-color 0.16s ease;

  &:hover:not(:disabled) {
    background: color-mix(in srgb, var(--primary-color) 18%, transparent);
    transform: translateY(-1px);
  }

  &:active:not(:disabled) { transform: translateY(1px) scale(0.98); }
  &:disabled { opacity: 0.45; cursor: not-allowed; }
}

.custom-expand-enter-active,
.custom-expand-leave-active {
  transition: opacity 0.18s ease, transform 0.2s cubic-bezier(0.2, 0.8, 0.2, 1);
  transform-origin: top;
}

.custom-expand-enter-from,
.custom-expand-leave-to {
  opacity: 0;
  transform: translateY(-8px) scaleY(0.97);
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

.option-button {
  min-width: 148px;
  padding: 12px 16px;
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
    transform: translateY(1px) scale(0.98);
    box-shadow: 0 1px 0 var(--primary-color);
  }

  &.active {
    border-color: var(--primary-color);
    background: var(--primary-color);
    color: var(--text-color);
    box-shadow: 0 4px 0 rgba(0, 0, 0, 0.2);
  }
}

.input-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(210px, 280px));
  justify-content: start;
  gap: 16px;
}

.input-field {
  display: flex;
  flex-direction: column;
  gap: 8px;
  width: min(100%, 320px);

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

.unit-control {
  display: grid;
  grid-template-columns: minmax(0, 220px) auto;
  align-items: center;
  gap: 10px;

  :deep(.p-inputnumber),
  :deep(.p-inputtext) {
    width: 100%;
    background: var(--settings-input-bg) !important;
    color: var(--settings-input-text) !important;
    caret-color: var(--settings-input-text);
  }

  :deep(.p-inputtext::placeholder) { color: var(--settings-input-placeholder) !important; opacity: 1; }

  > span {
    color: var(--surface-400);
    font-size: 0.85rem;
    font-weight: 600;
    white-space: nowrap;
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
    cursor: pointer;
    display: inline-flex;
    align-items: center;
    gap: 6px;
  }
}

.exclude-input {
  width: min(620px, 100%);
  padding: 8px 12px;
  border: 2px solid var(--surface-200);
  background: var(--settings-input-bg);
  color: var(--settings-input-text);
  caret-color: var(--settings-input-text);
  font-family: inherit;
  font-size: 0.9rem;
  resize: vertical;

  &:focus {
    outline: none;
    border-color: var(--primary-color);
  }


  &::placeholder { color: var(--settings-input-placeholder); opacity: 1; }
}

.memphis-text-input,
.memphis-select {
  width: min(360px, 100%);
  padding: 8px 12px;
  border: 2px solid var(--surface-200);
  background: var(--settings-input-bg);
  color: var(--settings-input-text);
  caret-color: var(--settings-input-text);
  font-family: inherit;
  font-size: 0.9rem;
  transition: border-color 0.2s ease;

  &:focus {
    outline: none;
    border-color: var(--primary-color);
  }
}

.language-select {
  width: min(220px, 100%);
}

.memphis-select {
  cursor: pointer;
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

.resource-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
  gap: 10px;
  margin-bottom: 24px;
}

.resource-card {
  min-width: 0;
  min-height: 68px;
  padding: 12px 14px;
  border: 2px solid var(--surface-200);
  background: var(--surface-card);
  color: var(--text-color);
  text-decoration: none;
  display: flex;
  flex-direction: column;
  justify-content: center;
  gap: 4px;
  overflow: hidden;
  transition: transform 0.16s ease, border-color 0.16s ease, box-shadow 0.16s ease;

  &:hover {
    border-color: var(--primary-color);
    transform: translateY(-2px);
    box-shadow: 0 3px 0 var(--primary-color);
  }

  &:active {
    transform: translateY(1px) scale(0.99);
    box-shadow: 0 1px 0 var(--primary-color);
  }
}

.resource-name {
  font-weight: 700;
  overflow-wrap: anywhere;
  word-break: break-word;
}

.resource-license {
  color: var(--surface-400);
  font-size: 0.82rem;
  overflow-wrap: anywhere;
}

@media (max-width: 640px) {
  .theme-group {
    grid-template-columns: 1fr;
    gap: 8px;
  }

  .theme-option {
    min-width: 138px;
  }

  .button-group {
    flex-wrap: wrap;
  }

  .custom-theme-grid,
  .custom-color-list {
    grid-template-columns: 1fr;
  }
}
</style>
