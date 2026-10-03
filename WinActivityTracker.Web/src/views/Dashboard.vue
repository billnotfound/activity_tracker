<!--
  Dashboard view — Memphis style, ECharts charts, auto-refresh every 2s
-->
<template>
  <div class="dashboard" :data-page-ready="initialRenderReady">
    <!-- Period selector -->
    <div class="period-selector mb-3">
      <div class="period-buttons" ref="periodButtonsRef">
        <div class="sliding-frame" :style="frameStyle" v-show="frameReady"></div>
        <button
          v-for="p in periods"
          :key="p.key"
          class="period-btn"
          :class="{ active: period === p.key }"
          @click="setPeriod(p.key)"
        >
          {{ p.label }}
        </button>
      </div>
      <div class="date-wheels" :class="{ dimmed: !isToday }">
        <TimeWheel v-model="wheelYear" :items="yearOptions" wide :label="t('common.year')" @carry="(d) => carryDate('year', d)" />
        <TimeWheel v-model="wheelMonth" :items="monthOptions" :label="t('common.month')" @carry="(d) => carryDate('month', d)" />
        <TimeWheel v-model="wheelDay" :items="dayOptions" :label="t('common.day')" @carry="(d) => carryDate('day', d)" />
      </div>
    </div>

    <!-- Error -->
    <div v-if="error" class="error-banner mb-3">
      {{ error }}
      <button class="close-btn" @click="error = ''" :aria-label="t('common.close')"><X :size="16" /></button>
    </div>

    <!-- Charts row -->
    <div class="charts-row mb-3">
      <MemphisCard class="chart-card" :class="{ 'context-hover-locked': contextMenuVisible && contextSource === 'focus' }">
        <h3 class="card-title">
          {{ t('dashboard.card.focusDurationTop10') }}
          <small v-if="showActionHint" class="chart-action-hint">{{ t('processActions.chartHint') }}</small>
        </h3>
        <MemphisSkeleton v-if="loading" :lines="5" />
        <div v-else ref="focusChartRef" class="chart-container"></div>
      </MemphisCard>

      <MemphisCard class="data-card">
        <h3 class="card-title">
          {{ t('dashboard.card.recentMedia') }}
          <small class="total-listen">
            <span>{{ t('dashboard.totalListen', { duration: totalListenFmt }) }}</span>
            <LoaderCircle v-if="listenTotalLoading" :size="13" class="listen-refresh-spinner spin" />
          </small>
        </h3>
        <MemphisSkeleton v-if="loading" :lines="8" />
        <div v-else class="table-wrapper">
          <table class="media-table">
            <thead>
              <tr>
                <th>{{ t('dashboard.media.duration') }}</th>
                <th>{{ t('dashboard.media.status') }}</th>
                <th>{{ t('dashboard.media.song') }}</th>
                <th>{{ t('dashboard.media.artist') }}</th>
              </tr>
            </thead>
            <tbody>
              <tr
                v-for="m in displayMedia.slice().reverse()"
                :key="m.id || m.startTime"
                :class="{ playing: m.playbackStatus === 'Playing', anomalous: m.isAnomalous, selected: mediaMenuVisible && selectedMedia?.startTime === m.startTime }"
                tabindex="0"
                @click="openMediaJump(m, $event)"
                @contextmenu.prevent="openMediaJump(m, $event)"
                @keydown.enter.prevent="openMediaJump(m, $event)"
              >
                <td>
                  <span :key="m.durationFmt" class="flicker-text" :class="{ 'anomaly-label': m.isAnomalous }">
                    {{ m.durationFmt }}
                  </span>
                </td>
                <td>
                  <span :key="m.playbackStatus" class="flicker-text playback-status">
                    <Play v-if="m.playbackStatus === 'Playing'" :size="16" />
                    <Pause v-else :size="16" />
                  </span>
                </td>
                <td><span :key="m.title" class="flicker-text">{{ m.title }}</span></td>
                <td><span :key="m.artist" class="flicker-text">{{ m.artist }}</span></td>
              </tr>
              <tr v-if="!displayMedia.length">
                <td colspan="4" class="no-data">{{ t('dashboard.media.noData') }}</td>
              </tr>
            </tbody>
          </table>
        </div>
      </MemphisCard>
    </div>

    <div class="overview-row mb-3">
      <!-- Focus drill-down keeps the existing media ring and water level. -->
      <MemphisCard class="overview-card" :class="{ 'context-hover-locked': contextMenuVisible && contextSource === 'pie' }">
        <div class="overview-heading">
          <h3 class="card-title">{{ overviewPieHeading }}</h3>
          <button v-if="selectedOverviewProcess || piePageOffset" type="button" class="pie-back" @click="showPreviousPiePage">
            <ArrowLeft :size="16" /> {{ t('dashboard.pie.back') }}
          </button>
        </div>
        <MemphisSkeleton v-if="loading" :lines="4" />
        <div v-else class="pie-chart-wrapper">
          <div ref="pieChartRef" class="pie-chart-container"></div>
          <LoaderCircle v-if="overviewTitleLoading" :size="24" class="overview-title-loading spin" />
          <div v-else-if="selectedOverviewProcess && !overviewTitleDurations.length" class="tag-pie-empty">{{ t('dashboard.pie.noTitles') }}</div>
          <div class="water-ball" :style="{ '--fill': usagePercent }">
            <div class="water-body">
              <svg class="wave-band" viewBox="0 0 480 32" preserveAspectRatio="none">
                <path :d="waterWavePath" fill="currentColor"/>
              </svg>
            </div>
            <span class="water-text">{{ usageFmt }}</span>
          </div>
          <div v-if="usagePercent >= 100" class="usage-warning">{{ t('dashboard.usageWarning') }}</div>
        </div>
      </MemphisCard>

      <MemphisCard class="tag-pie-card" :class="{ 'context-hover-locked': contextMenuVisible && contextSource === 'tagPie' }">
        <div class="overview-heading">
          <h3 class="card-title">{{ tagPieHeading }}</h3>
          <button v-if="selectedTag" type="button" class="pie-back" @click="showPreviousTagPiePage">
            <ArrowLeft :size="16" /> {{ t('dashboard.pie.back') }}
          </button>
        </div>
        <MemphisSkeleton v-if="loading" :lines="4" />
        <div v-else class="tag-pie-wrapper">
          <div ref="tagPieChartRef" class="tag-pie-chart"></div>
          <LoaderCircle v-if="tagTitleLoading" :size="24" class="tag-pie-loading spin" />
          <div v-if="!tagDurations.length" class="tag-pie-empty">{{ t('dashboard.pie.noTags') }}</div>
          <div v-else-if="selectedTagProcess && !tagTitleLoading && !tagTitleDurations.length" class="tag-pie-empty">{{ t('dashboard.pie.noTitles') }}</div>
        </div>
      </MemphisCard>
    </div>

    <ProcessActionDrawer
      v-model:visible="actionDrawerVisible"
      :process="selectedProcess"
      :range-start="actionRange.start"
      :range-end="actionRange.end"
      :process-color="selectedProcessColor"
      :icon="selectedProcessIcon"
      :initial-view="drawerView"
      :initial-tag-names="drawerTagNames"
      :tag-scope="contextSource === 'title' ? 'process-title' : 'process'"
      return-to-popup
      @select-time="openTimeSelection"
      @changed="loadSummary"
      @return-to-menu="reopenContextMenu"
    />
    <ProcessContextMenu
      :visible="contextMenuVisible"
      @update:visible="setContextMenuVisible"
      :page="contextSource === 'title' ? 'title' : 'dashboard'"
      :process-name="selectedProcess.processName || ''"
      :window-title="selectedProcess.windowTitle || ''"
      :raw-window-title="selectedProcess.rawWindowTitle || selectedProcess.windowTitle || ''"
      :x="contextPoint.x"
      :y="contextPoint.y"
      :replace-handler="replaceDashboardTitle"
      @choose="chooseContextAction"
      @create-tag="openNewTagDrawer"
      @tags-saved="loadSummary"
    />
    <MediaJumpMenu
      v-model:visible="mediaMenuVisible"
      :item="selectedMedia"
      :x="mediaMenuPoint.x"
      :y="mediaMenuPoint.y"
      @confirm="openMediaHistory"
    />
  </div>
</template>

<script setup>
import { ref, inject, onMounted, onUnmounted, computed, nextTick, watch } from 'vue'
import { useRouter } from 'vue-router'
import { fmtShortDur, parseUtcTs, toLocalDateString, toLocalDatetimeString } from '../utils/time.js'
import { mergeByProcessName } from '../utils/process.js'
import { useI18n } from '../i18n/index.js'
import { useTheme } from '../composables/useTheme.js'
import { useDashboardPeriod } from '../composables/useDashboardPeriod.js'
import { echarts } from '../utils/echartsInit.js'
import { buildDisplayMedia, buildMediaRing } from '../utils/dashboardMedia.js'
import { escapeHtml } from '../utils/html.js'
import { configWriteHeaders, ensureConfigWriteSucceeded } from '../utils/configVersion.js'
import MemphisCard from '../components/MemphisCard.vue'
import MemphisSkeleton from '../components/MemphisSkeleton.vue'
import TimeWheel from '../components/TimeWheel.vue'
import ProcessActionDrawer from '../components/ProcessActionDrawer.vue'
import ProcessContextMenu from '../components/ProcessContextMenu.vue'
import MediaJumpMenu from '../components/MediaJumpMenu.vue'
import { ArrowLeft, LoaderCircle, Play, Pause, X } from '@lucide/vue'

const apiBase = inject('apiBase')
const { t } = useI18n()
const { isDark, applyAutoColor } = useTheme()
const router = useRouter()

// Cache icon data to avoid re-fetching every refresh (2s interval)
const iconCache = new Map()
const ICON_CACHE_LIMIT = 500

const periods = [
  { key: 'today', label: t('dashboard.periods.today') },
  { key: 'week', label: t('dashboard.periods.week') },
  { key: 'month', label: t('dashboard.periods.month') },
  { key: 'halfYear', label: t('dashboard.periods.halfYear') },
  { key: 'year', label: t('dashboard.periods.year') },
]

function dashboardIconAtTime() {
  const [, toDate] = periodRange()
  return new Date(`${toDate}T23:59:59`).toISOString()
}

const {
  period, isToday, pickDate, earliestDate,
  wheelYear, wheelMonth, wheelDay,
  yearOptions, monthOptions, dayOptions,
  carryDate, resetDateToToday, periodButtonsRef,
  frameStyle, frameReady, updateFrame, periodRange,
} = useDashboardPeriod({ onDateChange: onPickDate })
const mergeSameProcess = ref(true)
const summary = ref([])
const tagDurations = ref([])
const piePageOffset = ref(0)
const selectedOverviewProcess = ref(null)
const overviewTitleDurations = ref([])
const overviewTitleLoading = ref(false)
const selectedTag = ref(null)
const selectedTagProcess = ref(null)
const tagTitleDurations = ref([])
const tagTitleLoading = ref(false)
const tagPiePageOffset = ref(0)
const totalSleepSeconds = ref(0)
const media = ref([])
const totalListenSeconds = ref(0)
const listenTotalLoading = ref(false)
const mediaMenuVisible = ref(false)
const selectedMedia = ref(null)
const mediaMenuPoint = ref({ x: 0, y: 0 })
const error = ref('')
const loading = ref(true)
const initialRenderReady = ref(false)
const actionDrawerVisible = ref(false)
const contextMenuVisible = ref(false)
const drawerView = ref('replace')
const drawerTagNames = ref([])
const contextPoint = ref({ x: 0, y: 0 })
const contextSource = ref('focus')
let contextClosedAt = 0
let contextOpenedAt = 0
const selectedProcess = ref({})
const selectedProcessColor = ref('var(--primary-color)')
const selectedProcessIcon = ref('')
const showActionHint = ref(localStorage.getItem('wta-process-actions-seen') !== '1')
const actionRange = computed(() => {
  const [from, to] = periodRange()
  return { start: new Date(`${from}T00:00:00`), end: new Date(`${to}T23:59:59`) }
})
const tagPieHeading = computed(() => {
  if (selectedTagProcess.value)
    return `${selectedTag.value?.tag || ''} · ${selectedTagProcess.value.displayName || selectedTagProcess.value.processName}`
  return selectedTag.value ? selectedTag.value.tag : t('dashboard.card.tagDurationPie')
})
const overviewPieHeading = computed(() => selectedOverviewProcess.value
  ? selectedOverviewProcess.value.displayName || selectedOverviewProcess.value.processName
  : t('dashboard.card.overviewPie'))

const TOOLTIP_GAP = 18
const TOOLTIP_RIGHT_GUARD = 64
const TOOLTIP_EDGE_GUARD = 12

function visibleFloatingMenuRect() {
  for (const selector of ['.context-anchor', '.media-menu-anchor']) {
    const element = document.querySelector(selector)
    if (element && element.getClientRects().length) return element.getBoundingClientRect()
  }
  return null
}

function overlaps(a, b) {
  return a.left < b.right && a.right > b.left && a.top < b.bottom && a.bottom > b.top
}

function smartTooltipPosition(point, _params, dom, _rect, size) {
  const [viewWidth, viewHeight] = size.viewSize
  const [contentWidth, contentHeight] = size.contentSize
  dom.dataset.cursorX = String(point[0])
  dom.dataset.cursorY = String(point[1])
  let x = point[0] + TOOLTIP_GAP
  const leftX = point[0] - contentWidth - TOOLTIP_GAP
  if (x + contentWidth > viewWidth - TOOLTIP_RIGHT_GUARD) x = leftX
  x = Math.max(TOOLTIP_EDGE_GUARD,
    Math.min(x, viewWidth - contentWidth - TOOLTIP_RIGHT_GUARD))
  let y = Math.max(TOOLTIP_EDGE_GUARD,
    Math.min(point[1] - contentHeight / 2, viewHeight - contentHeight - TOOLTIP_EDGE_GUARD))

  const hostRect = dom.offsetParent?.getBoundingClientRect?.()
  const menuRect = visibleFloatingMenuRect()
  if (hostRect && menuRect) {
    const candidate = {
      left: hostRect.left + x, top: hostRect.top + y,
      right: hostRect.left + x + contentWidth, bottom: hostRect.top + y + contentHeight,
    }
    if (overlaps(candidate, menuRect)) {
      const opposite = x > point[0] ? leftX : point[0] + TOOLTIP_GAP
      if (opposite >= TOOLTIP_EDGE_GUARD
        && opposite + contentWidth <= viewWidth - TOOLTIP_RIGHT_GUARD) x = opposite
      else y = menuRect.top > hostRect.top + point[1]
        ? Math.max(TOOLTIP_EDGE_GUARD, point[1] - contentHeight - TOOLTIP_GAP)
        : Math.min(viewHeight - contentHeight - TOOLTIP_EDGE_GUARD, point[1] + TOOLTIP_GAP)
    }
  }
  return [x, y]
}

function repositionVisibleTooltips() {
  const menuRect = visibleFloatingMenuRect()
  if (!menuRect) return
  for (const tooltip of document.querySelectorAll('.wta-chart-tooltip')) {
    if (getComputedStyle(tooltip).display === 'none' || !tooltip.offsetWidth) continue
    const tooltipRect = tooltip.getBoundingClientRect()
    if (!overlaps(tooltipRect, menuRect)) continue
    const hostRect = tooltip.offsetParent?.getBoundingClientRect?.()
    if (!hostRect) continue
    const cursorX = Number(tooltip.dataset.cursorX)
    const leftOfCursor = hostRect.left + cursorX - tooltipRect.width - TOOLTIP_GAP
    const rightOfMenu = menuRect.right + TOOLTIP_GAP
    let viewportLeft = leftOfCursor >= TOOLTIP_EDGE_GUARD
      ? leftOfCursor : rightOfMenu
    viewportLeft = Math.max(TOOLTIP_EDGE_GUARD,
      Math.min(viewportLeft, window.innerWidth - tooltipRect.width - TOOLTIP_RIGHT_GUARD))
    const localLeft = viewportLeft - hostRect.left
    const localTop = tooltipRect.top - hostRect.top
    // ECharts positions HTML tooltips with translate3d rather than `left`, so
    // update the transform used by its renderer instead of a shadowed property.
    tooltip.style.left = '0px'
    tooltip.style.top = '0px'
    tooltip.style.transform = `translate3d(${localLeft}px, ${localTop}px, 0)`
  }
}

watch([contextMenuVisible, mediaMenuVisible], ([processOpen, mediaOpen]) => {
  if (!processOpen && !mediaOpen) return
  nextTick(() => {
    repositionVisibleTooltips()
    requestAnimationFrame(repositionVisibleTooltips)
    setTimeout(repositionVisibleTooltips, 100)
  })
})

function openProcessActions(processName, color, nativeEvent = null, source = 'focus', displayName = '', details = {}) {
  if (contextMenuVisible.value) {
    if (performance.now() - contextOpenedAt < 260) return
    setContextMenuVisible(false)
    return
  }
  if (performance.now() - contextClosedAt < 320) return
  if (!processName || processName === t('dashboard.pie.other')) return
  mediaMenuVisible.value = false
  selectedProcess.value = { processName, displayName: displayName || processName, ...details }
  selectedProcessColor.value = color || 'var(--primary-color)'
  const atTime = dashboardIconAtTime()
  selectedProcessIcon.value = iconCache.get(`${processName}|${atTime}`)?.icon || ''
  showActionHint.value = false
  localStorage.setItem('wta-process-actions-seen', '1')
  contextPoint.value = {
    x: nativeEvent?.clientX ?? window.innerWidth / 2,
    y: nativeEvent?.clientY ?? window.innerHeight / 2,
  }
  contextSource.value = source
  contextMenuVisible.value = true
  contextOpenedAt = performance.now()
}

function setContextMenuVisible(value) {
  if (!value && contextMenuVisible.value) contextClosedAt = performance.now()
  contextMenuVisible.value = value
}

function chooseContextAction(action) {
  if (contextSource.value === 'title' && action === 'isolate') {
    const [from, to] = periodRange()
    router.push({
      path: '/history',
      query: {
        process: selectedProcess.value.processName,
        isolate: '1',
        from: `${from}T00:00`,
        to: `${to}T23:59`,
      },
    })
    return
  }
  drawerView.value = action
  actionDrawerVisible.value = true
}

function openTitleActions(item, nativeEvent, source = 'title') {
  const processName = item._processName || selectedTagProcess.value?.processName
    || selectedOverviewProcess.value?.processName
  if (!processName) return
  openProcessActions(processName, item.itemStyle?.color, nativeEvent, source,
    item._displayName || selectedTagProcess.value?.displayName
      || selectedOverviewProcess.value?.displayName || processName,
    { windowTitle: item._title || item.name, rawWindowTitle: item._title || item.name })
  contextSource.value = 'title'
}

function openNewTagDrawer(selectedTags) {
  drawerTagNames.value = [...selectedTags, '']
  drawerView.value = 'tag'
  actionDrawerVisible.value = true
}

function reopenContextMenu() {
  requestAnimationFrame(() => { contextMenuVisible.value = true })
}

async function replaceDashboardTitle(replacement) {
  const statusResponse = await fetch(`${apiBase}/api/tags/status`)
  if (!statusResponse.ok) throw new Error(`API ${statusResponse.status}`)
  const status = await statusResponse.json()
  const processName = selectedProcess.value.processName
  const kept = (status.titleRules?.rules || []).filter(rule =>
    String(rule.process || '').toLowerCase() !== processName.toLowerCase())
  kept.push({
    process: processName,
    title: replacement,
    titleRegex: null,
    titleReplacement: null,
    applyOnWrite: false,
  })
  const response = await fetch(`${apiBase}/api/title-rules/save`, {
    method: 'PUT',
    headers: configWriteHeaders(status.titleRules?.lastWrite),
    body: JSON.stringify(kept),
  })
  await ensureConfigWriteSucceeded(response)
  await loadSummary()
}

function openTimeSelection(item) {
  const [from, to] = periodRange()
  router.push({ path: '/history', query: { process: item.processName, timeSelect: '1', from: `${from}T00:00`, to: `${to}T23:59` } })
}

function openMediaJump(item, event) {
  setContextMenuVisible(false)
  selectedMedia.value = item
  mediaMenuPoint.value = {
    x: event?.clientX ?? window.innerWidth / 2,
    y: event?.clientY ?? window.innerHeight / 2,
  }
  mediaMenuVisible.value = true
}

function openMediaHistory(item) {
  if (!item?.startTime) return
  const start = parseUtcTs(item.startTime)
  const end = item.endTime ? parseUtcTs(item.endTime) : new Date()
  if (!start || !end) return
  const duration = Math.max(60_000, end - start)
  const span = Math.min(24 * 60 * 60 * 1000, Math.max(3 * 60 * 60 * 1000, duration * 12))
  const center = (start.getTime() + end.getTime()) / 2
  router.push({
    path: '/history',
    query: {
      media: '1',
      mediaAt: item.startTime,
      mediaId: item.id || undefined,
      from: toLocalDatetimeString(new Date(center - span / 2)),
      to: toLocalDatetimeString(new Date(center + span / 2)),
    },
  })
}

const focusChartRef = ref(null)
let focusChart = null
let focusRenderId = 0
const pieChartRef = ref(null)
let pieChart = null
let pieRenderId = 0
let pieDrillLocked = false
let pieDrillTimer = null
const tagPieChartRef = ref(null)
let tagPieChart = null
let tagPieRenderId = 0
let tagPieDrillLocked = false
let tagPieDrillTimer = null
let timer = null
let longRangeBasePayload = null
let longRangeListenBase = null
// Race guard: each loadSummary() call increments loadId; stale calls
// bail before writing summary.value / rendering charts.
let loadId = 0
// Aborted on unmount so in-flight fetches don't keep running after the
// view is gone (their result writes would be dropped by loadId anyway,
// but this saves the bandwidth/CPU).
const abortController = new AbortController()

function setPeriod(p) {
  piePageOffset.value = 0
  selectedOverviewProcess.value = null
  overviewTitleDurations.value = []
  selectedTag.value = null
  selectedTagProcess.value = null
  tagTitleDurations.value = []
  longRangeBasePayload = null
  longRangeListenBase = null
  tagPiePageOffset.value = 0
  period.value = p
  if (p === 'today') {
    const now = new Date()
    const dateChanged = wheelYear.value !== now.getFullYear()
      || wheelMonth.value !== now.getMonth() + 1
      || wheelDay.value !== now.getDate()
    if (dateChanged) {
      resetDateToToday() // the wheel watcher starts the load once
      return
    }
  }
  startPolling()
}

function onPickDate() {
  piePageOffset.value = 0
  selectedOverviewProcess.value = null
  overviewTitleDurations.value = []
  selectedTag.value = null
  selectedTagProcess.value = null
  tagTitleDurations.value = []
  longRangeBasePayload = null
  longRangeListenBase = null
  tagPiePageOffset.value = 0
  startPolling()
}

function showPreviousPiePage() {
  if (pieDrillLocked) return
  if (selectedOverviewProcess.value) {
    selectedOverviewProcess.value = null
    overviewTitleDurations.value = []
  } else {
    piePageOffset.value = Math.max(0, piePageOffset.value - 5)
  }
  lockPieDrill()
  renderPieChart(summary.value)
}

function lockPieDrill() {
  pieDrillLocked = true
  clearTimeout(pieDrillTimer)
  pieDrillTimer = setTimeout(() => { pieDrillLocked = false }, 620)
}

function lockTagPieDrill() {
  tagPieDrillLocked = true
  clearTimeout(tagPieDrillTimer)
  tagPieDrillTimer = setTimeout(() => { tagPieDrillLocked = false }, 520)
}

function showPreviousTagPiePage() {
  if (tagPieDrillLocked) return
  if (selectedTagProcess.value) {
    selectedTagProcess.value = null
    tagTitleDurations.value = []
  } else if (tagPiePageOffset.value > 0) tagPiePageOffset.value = Math.max(0, tagPiePageOffset.value - 5)
  else selectedTag.value = null
  lockTagPieDrill()
  renderTagPieChart(tagDurations.value)
}

async function openTagProcessTitles(item) {
  selectedTagProcess.value = {
    processName: item._processName,
    displayName: item.name || item._processName,
  }
  tagTitleDurations.value = []
  tagTitleLoading.value = true
  lockTagPieDrill()
  try {
    tagTitleDurations.value = await fetchTitleBreakdown(
      item._processName, selectedTag.value?.tag || '')
  } catch (error) {
    if (error.name !== 'AbortError') console.warn('Failed to load title breakdown:', error)
  } finally {
    tagTitleLoading.value = false
    await renderTagPieChart(tagDurations.value)
  }
}

async function fetchTitleBreakdown(processName, tag = '') {
  const [from, to] = periodRange()
  const query = new URLSearchParams({
    from: `${from}T00:00:00`,
    to: `${to}T23:59:59`,
    process: processName,
  })
  if (tag) query.set('tag', tag)
  const response = await fetch(`${apiBase}/api/summary/title-breakdown?${query}`, { signal: abortController.signal })
  if (!response.ok) throw new Error(`API ${response.status}`)
  return (await response.json()).titles || []
}

async function openOverviewProcessTitles(item) {
  selectedOverviewProcess.value = {
    processName: item._processName,
    displayName: item.name || item._processName,
  }
  overviewTitleDurations.value = []
  overviewTitleLoading.value = true
  lockPieDrill()
  try {
    overviewTitleDurations.value = await fetchTitleBreakdown(item._processName)
  } catch (error) {
    if (error.name !== 'AbortError') console.warn('Failed to load overview title breakdown:', error)
  } finally {
    overviewTitleLoading.value = false
    await renderPieChart(summary.value)
  }
}

function startPolling() {
  stopPolling()
  loadSummary()
  schedulePolling()
}

function schedulePolling() {
  stopPolling()
  if (period.value === 'today') {
    timer = setInterval(loadSummary, 2000)
    return
  }
  const [, to] = periodRange()
  if (to === toLocalDateString()) timer = setInterval(refreshLongRangeTail, 10000)
}

function stopPolling() {
  if (timer) {
    clearInterval(timer)
    timer = null
  }
}

function handleVisibility() {
  if (document.hidden) stopPolling()
  else startPolling()
}

const displayMedia = computed(() => buildDisplayMedia(media.value, {
  formatDuration: fmtShortDur,
  formatAnomaly: count => t('dashboard.media.anomalousGroup', { count }),
}))

const totalListenFmt = computed(() => fmtShortDur(totalListenSeconds.value))

const usageTotalSec = computed(() => {
  if (!summary.value || !summary.value.length) return 0
  return summary.value.reduce((s, i) => s + i.totalSeconds, 0)
})

const usageTargetSec = computed(() => {
  const [from, to] = periodRange()
  const dayCount = Math.max(1, Math.round((new Date(`${to}T00:00:00`) - new Date(`${from}T00:00:00`)) / 86400000) + 1)
  return 86400 * (period.value === 'today' ? 0.7 : 0.5 * dayCount)
})

const usagePercent = computed(() => {
  return Math.min(100, Math.round(usageTotalSec.value / usageTargetSec.value * 100))
})

const waterWavePath = computed(() => {
  const seedText = `${period.value}:${pickDate.value}`
  let seed = [...seedText].reduce((sum, char) => (sum * 31 + char.charCodeAt(0)) >>> 0, 2166136261)
  const random = () => {
    seed = (1664525 * seed + 1013904223) >>> 0
    return seed / 4294967296
  }
  const step = 40
  const baseline = 13
  const amplitude = 2.2
  const points = Array.from({ length: 13 }, (_, index) => ({
    x: index * step,
    y: baseline + (random() * 2 - 1) * amplitude,
  }))
  // Periodic endpoints keep the drifting line seamless; Catmull–Rom control
  // points are converted to cubic Béziers for a smooth, hand-bent boundary.
  points[12].y = points[0].y
  points[11].y = points[1].y
  let path = `M${points[0].x},${points[0].y.toFixed(1)}`
  for (let index = 0; index < 12; index++) {
    const p0 = index === 0 ? { x: -step, y: points[11].y } : points[index - 1]
    const p1 = points[index]
    const p2 = points[index + 1]
    const p3 = index === 11 ? { x: 520, y: points[1].y } : points[index + 2]
    const c1x = p1.x + (p2.x - p0.x) / 6
    const c1y = p1.y + (p2.y - p0.y) / 6
    const c2x = p2.x - (p3.x - p1.x) / 6
    const c2y = p2.y - (p3.y - p1.y) / 6
    path += ` C${c1x.toFixed(1)},${c1y.toFixed(1)} ${c2x.toFixed(1)},${c2y.toFixed(1)} ${p2.x},${p2.y.toFixed(1)}`
  }
  return `${path} L480,32 L0,32 Z`
})

const usageFmt = computed(() => {
  const s = usageTotalSec.value
  if (s < 60) return '<1min'
  const h = Math.floor(s / 3600)
  const m = Math.floor((s % 3600) / 60)
  if (h > 0) return `${h}h ${m}min`
  return `${m}min`
})

onMounted(async () => {
  try {
    const r = await fetch(`${apiBase}/api/settings`, { signal: abortController.signal })
    if (r.ok) {
      const s = await r.json()
      mergeSameProcess.value = s.mergeSameProcessSwitches ?? true
    }
  } catch (e) {
    console.error('Failed to load settings:', e)
  }
  await loadSummary()
  schedulePolling()

  // Add window listeners
  window.addEventListener('resize', handleResize)
  document.addEventListener('visibilitychange', handleVisibility)

  // Fetch earliest record to constrain date wheels
  try {
    const r = await fetch(`${apiBase}/api/db/stats`, { signal: abortController.signal })
    if (r.ok) {
      const stats = await r.json()
      if (stats.oldestRecord) {
        earliestDate.value = parseUtcTs(stats.oldestRecord)
      }
    }
  } catch (e) {
    console.error('Failed to fetch DB stats:', e)
  }

  // Position the sliding frame on the initially active button
  updateFrame()
})

// Watch for theme changes and re-render charts
watch(isDark, () => {
  console.log('Theme changed, re-rendering charts')
  if (focusChart && summary.value && summary.value.length > 0) {
    renderCharts(summary.value)
  }
  if (pieChart && summary.value && summary.value.length > 0) {
    renderPieChart(summary.value)
  }
  if (tagPieChart) renderTagPieChart(tagDurations.value)
})

onUnmounted(() => {
  stopPolling()
  focusRenderId++
  clearTimeout(pieDrillTimer)
  clearTimeout(tagPieDrillTimer)
  clearTimeout(resizeTimer)
  loadId++  // cancel any in-flight load
  abortController.abort()

  // Remove listeners
  window.removeEventListener('resize', handleResize)
  document.removeEventListener('visibilitychange', handleVisibility)

  if (focusChart) {
    focusChart.dispose()
    focusChart = null
  }
  if (pieChart) {
    pieChart.dispose()
    pieChart = null
  }
  if (tagPieChart) {
    tagPieChart.dispose()
    tagPieChart = null
  }
})

// Debounced resize handler
let resizeTimer = null
function handleResize() {
  if (resizeTimer) clearTimeout(resizeTimer)
  resizeTimer = setTimeout(() => {
    if (focusChart) {
      focusChart.resize()
      console.log('Chart resized due to window resize')
      if (summary.value && summary.value.length > 0) {
        renderCharts(summary.value)
        console.log('Chart re-rendered after resize')
      }
    }
    if (pieChart) {
      pieChart.resize()
      if (summary.value && summary.value.length > 0) {
        renderPieChart(summary.value)
      }
    }
    if (tagPieChart) {
      tagPieChart.resize()
      renderTagPieChart(tagDurations.value)
    }
  }, 200) // 200ms debounce
}

async function loadSummary() {
  if (period.value === 'today' && pickDate.value !== toLocalDateString()) {
    resetDateToToday()
    return
  }
  const myLoadId = ++loadId
  await Promise.all([fetchSummary(myLoadId), fetchMedia(myLoadId)])
  if (myLoadId !== loadId) return
  if (summary.value) await renderPieChart(summary.value)
  await renderTagPieChart(tagDurations.value)
  if (myLoadId === loadId) initialRenderReady.value = true
}

function combineSummaryPayload(base, tail, sign = 1) {
  const result = { items: [], tagDurations: [], totalSleepSeconds: 0, totalIdleSeconds: 0 }
  const itemMap = new Map()
  for (const source of [base, tail]) {
    const factor = source === tail ? sign : 1
    for (const item of source?.items || []) {
      const key = String(item.processName || '').toLowerCase()
      if (!itemMap.has(key)) itemMap.set(key, { ...item, totalSeconds: 0, switchCount: 0, adjustedSwitchCount: 0 })
      const out = itemMap.get(key)
      out.totalSeconds += factor * Number(item.totalSeconds || 0)
      out.switchCount += factor * Number(item.switchCount || 0)
      out.adjustedSwitchCount += factor * Number(item.adjustedSwitchCount || 0)
      if (item.displayName) out.displayName = item.displayName
    }
  }
  result.items = [...itemMap.values()].filter(item => item.totalSeconds > .01)

  const tagMap = new Map()
  for (const source of [base, tail]) {
    const factor = source === tail ? sign : 1
    for (const tag of source?.tagDurations || []) {
      if (!tagMap.has(tag.tag)) tagMap.set(tag.tag, new Map())
      const processMap = tagMap.get(tag.tag)
      for (const process of tag.processes || []) {
        const key = String(process.processName || '').toLowerCase()
        if (!processMap.has(key)) processMap.set(key, { ...process, totalSeconds: 0 })
        const out = processMap.get(key)
        out.totalSeconds += factor * Number(process.totalSeconds || 0)
        if (process.displayName) out.displayName = process.displayName
      }
    }
  }
  result.tagDurations = [...tagMap].map(([tag, processMap]) => {
    const processes = [...processMap.values()]
      .filter(item => item.totalSeconds > .01)
      .sort((a, b) => b.totalSeconds - a.totalSeconds)
    return {
      tag,
      totalSeconds: processes.reduce((sum, item) => sum + item.totalSeconds, 0),
      dominantProcess: processes[0]?.processName || '',
      processes,
    }
  }).filter(tag => tag.totalSeconds > .01).sort((a, b) => b.totalSeconds - a.totalSeconds)
  result.totalSleepSeconds = Math.max(0,
    Number(base?.totalSleepSeconds || 0) + sign * Number(tail?.totalSleepSeconds || 0))
  result.totalIdleSeconds = Math.max(0,
    Number(base?.totalIdleSeconds || 0) + sign * Number(tail?.totalIdleSeconds || 0))
  return result
}

async function applySummaryPayload(res, myLoadId) {
  if (myLoadId !== loadId) return
  const rawData = Array.isArray(res) ? res : res.items || []
  tagDurations.value = Array.isArray(res) ? [] : res.tagDurations || []
  if (selectedTag.value) {
    selectedTag.value = tagDurations.value.find(item => item.tag === selectedTag.value.tag) || null
    if (!selectedTag.value) {
      selectedTagProcess.value = null
      tagPiePageOffset.value = 0
    }
  }
  const mergedData = mergeByProcessName(rawData, (item, acc) => {
    acc.totalSeconds += item.totalSeconds
    acc.switchCount += item.switchCount
    if (item.adjustedSwitchCount !== undefined)
      acc.adjustedSwitchCount = (acc.adjustedSwitchCount || 0) + item.adjustedSwitchCount
  })
  mergedData.sort((a, b) => b.totalSeconds - a.totalSeconds)
  summary.value = mergedData
  totalSleepSeconds.value = Array.isArray(res) ? 0 : res.totalSleepSeconds || 0
  error.value = ''
  loading.value = false
  await nextTick()
  if (myLoadId === loadId) await renderCharts(summary.value)
}

async function refreshLongRangeTail() {
  if (!longRangeBasePayload || document.hidden) return
  const myLoadId = ++loadId
  const today = toLocalDateString()
  try {
    const [summaryResponse, mediaResponse, totalResponse] = await Promise.all([
      fetch(`${apiBase}/api/summary/today?date=${today}`, { signal: abortController.signal }),
      fetch(`${apiBase}/api/media/history?limit=100&from=${today}&to=${today}`, { signal: abortController.signal }),
      fetch(`${apiBase}/api/media/listen-total?from=${today}&to=${today}`, { signal: abortController.signal }),
    ])
    if (!summaryResponse.ok) throw new Error(`API ${summaryResponse.status}`)
    const tail = await summaryResponse.json()
    const combined = combineSummaryPayload(longRangeBasePayload, tail)
    await applySummaryPayload(combined, myLoadId)
    if (myLoadId !== loadId) return
    if (mediaResponse.ok) {
      const latest = await mediaResponse.json()
      const merged = new Map(media.value.map(item => [`${item.id || ''}:${item.startTime}`, item]))
      for (const item of latest) merged.set(`${item.id || ''}:${item.startTime}`, item)
      media.value = [...merged.values()].sort((a, b) => parseUtcTs(a.startTime) - parseUtcTs(b.startTime))
    }
    if (totalResponse.ok && longRangeListenBase != null) {
      const total = await totalResponse.json()
      totalListenSeconds.value = Math.max(0, longRangeListenBase + Number(total.totalSeconds || 0))
    }
    await renderPieChart(summary.value)
    await renderTagPieChart(tagDurations.value)
  } catch (error) {
    if (error.name !== 'AbortError') console.warn('Long-range tail refresh failed:', error)
  }
}

async function fetchSummary(myLoadId) {
  try {
    const [from, to] = periodRange()
    const url =
      period.value === 'today'
        ? `${apiBase}/api/summary/today?date=${pickDate.value}`
        : `${apiBase}/api/summary/range?from=${from}&to=${to}T23:59:59`
    const r = await fetch(url, { signal: abortController.signal })
    if (!r.ok) throw new Error(`API ${r.status}`)
    const res = await r.json()
    if (myLoadId !== loadId) return  // stale, newer call in flight
    if (period.value !== 'today' && to === toLocalDateString() && !Array.isArray(res)) {
      const tailResponse = await fetch(`${apiBase}/api/summary/today?date=${to}`, { signal: abortController.signal })
      if (tailResponse.ok) {
        const tail = await tailResponse.json()
        longRangeBasePayload = combineSummaryPayload(res, tail, -1)
      }
    } else {
      longRangeBasePayload = null
    }
    await applySummaryPayload(res, myLoadId)
  } catch (e) {
    if (myLoadId !== loadId) return
    console.error(e)
    error.value = t('dashboard.error.loadSummaryFailed', { message: e.message })
    loading.value = false
  }
}

async function fetchMedia(myLoadId) {
  try {
    const [fromDate, toDate] = periodRange()
    const limits = { today: 2000, week: 500, month: 2000, halfYear: 5000, year: 5000 }
    const limit = limits[period.value] || 50
    listenTotalLoading.value = true
    const needsTailBase = period.value !== 'today' && toDate === toLocalDateString()
    const [historyResponse, totalResponse, tailTotalResponse] = await Promise.all([
      fetch(`${apiBase}/api/media/history?limit=${limit}&from=${fromDate}&to=${toDate}`, { signal: abortController.signal }),
      fetch(`${apiBase}/api/media/listen-total?from=${fromDate}&to=${toDate}`, { signal: abortController.signal }),
      needsTailBase
        ? fetch(`${apiBase}/api/media/listen-total?from=${toDate}&to=${toDate}`, { signal: abortController.signal })
        : Promise.resolve(null),
    ])
    if (!historyResponse.ok) throw new Error(`API ${historyResponse.status}`)
    const data = await historyResponse.json()
    const total = totalResponse.ok ? await totalResponse.json() : { totalSeconds: 0 }
    if (myLoadId !== loadId) return  // stale
    media.value = data
    totalListenSeconds.value = Math.max(0, Number(total.totalSeconds) || 0)
    if (needsTailBase && tailTotalResponse?.ok) {
      const tailTotal = await tailTotalResponse.json()
      longRangeListenBase = Math.max(0,
        totalListenSeconds.value - Number(tailTotal.totalSeconds || 0))
    } else {
      longRangeListenBase = null
    }
  } catch (e) {
    console.error(e)
  } finally {
    if (myLoadId === loadId) listenTotalLoading.value = false
  }
}

async function getProcessIcon(processName, atTime) {
  const cacheKey = `${processName}|${atTime}`
  const cached = iconCache.get(cacheKey)
  if (cached) {
    // Refresh insertion order so the Map acts as a small LRU cache.
    iconCache.delete(cacheKey)
    iconCache.set(cacheKey, cached)
    return cached
  }
  let result = {
    icon: null, colorPrimary: '#6B7FD7', colorSecondary: '#DD7596',
    colorAccent: '#06D6A0', hasExtractedPalette: false,
  }
  try {
    const response = await fetch(
      `${apiBase}/api/icons/${encodeURIComponent(processName)}?at=${encodeURIComponent(atTime)}`,
      { signal: abortController.signal })
    if (response.ok) {
      const data = await response.json()
      result = {
        icon: data.iconData ? `data:image/png;base64,${data.iconData}` : null,
        colorPrimary: data.colorPrimary || result.colorPrimary,
        colorSecondary: data.colorSecondary || result.colorSecondary,
        colorAccent: data.colorAccent || result.colorAccent,
        hasExtractedPalette: Boolean(data.colorPrimary && data.colorSecondary && data.colorAccent),
      }
    }
  } catch (error) {
    if (error.name !== 'AbortError') console.warn(`Failed to fetch icon for ${processName}:`, error)
  }
  iconCache.set(cacheKey, result)
  if (iconCache.size > ICON_CACHE_LIMIT) iconCache.delete(iconCache.keys().next().value)
  return result
}

async function renderCharts(data) {
  if (!focusChartRef.value) return
  const renderId = ++focusRenderId

  const top = data.slice(0, 10)
  const labels = top.map(d => d.processName)
  const displayNames = top.map(d => d.displayName || d.processName)
  const focusData = top.map(d => +(d.totalSeconds / 60).toFixed(1))

  // Compute reference time for time-aware icon queries
  const atTime = dashboardIconAtTime()

  // Fetch icons and colors for all processes (cached by process+atTime)
  const iconDataList = await Promise.all(labels.map(processName => getProcessIcon(processName, atTime)))
  if (renderId !== focusRenderId || !focusChartRef.value) return
  await applyAutoColor(iconDataList.map(item => item.hasExtractedPalette ? item : {}))
  if (renderId !== focusRenderId || !focusChartRef.value) return
  const icons = iconDataList.map(d => d.icon)
  const colors = iconDataList.map(d => d.colorPrimary)
  const chartWidth = focusChartRef.value.clientWidth || window.innerWidth
  const plotWidth = Math.max(180, chartWidth - 80)
  const slotWidth = plotWidth / Math.max(1, top.length)
  const viewportCap = Math.max(15, Math.min(36, window.innerWidth * .02))
  const iconSize = Math.round(Math.max(14, Math.min(viewportCap, slotWidth * .48)))

  // Resolve CSS variables for ECharts (Canvas doesn't support CSS custom properties)
  const cs = getComputedStyle(document.documentElement)
  const textColor = cs.getPropertyValue('--text-color').trim()
  const borderColor = cs.getPropertyValue('--border-color').trim()
  const surface200 = cs.getPropertyValue('--surface-200').trim()
  const surfaceCard = cs.getPropertyValue('--surface-card').trim()
  const primaryColor = cs.getPropertyValue('--primary-color').trim()

  // Focus duration chart
  if (!focusChart) {
    focusChart = echarts.init(focusChartRef.value)
    focusChart.getZr().on('click', event => {
      const meta = focusChart?._columnActions
      if (!meta?.labels?.length) return
      const point = [event.offsetX, event.offsetY]
      if (!focusChart.containPixel({ gridIndex: 0 }, point)) return
      const centers = meta.labels.map(label => focusChart.convertToPixel({ xAxisIndex: 0 }, label))
      let nearest = 0
      for (let index = 1; index < centers.length; index++) {
        if (Math.abs(centers[index] - event.offsetX) < Math.abs(centers[nearest] - event.offsetX)) nearest = index
      }
      const rect = focusChartRef.value.getBoundingClientRect()
      openProcessActions(meta.labels[nearest], meta.colors[nearest], {
        clientX: event.event?.clientX ?? rect.left + event.offsetX,
        clientY: event.event?.clientY ?? rect.top + event.offsetY,
      }, 'focus', meta.displayNames[nearest])
    })
  }
  focusChart.setOption({
    grid: { left: 60, right: 20, top: 20, bottom: Math.max(68, iconSize + 42) },
    xAxis: {
      type: 'category',
      data: labels,
      axisLabel: {
        interval: 0,
        formatter: (value, index) => {
          // Return image placeholder that will be replaced by rich text
          return `{img${index}|}`
        },
        rich: icons.reduce((acc, icon, idx) => {
          acc[`img${idx}`] = {
            backgroundColor: {
              image: icon || 'data:image/svg+xml;base64,PHN2ZyB3aWR0aD0iMzIiIGhlaWdodD0iMzIiIHZpZXdCb3g9IjAgMCAzMiAzMiIgZmlsbD0ibm9uZSIgeG1sbnM9Imh0dHA6Ly93d3cudzMub3JnLzIwMDAvc3ZnIj4KICA8cmVjdCB3aWR0aD0iMzIiIGhlaWdodD0iMzIiIGZpbGw9IiNDQ0NDQ0MiLz4KICA8dGV4dCB4PSI1MCUiIHk9IjUwJSIgZG9taW5hbnQtYmFzZWxpbmU9Im1pZGRsZSIgdGV4dC1hbmNob3I9Im1pZGRsZSIgZmlsbD0id2hpdGUiIGZvbnQtc2l6ZT0iMTYiPj88L3RleHQ+Cjwvc3ZnPg=='
            },
            height: iconSize,
            width: iconSize,
            lineHeight: iconSize,
          }
          return acc
        }, {})
      },
      axisLine: { lineStyle: { color: borderColor, width: 2 } },
    },
    yAxis: {
      type: 'value',
      name: t('dashboard.chart.durationAxis'),
      nameTextStyle: { color: textColor, fontWeight: 600 },
      axisLabel: { color: textColor },
      axisLine: { lineStyle: { color: borderColor, width: 2 } },
      splitLine: { lineStyle: { type: 'dashed', color: surface200 } },
    },
    series: [
      {
        type: 'bar',
        data: focusData.map((val, idx) => ({
          value: val,
          itemStyle: {
            color: colors[idx],
            borderColor: borderColor,
            borderWidth: 2,
          },
        })),
        label: { show: false },
        emphasis: {
          itemStyle: {
            shadowBlur: 10,
            shadowColor: 'rgba(0,0,0,0.3)',
          },
        },
      },
    ],
    tooltip: {
      className: 'wta-chart-tooltip',
      position: smartTooltipPosition,
      trigger: 'axis',
      axisPointer: { type: 'shadow' },
      backgroundColor: surfaceCard,
      borderColor: primaryColor,
      borderWidth: 2,
      textStyle: { color: textColor, fontWeight: 600, fontFamily: 'Ubuntu Mono' },
      formatter: params => {
        const p = params[0]
        const totalSec = Math.round(p.value * 60)
        const displayName = escapeHtml(displayNames[p.dataIndex] || p.name)
        return `<strong style="font-family:'Ubuntu Mono'">${displayName}</strong><br/>${fmtShortDur(totalSec)}`
      },
    },
  })
  focusChart._columnActions = { labels, colors, displayNames }
}
async function renderPieChart(data) {
  if (!pieChartRef.value) return
  const renderId = ++pieRenderId

  const cs = getComputedStyle(document.documentElement)
  const textColor = cs.getPropertyValue('--text-color').trim()
  const successColor = cs.getPropertyValue('--success-color').trim()
  const surfaceCard = cs.getPropertyValue('--surface-card').trim()
  const primaryColor = cs.getPropertyValue('--primary-color').trim()
  const surface300 = cs.getPropertyValue('--surface-300').trim()

  const [fromDate, toDate] = periodRange()
  const atTime = dashboardIconAtTime()
  let focusData
  if (selectedOverviewProcess.value) {
    const icon = await getProcessIcon(selectedOverviewProcess.value.processName, atTime)
    if (renderId !== pieRenderId || !pieChartRef.value) return
    const palette = [icon.colorPrimary, icon.colorSecondary, icon.colorAccent]
    focusData = overviewTitleDurations.value.map((item, index) => ({
      id: `${selectedOverviewProcess.value.processName}:${item.title}`,
      value: Number(item.totalSeconds),
      name: item.title,
      itemStyle: { color: palette[index % palette.length], borderColor: textColor, borderWidth: 2 },
      _processName: selectedOverviewProcess.value.processName,
      _displayName: selectedOverviewProcess.value.displayName,
      _title: item.title,
      _type: 'overviewTitle',
    }))
  } else {
    const maxOffset = Math.max(0, Math.floor((data.length - 1) / 5) * 5)
    if (piePageOffset.value > maxOffset) piePageOffset.value = maxOffset
    const offset = piePageOffset.value
    const top5 = data.slice(offset, offset + 5)
    const otherSec = data.slice(offset + 5).reduce((s, i) => s + i.totalSeconds, 0)
    const icons = await Promise.all(top5.map(item => getProcessIcon(item.processName, atTime)))
    if (renderId !== pieRenderId || !pieChartRef.value) return
    focusData = top5.map((item, i) => ({
      id: item.processName,
      groupId: `other-${offset}`,
      value: item.totalSeconds,
      name: item.displayName || item.processName,
      itemStyle: { color: icons[i].colorPrimary, borderColor: textColor, borderWidth: 2 },
      _icon: icons[i].icon,
      _processName: item.processName,
      _type: 'focus',
    }))
    if (otherSec > 0.5) {
      focusData.push({
        id: 'other',
        groupId: `other-${offset + 5}`,
        value: otherSec,
        name: t('dashboard.pie.other'),
        itemStyle: { color: surface300, borderColor: textColor, borderWidth: 2 },
        _icon: null,
        _type: 'other',
      })
    }
  }

  const ringData = !selectedOverviewProcess.value && period.value === 'today'
    ? buildMediaRing(media.value, fromDate, toDate, successColor, period.value === 'today') : []

  if (!pieChart) {
    pieChart = echarts.init(pieChartRef.value)
    pieChart._firstRender = true
    pieChart.on('click', params => {
      if (params.data?._type === 'other') {
        if (pieDrillLocked || piePageOffset.value + 5 >= summary.value.length) return
        piePageOffset.value += 5
        lockPieDrill()
        renderPieChart(summary.value)
        return
      }
      if (params.data?._type === 'focus') {
        openOverviewProcessTitles(params.data)
      } else if (params.data?._type === 'overviewTitle') {
        openTitleActions(params.data, params.event?.event, 'overviewTitle')
      }
    })
  }

  const animDur = pieChart._firstRender ? 800 : 560
  pieChart._firstRender = false

  pieChart.setOption({
    animation: true,
    animationDuration: animDur,
    animationDurationUpdate: 560,
    animationEasing: 'cubicOut',
    animationEasingUpdate: 'cubicInOut',
    color: focusData.map(d => d.itemStyle.color),
    tooltip: {
      className: 'wta-chart-tooltip',
      position: smartTooltipPosition,
      trigger: 'item',
      backgroundColor: surfaceCard,
      borderColor: primaryColor,
      borderWidth: 2,
      textStyle: { color: textColor, fontFamily: 'Ubuntu Mono' },
      formatter: (params) => {
        if (!params.data || !params.data._type) return ''
        if (params.data._type === 'focus') {
          const d = params.data
          const iconHtml = d._icon
            ? `<img src="${d._icon}" style="width:16px;height:16px;vertical-align:middle;margin-right:4px;image-rendering:crisp-edges" />`
            : `<span style="display:inline-block;width:8px;height:8px;border-radius:50%;background:${d.itemStyle.color};margin-right:4px;vertical-align:middle"></span>`
          return `<div style="font-weight:600;margin-bottom:2px">${iconHtml}${escapeHtml(d.name)}</div><div style="font-size:0.95em">${fmtShortDur(d.value)}</div>`
        }
        if (params.data._type === 'overviewTitle')
          return `<div style="font-weight:600;margin-bottom:2px">${escapeHtml(params.data.name)}</div><div>${fmtShortDur(params.data.value)}</div>`
        if (params.data._type === 'other')
          return `${escapeHtml(t('dashboard.pie.other'))}<br/>${fmtShortDur(params.data.value)}`
        if (params.data._type === 'media') {
          const m = params.data._media
          return `<div style="font-weight:600;margin-bottom:2px;color:${successColor}">${escapeHtml(m.title)}</div>
                  <div style="font-size:0.95em">${fmtShortDur(params.data.value)}</div>
                  <div style="margin-top:2px;color:var(--surface-400)">${escapeHtml(m.artist || m.appName || '')}</div>`
        }
        return ''
      },
    },
    series: [
      {
        id: 'focus',
        name: 'focus',
        type: 'pie',
        universalTransition: { enabled: true, divideShape: 'split' },
        radius: ['28%', '52%'],
        center: ['50%', '50%'],
        data: focusData,
        label: { show: false },
        emphasis: {
          scaleSize: 8,
        },
      },
      ...(ringData.length ? [{
        id: 'media',
        name: 'media',
        type: 'pie',
        radius: ['58%', '72%'],
        center: ['50%', '50%'],
        data: ringData,
        label: { show: false },
        silent: false,
        emphasis: {
          scaleSize: 4,
        },
      }] : []),
    ],
  }, { replaceMerge: ['series'] })
}

async function renderTagPieChart(items) {
  if (!tagPieChartRef.value) return
  const renderId = ++tagPieRenderId
  const valid = items.filter(item => item.tag && Number(item.totalSeconds) > 0)
  const atTime = dashboardIconAtTime()
  const cs = getComputedStyle(document.documentElement)
  const textColor = cs.getPropertyValue('--text-color').trim()
  const surfaceCard = cs.getPropertyValue('--surface-card').trim()
  const borderColor = cs.getPropertyValue('--border-color').trim()
  const surface300 = cs.getPropertyValue('--surface-300').trim()
  let chartData

  if (selectedTagProcess.value) {
    const icon = await getProcessIcon(selectedTagProcess.value.processName, atTime)
    if (renderId !== tagPieRenderId || !tagPieChartRef.value) return
    const palette = [icon.colorPrimary, icon.colorSecondary, icon.colorAccent]
    chartData = tagTitleDurations.value.map((item, index) => ({
      id: `${selectedTagProcess.value.processName}:${item.title}`,
      name: item.title,
      value: Number(item.totalSeconds),
      itemStyle: { color: palette[index % palette.length], borderColor, borderWidth: 2 },
      _type: 'tagTitle',
      _processName: selectedTagProcess.value.processName,
      _displayName: selectedTagProcess.value.displayName,
      _title: item.title,
    }))
  } else if (selectedTag.value) {
    const processes = selectedTag.value.processes || []
    const maxOffset = Math.max(0, Math.floor((processes.length - 1) / 5) * 5)
    if (tagPiePageOffset.value > maxOffset) tagPiePageOffset.value = maxOffset
    const subset = processes.slice(tagPiePageOffset.value, tagPiePageOffset.value + 5)
    const icons = await Promise.all(subset.map(item => getProcessIcon(item.processName, atTime)))
    if (renderId !== tagPieRenderId || !tagPieChartRef.value) return
    chartData = subset.map((item, index) => ({
      id: item.processName,
      groupId: `tag-${selectedTag.value.tag}-${tagPiePageOffset.value}`,
      name: item.displayName || item.processName,
      value: Number(item.totalSeconds),
      itemStyle: { color: icons[index].colorPrimary, borderColor, borderWidth: 2 },
      _type: 'tagProcess',
      _processName: item.processName,
    }))
    const otherSeconds = processes.slice(tagPiePageOffset.value + 5)
      .reduce((sum, item) => sum + Number(item.totalSeconds || 0), 0)
    if (otherSeconds > .5) {
      chartData.push({
        id: 'other',
        groupId: `tag-${selectedTag.value.tag}-${tagPiePageOffset.value + 5}`,
        name: t('dashboard.pie.other'),
        value: otherSeconds,
        itemStyle: { color: surface300, borderColor, borderWidth: 2 },
        _type: 'tagOther',
      })
    }
  } else {
    const icons = await Promise.all(valid.map(item => getProcessIcon(item.dominantProcess, atTime)))
    if (renderId !== tagPieRenderId || !tagPieChartRef.value) return
    chartData = valid.map((item, index) => ({
      id: item.tag,
      name: item.tag,
      value: Number(item.totalSeconds),
      itemStyle: { color: icons[index].colorPrimary, borderColor, borderWidth: 2 },
      _type: 'tag',
      _tagItem: item,
    }))
  }

  if (!tagPieChart) {
    tagPieChart = echarts.init(tagPieChartRef.value)
    tagPieChart.on('click', params => {
      const item = params.data
      if (!item || tagPieDrillLocked) return
      if (item._type === 'tag') {
        selectedTag.value = item._tagItem
        tagPiePageOffset.value = 0
        lockTagPieDrill()
        renderTagPieChart(tagDurations.value)
      } else if (item._type === 'tagOther') {
        tagPiePageOffset.value += 5
        lockTagPieDrill()
        renderTagPieChart(tagDurations.value)
      } else if (item._type === 'tagProcess') {
        openTagProcessTitles(item)
      } else if (item._type === 'tagTitle') {
        openTitleActions(item, params.event?.event, 'tagTitle')
      }
    })
  }
  tagPieChart.setOption({
    animationDuration: 600,
    animationDurationUpdate: 450,
    animationEasing: 'cubicOut',
    animationEasingUpdate: 'cubicInOut',
    legend: {
      type: 'scroll',
      orient: 'vertical',
      right: 6,
      top: 'middle',
      height: '75%',
      textStyle: { color: textColor, fontFamily: 'Ubuntu' },
      formatter: name => name.length > 16 ? `${name.slice(0, 15)}…` : name,
    },
    tooltip: {
      className: 'wta-chart-tooltip',
      position: smartTooltipPosition,
      trigger: 'item',
      backgroundColor: surfaceCard,
      borderColor,
      borderWidth: 2,
      textStyle: { color: textColor, fontFamily: 'Ubuntu Mono' },
      formatter: params => `${escapeHtml(params.name)}<br/>${fmtShortDur(params.value)}`,
    },
    series: [{
      id: 'tags',
      name: t('dashboard.card.tagDurationPie'),
      type: 'pie',
      universalTransition: { enabled: true, divideShape: 'split' },
      radius: '62%',
      center: ['36%', '50%'],
      data: chartData,
      label: { show: false },
      emphasis: { scaleSize: 6 },
    }],
  }, { replaceMerge: ['series'] })
}

</script>

<style lang="scss" scoped src="../styles/views/dashboard.scss"></style>
