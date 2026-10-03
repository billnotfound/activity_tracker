<!--
  Settings page — backend config + theme settings + language switcher
-->
<template>
  <div class="settings-page" :data-page-ready="initialRenderReady">
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
          <div class="palette-controls">
          <div class="palette-controls-content" :class="{ blurred: autoColor }">
            <div class="theme-groups">
              <div v-for="mode in customModes" :key="mode" class="theme-group">
                <span class="theme-group-label">{{ t(`settings.appearance.${mode}`) }}</span>
                <div class="theme-group-body">
                  <div class="theme-selector">
                    <button
                      v-for="th in mode === 'light' ? lightThemes : darkThemes"
                      :key="th.id"
                      class="theme-option"
                      :class="{ active: mode === 'light' ? (!isDark && lightTheme === th.id) : (isDark && darkTheme === th.id) }"
                      @click="selectTheme(th)"
                    >
                      <ThemeOrb :colors="themePreviewColors(th)" />
                      <span class="theme-name">{{ th.name }}</span>
                    </button>
                    <button
                      class="custom-palette-toggle"
                      :data-mode="mode"
                      :aria-expanded="openCustomPalettes[mode]"
                      :title="t(`settings.appearance.custom.${mode}Scheme`)"
                      @click="openCustomPalettes[mode] = !openCustomPalettes[mode]"
                    >
                      <X v-if="openCustomPalettes[mode]" :size="18" />
                      <Plus v-else :size="18" />
                    </button>
                  </div>
                  <Transition name="custom-expand">
                    <section v-if="openCustomPalettes[mode]" class="custom-theme-editor" :data-mode="mode">
                  <div class="custom-theme-editor-head">
                    <strong>{{ t(`settings.appearance.custom.${mode}Scheme`) }}</strong>
                    <ThemeOrb :colors="customPreview(mode)" class="custom-orb" />
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
                  </Transition>
                </div>
              </div>
            </div>
          </div>
          <div v-if="autoColor" class="auto-palette-lock">
            <span>{{ t('settings.appearance.autoColorEnabled') }}</span>
          </div>
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
              data-testid="auto-color-toggle"
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
                <SettingHelp :text="t('settings.windowPollHelp')" />
              </label>
              <div class="unit-control"><InputNumber v-model="form.windowPollSeconds" :min="1" :placeholder="t('settings.windowPollPlaceholder')" /><span>{{ t('time.seconds.suffix') }}</span></div>
            </div>
            <div class="input-field">
              <label>
                {{ t('settings.processPollLabel') }}
                <SettingHelp :text="t('settings.processPollHelp')" />
              </label>
              <div class="unit-control"><InputNumber v-model="form.processPollSeconds" :min="5" :placeholder="t('settings.processPollPlaceholder')" /><span>{{ t('time.seconds.suffix') }}</span></div>
            </div>
            <div class="input-field">
              <label>
                {{ t('settings.mediaPollLabel') }}
                <SettingHelp :text="t('settings.mediaPollHelp')" />
              </label>
              <div class="unit-control"><InputNumber v-model="form.mediaPollSeconds" :min="1" :placeholder="t('settings.mediaPollPlaceholder')" /><span>{{ t('time.seconds.suffix') }}</span></div>
            </div>
          </div>

          <div class="divider"></div>

          <h3 class="section-title">{{ t('settings.tracking.idleDetection') }}</h3>
          <div class="input-field">
            <label>
              {{ t('settings.idleThresholdLabel') }}
              <SettingHelp :text="t('settings.idleThresholdHelp')" />
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
                <SettingHelp :text="t('settings.fullscreenBypassHelp')" />
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
                <SettingHelp :text="t('settings.mergeSwitchesHelp')" />
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
            <div class="input-field"><label>{{ t('settings.timeAnomalyThresholdSeconds') }} <SettingHelp :text="t('settings.timeAnomalyThresholdHelp')" /></label><div class="unit-control"><InputNumber v-model="form.timeAnomalyThresholdSeconds" :min="30" /><span>{{ t('time.seconds.suffix') }}</span></div></div>
            <div class="input-field"><label>{{ t('settings.ntpEpsilonSeconds') }} <SettingHelp :text="t('settings.ntpEpsilonHelp')" /></label><div class="unit-control"><InputNumber v-model="form.ntpEpsilonSeconds" :min="1" /><span>{{ t('time.seconds.suffix') }}</span></div></div>
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
              <SettingHelp :text="t('settings.retentionHelp')" />
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
import { ref, inject, onMounted, onBeforeUnmount, watch, nextTick } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useI18n } from '../i18n/index.js'
import { useTheme } from '../composables/useTheme.js'
import { useSettingsTheme } from '../composables/useSettingsTheme.js'
import { useSettingsData } from '../composables/useSettingsData.js'
import { thirdPartyResources } from '../data/thirdPartyResources.js'
import MemphisCard from '../components/MemphisCard.vue'
import TagsView from './Tags.vue'
import TimeAnomalyView from './TimeAnomaly.vue'
import ThemeOrb from '../components/ThemeOrb.vue'
import SettingHelp from '../components/SettingHelp.vue'
import Tabs from 'primevue/tabs'
import TabList from 'primevue/tablist'
import Tab from 'primevue/tab'
import TabPanels from 'primevue/tabpanels'
import TabPanel from 'primevue/tabpanel'
import InputNumber from 'primevue/inputnumber'
import Button from 'primevue/button'
import { ArrowRight, Grid, RefreshCw, Image, Trash2, X, Check, Plus } from '@lucide/vue'

const apiBase = inject('apiBase')
const { t, locale, setLocale } = useI18n()
const theme = useTheme()
const route = useRoute()
const router = useRouter()
const {
  isDark, autoColor, lightTheme, darkTheme, pageTransition,
  customColorKeys, customModes, openCustomPalettes, customDrafts, customSaved,
  languages, lightThemes, darkThemes, selectTheme, themePreviewColors,
  customDraftValid, customPreview, saveCustomScheme,
} = useSettingsTheme(theme)
const validTabs = new Set(['appearance', 'tracking', 'tags', 'time', 'database', 'licenses'])
const activeTab = ref(validTabs.has(String(route.query.section)) ? String(route.query.section) : 'appearance')
const settingsTabsRef = ref(null)
const settingsTabFrameStyle = ref({ opacity: 0 })
const settingsTabMoving = ref(false)
let settingsTabTarget = null
let settingsTabTimer = null

watch(activeTab, value => {
  const nextQuery = { ...route.query, section: value === 'appearance' ? undefined : value }
  const currentSection = validTabs.has(String(route.query.section)) ? String(route.query.section) : 'appearance'
  if (currentSection !== value) router.replace({ query: nextQuery })
  nextTick(() => moveSettingsTabFrame(activeSettingsTab()))
})

watch(() => route.query.section, section => {
  const nextTab = validTabs.has(String(section)) ? String(section) : 'appearance'
  if (activeTab.value !== nextTab) activeTab.value = nextTab
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

const {
  form, timeSourceModes, excludeText, saving, cleaning, clearingIcons,
  dbStats, cleanupResult, iconCacheResult, resetConfirm, resetting, resetResult,
  statusClass, statusText, loadSettings, saveSettings, loadDbStats,
  runCleanup, clearIconCache, runReset,
} = useSettingsData({ apiBase, t })
const initialRenderReady = ref(false)

onMounted(async () => {
  await nextTick()
  moveSettingsTabFrame(activeSettingsTab())
  await loadSettings()
  await loadDbStats()
  initialRenderReady.value = true
})

onBeforeUnmount(() => {
  clearTimeout(settingsTabTimer)
})
</script>

<style lang="scss" scoped src="../styles/views/settings.scss"></style>
