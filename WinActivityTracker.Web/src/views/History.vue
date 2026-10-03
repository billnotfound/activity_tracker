<!--
  History view — visual timeline with time wheel picker
  Combines timeline visualization with aggregated stats
-->
<template>
  <div class="history-page" :data-page-ready="initialRenderReady">
    <!-- Time range picker with wheels -->
  <TimeRangePicker
      ref="timeRangePickerRef"
      :start-date="startDate"
      :end-date="endDate"
      :earliest-date="earliestDate"
      :disabled="dataTooShort"
      @change="handleTimeChange"
      class="mb-3"
    />

    <div v-if="focusedProcess" class="focus-banner mb-3">
      <Focus :size="18" />
      <span>{{ t('history.focusedOn') }} <b>{{ focusedDisplayName }}</b></span>
      <button type="button" @click="clearProcessFocus"><X :size="16" /> {{ t('history.showAll') }}</button>
    </div>

    <!-- Error -->
    <div v-if="error" class="error-banner mb-3">
      {{ error }}
      <button class="close-btn" @click="error = ''" :aria-label="t('common.close')"><X :size="16" /></button>
    </div>

    <!-- Visual timeline -->
    <MemphisCard class="timeline-card mb-3" :class="{ 'context-hover-locked': contextMenuVisible }">
      <h3 class="card-title">
        {{ t('history.card.visualTimeline') }}
        <small v-if="showActionHint" class="first-use-hint">{{ t('processActions.chartHint') }}</small>
      </h3>
      <!-- Easter egg: invalid time range -->
      <div v-if="!isTimeValid" class="easter-egg-chart">
        <div class="easter-egg-chart-text">{{ timeEasterEgg }}</div>
      </div>
      <!-- Data too short (< 2 min) -->
      <div v-else-if="dataTooShort" class="easter-egg-chart">
        <div class="easter-egg-chart-text">{{ t('history.tooShort') }}</div>
      </div>
      <template v-else>
        <MemphisSkeleton v-if="loading && !hasRenderedTimeline" :lines="6" />
        <div v-else>
          <div
            class="timeline-chart-wrap"
            :class="[rangeMotionClass, { 'is-dragging': timelineDragging }]"
            :style="{ '--range-motion-origin': rangeMotionOrigin, '--timeline-plot-left': focusedProcess || timelineViewMode !== 'process' ? '220px' : '20px' }"
            @wheel="onTimelineWheel"
            @pointerdown="onTimelinePointerDown"
            @pointermove="onTimelinePointerMove"
            @pointerup="onTimelinePointerUp"
            @pointercancel="onTimelinePointerUp"
            @auxclick.prevent
          >
            <div ref="timelineChartRef" class="timeline-chart"></div>
            <!-- Hover overlay: a canvas layer painted with the background color
                 over every bar EXCEPT the hovered process's ones, which stay
                 full-color. No chart re-render — the ECharts canvas never
                 changes, so there is no flicker.
                 NOTE: must stay a SIBLING of the chart container —
                 echarts.init() wipes the container's existing children. -->
            <canvas ref="hoverDimmerRef" class="hover-dimmer"
                    :class="{ visible: !!hoveredProcess }"></canvas>
            <div v-if="loading" class="timeline-loading" aria-hidden="true"></div>
            <div
              v-if="timeSelection.active"
              ref="timeSelectionLayerRef"
              class="time-selection-layer"
              :class="{ 'repair-animating': timeSelection.animating }"
              @pointerdown.self="onSelectionPointerDown($event, 'create')"
              @pointermove="onSelectionPointerMove"
              @pointerup="onSelectionPointerUp"
              @pointercancel="onSelectionPointerUp"
            >
              <div class="selection-mask mask-left" :style="selectionLeftMaskStyle"></div>
              <div class="selection-mask mask-right" :style="selectionRightMaskStyle"></div>
              <div class="target-range" :style="targetRangeStyle" @pointerdown.stop="onSelectionPointerDown($event, 'target')">
                <span>{{ t('history.time.target') }}</span>
              </div>
              <div
                v-if="timeSelection.animationPhase"
                class="repair-preview-slice"
                :class="timeSelection.animationPhase"
                :style="repairPreviewStyle"
                aria-hidden="true"
              >
                <canvas ref="repairPreviewCanvasRef"></canvas>
              </div>
              <div class="selected-range" :style="selectedRangeStyle" @pointerdown.stop="onSelectionPointerDown($event, 'move')">
                <button class="selection-handle left" type="button" :aria-label="t('history.time.dragStart')" @pointerdown.stop="onSelectionPointerDown($event, 'start')"><ChevronLeft :size="24" /></button>
                <span>{{ t('history.time.selected') }}</span>
                <button class="selection-handle right" type="button" :aria-label="t('history.time.dragEnd')" @pointerdown.stop="onSelectionPointerDown($event, 'end')"><ChevronRight :size="24" /></button>
              </div>
            </div>
          </div>
          <div class="timeline-legend">
            <span class="legend-item">
              <span class="legend-box focus"></span>
              {{ t('history.legend.focus') }}
            </span>
            <span class="legend-item">
              <span class="legend-box visible"></span>
              {{ t('history.legend.visible') }}
            </span>
            <span class="legend-item">
              <span class="legend-box idle"></span>
              {{ t('history.legend.idle') }}
            </span>
            <span class="legend-item">
              <span class="legend-box offline"></span>
              {{ t('history.legend.offline') }}
            </span>
          </div>

          <div class="timeline-view-controls">
            <select v-model="timelineViewMode" :aria-label="t('history.view.mode')">
              <option value="process">{{ t('history.view.process') }}</option>
              <option value="tag">{{ t('history.view.tag') }}</option>
              <option value="processFilter">{{ t('history.view.processFilter') }}</option>
              <option value="titleFilter">{{ t('history.view.titleFilter') }}</option>
            </select>
            <input
              v-if="timelineViewMode === 'processFilter' || timelineViewMode === 'titleFilter'"
              v-model="timelineFilter"
              type="text"
              class="timeline-filter-input"
              :placeholder="timelineViewMode === 'processFilter' ? t('history.view.processPlaceholder') : t('history.view.titlePlaceholder')"
            />
          </div>

          <section v-if="timeSelection.active" class="time-correction-panel">
            <header>
              <div>
                <span>{{ t('history.time.kicker') }}</span>
                <h4>{{ t('history.time.title') }}</h4>
              </div>
              <button type="button" @click="cancelTimeSelection"><X :size="17" /> {{ t('common.cancel') }}</button>
            </header>
            <p>{{ t('history.time.help') }}</p>
            <div class="time-correction-summary">
              <div><span>{{ t('history.time.from') }}</span><b>{{ selectionRangeLabel }}</b></div>
              <div><span>{{ t('history.time.to') }}</span><b>{{ targetRangeLabel }}</b></div>
              <div class="offset-summary"><span>{{ t('history.time.offset') }}</span><b>{{ offsetDurationLabel }}</b></div>
            </div>
            <div v-if="targetHasRecords" class="time-target-warning">{{ t('history.time.targetOccupied') }}</div>
            <div v-else-if="!targetInRange" class="time-target-warning">{{ t('history.time.targetOutside') }}</div>
            <div class="shift-controls">
              <label>
                <span>{{ t('history.time.shiftMinutes') }}</span>
                <input v-model.number="timeSelection.shiftMinutes" type="number" step="1" />
              </label>
              <button v-for="amount in [-60, -5, 5, 60]" :key="amount" type="button" @click="timeSelection.shiftMinutes += amount">
                {{ amount > 0 ? '+' : '' }}{{ amount }}m
              </button>
            </div>
            <div v-if="timeSelection.preview" class="time-preview-result">
              {{ t('timeAnomaly.applyPreview', { total: timeSelection.preview.total, tables: timeSelection.preview.tables.join(listSeparator) }) }}
            </div>
            <button v-if="!timeSelection.preview" type="button" class="preview-button" :disabled="!timeSelection.shiftMinutes || !targetInRange || timeSelection.animating" @click="previewManualTime">
              <ScanSearch :size="17" /> {{ t('history.time.preview') }}
            </button>
            <button v-else type="button" class="apply-time-button" @click="applyManualTime">
              <Check :size="17" /> {{ t('history.time.apply') }}
            </button>
          </section>
        </div>
      </template>
    </MemphisCard>

    <ProcessActionDrawer
      v-model:visible="actionDrawerVisible"
      :process="selectedProcess"
      :range-start="startDate"
      :range-end="endDate"
      :process-color="selectedProcessColor"
      :initial-view="drawerView"
      :initial-tag-names="drawerTagNames"
      tag-scope="process-title"
      allow-isolate
      return-to-popup
      @isolate="isolateProcess"
      @select-time="beginTimeSelection"
      @changed="loadData"
      @return-to-menu="reopenContextMenu"
    />
    <ProcessContextMenu
      :visible="contextMenuVisible"
      @update:visible="setContextMenuVisible"
      page="history"
      :process-name="selectedProcess.processName || ''"
      :window-title="selectedProcess.windowTitle || ''"
      :raw-window-title="selectedProcess.rawWindowTitle || selectedProcess.windowTitle || ''"
      :x="contextPoint.x"
      :y="contextPoint.y"
      @choose="chooseContextAction"
      @create-tag="openNewTagDrawer"
      @tags-saved="handleTagsSaved"
    />

  </div>
</template>

<script setup>
import { computed, reactive, ref, inject, onMounted, onUnmounted, nextTick, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { toLocalTime, parseUtcTs, toLocalDateString, toLocalDatetimeString, fmtShortDur } from '../utils/time.js'
import { mergeByProcessName } from '../utils/process.js'
import { inferIdleGaps, mergeTimePeriods, subtractTimePeriods } from '../utils/intervals.js'
import { mergePixelIntervals, withAlpha } from '../utils/chart.js'
import { escapeHtml } from '../utils/html.js'
import { useI18n } from '../i18n/index.js'
import { useTheme } from '../composables/useTheme.js'
import { echarts } from '../utils/echartsInit.js'
import MemphisCard from '../components/MemphisCard.vue'
import MemphisSkeleton from '../components/MemphisSkeleton.vue'
import TimeRangePicker from '../components/TimeRangePicker.vue'
import ProcessActionDrawer from '../components/ProcessActionDrawer.vue'
import ProcessContextMenu from '../components/ProcessContextMenu.vue'
import { Check, ChevronLeft, ChevronRight, Focus, ScanSearch, X } from '@lucide/vue'

const apiBase = inject('apiBase')
const { t, locale } = useI18n()
const { isDark } = useTheme()
const route = useRoute()
const router = useRouter()

// Aborted on unmount so in-flight fetches don't keep running after the
// view is gone. Per-instance (re-created each time the view is mounted).
const lifetimeController = new AbortController()
let activeLoadController = null

const THREE_HOURS_MS = 3 * 60 * 60 * 1000
const TWO_MIN_MS = 2 * 60 * 1000
const PREFETCH_MAX_MS = 3 * 24 * 60 * 60 * 1000
const DEFAULT_CHART_HEIGHT = 440
const DRAG_PREVIEW_INTERVAL_MS = 32

const startDate = ref(new Date(Date.now() - THREE_HOURS_MS))
const endDate = ref(new Date())
const earliestDate = ref(null)

const data = ref([])
const timeline = ref([])
const timelineMeta = ref({ sampled: false, truncated: false })
const windowSessions = ref([])
const systemEvents = ref([])
const mediaSessions = ref([])
const mergeSameProcess = ref(true)
const totalSleepSeconds = ref(0)
const loading = ref(true)
const initialRenderReady = ref(false)
const error = ref('')
const isTimeValid = ref(true)
const timeEasterEgg = ref('')
const dataTooShort = ref(false)
const mediaMode = computed(() => route.query.media === '1')
const hasRenderedTimeline = ref(false)

const timeRangePickerRef = ref(null)
const timelineChartRef = ref(null)
const timeSelectionLayerRef = ref(null)
const repairPreviewCanvasRef = ref(null)
let timelineChart = null
let renderedRangeMin = 0
let renderedRangeMax = 0

// The existing canvas stays visible while a range request is in flight. A
// short, directional retreat makes the changed edge immediately legible.
const rangeMotionClass = ref('')
const rangeMotionOrigin = ref('50% 50%')
const timelineDragging = ref(false)
let rangeMotionTimer = null
let rangeLoadTimer = null
let dragState = null
let dragPreviewFrame = null
let pendingDragPreview = null
let lastDragPreviewAt = 0

const actionDrawerVisible = ref(false)
const contextMenuVisible = ref(false)
const drawerView = ref('normalize')
const drawerTagNames = ref([])
const contextPoint = ref({ x: 0, y: 0 })
let contextClosedAt = 0
const selectedProcess = ref({})
const selectedProcessColor = ref('var(--primary-color)')
const focusedProcess = ref('')
const focusedDisplayName = computed(() => {
  const match = [...timeline.value, ...windowSessions.value].find(item =>
    item.processName?.toLowerCase() === focusedProcess.value.toLowerCase()
    && item.displayName && item.displayName !== item.processName)
  return match?.displayName || focusedProcess.value
})
const processRelations = ref({ instances: [], parents: [], children: [] })
const focusedProcessEntries = ref([])
const relationProcessSessions = ref([])
const filteredProcessSessions = ref([])
const tagRules = ref([])
const timelineViewMode = ref('process')
const timelineFilter = ref('')
const showActionHint = ref(localStorage.getItem('wta-process-actions-seen') !== '1')
const listSeparator = computed(() => locale.value === 'zh-CN' ? '、' : ', ')
const timeSelection = reactive({
  active: false,
  start: 0,
  end: 0,
  shiftMinutes: 60,
  preview: null,
  applying: false,
  animating: false,
  animationPhase: '',
})
let selectionDrag = null
let filterLoadTimer = null
let repairAnimationStartTimer = null
let repairAnimationEndTimer = null
let repairCaptureFrame = null

const percentInRange = value => {
  const min = startDate.value.getTime()
  const span = Math.max(1, endDate.value.getTime() - min)
  return Math.max(0, Math.min(100, ((value - min) / span) * 100))
}
const selectionLeft = computed(() => percentInRange(timeSelection.start))
const selectionRight = computed(() => percentInRange(timeSelection.end))
const selectionLeftMaskStyle = computed(() => ({ width: `${selectionLeft.value}%` }))
const selectionRightMaskStyle = computed(() => ({ left: `${selectionRight.value}%`, width: `${100 - selectionRight.value}%` }))
const selectedRangeStyle = computed(() => ({ left: `${selectionLeft.value}%`, width: `${Math.max(.5, selectionRight.value - selectionLeft.value)}%` }))
const targetStart = computed(() => timeSelection.start + timeSelection.shiftMinutes * 60000)
const targetEnd = computed(() => timeSelection.end + timeSelection.shiftMinutes * 60000)
const targetLeft = computed(() => percentInRange(targetStart.value))
const targetRight = computed(() => percentInRange(targetEnd.value))
const targetRangeStyle = computed(() => {
  return { left: `${targetLeft.value}%`, width: `${Math.max(.5, targetRight.value - targetLeft.value)}%` }
})
const repairPreviewStyle = computed(() => ({
  '--repair-source-left': `${selectionLeft.value}%`,
  '--repair-target-left': `${targetLeft.value}%`,
  width: `${Math.max(.5, selectionRight.value - selectionLeft.value)}%`,
}))
const selectionRangeLabel = computed(() => timeSelection.active
  ? `${new Date(timeSelection.start).toLocaleString()} — ${new Date(timeSelection.end).toLocaleString()}` : '')
const targetRangeLabel = computed(() => {
  return `${new Date(targetStart.value).toLocaleString()} — ${new Date(targetEnd.value).toLocaleString()}`
})
const offsetDurationLabel = computed(() => {
  const seconds = Math.round(Math.abs(Number(timeSelection.shiftMinutes) || 0) * 60)
  const sign = Number(timeSelection.shiftMinutes) > 0 ? '+' : Number(timeSelection.shiftMinutes) < 0 ? '−' : ''
  return `${sign}${fmtShortDur(seconds)}`
})
const targetInRange = computed(() => targetStart.value >= startDate.value.getTime() && targetEnd.value <= endDate.value.getTime())
const targetHasRecords = computed(() => {
  if (!timeSelection.active || !targetInRange.value) return false
  const start = targetStart.value
  const end = targetEnd.value
  const overlapsTarget = item => {
    const itemStart = parseUtcTs(item.timestamp || item.openTime || item.startTime)?.getTime()
    if (itemStart == null) return false
    const explicitEnd = parseUtcTs(item.closeTime || item.endTime)?.getTime()
    const itemEnd = explicitEnd ?? itemStart + Math.max(0, Number(item.durationSeconds || 0)) * 1000
    return itemEnd > start && itemStart < end
  }
  return timeline.value.some(overlapsTarget) || windowSessions.value.some(overlapsTarget)
})

// Hover-dim state: while hovering a process, a canvas layer is painted with
// the background color over every bar EXCEPT that process's bars, which stay
// full-color. The ECharts canvas never re-renders, so there is no flicker.
const hoveredProcess = ref(null)
let hoveredTimelineData = null
const hoverDimmerRef = ref(null)
// Bars of the latest render grouped by chart row. Geometry is calculated on
// first hover with one conversion per row and direct time→pixel arithmetic.
let allBarsPx = [] // rows: [{ centerY, barH, bars: [{ proc, x, w, focused }] }]
let focusedWindowsData = []
let backgroundWindowsData = []
let lastRowCount = 1
// Focused bar height as actually rendered (api.size * 0.6), recorded by
// renderBar. Dimmer rects must match it exactly — deriving row height from
// convertToPixel breaks with few rows (single-row category bands are taller
// than the axis-label-free estimate).
let lastFocusedBarH = 0
// Chart background color (--surface-card), used to paint the dim layer.
let surfaceCard = ''
let barGeometryDirty = true
let hoverSuppressedUntilMove = false

// Counter to cancel stale loadData calls — each call increments the ID;
// only the call whose ID still matches when it reaches renderTimeline() proceeds.
let loadId = 0

// Cache process colors keyed by `${processName}|${atTime}` so re-renders
// (e.g. theme toggle, resize) don't re-fetch /api/icons for the same range.
const colorCache = new Map()
const COLOR_CACHE_LIMIT = 500
function getCachedColor(key) {
  if (!colorCache.has(key)) return null
  const color = colorCache.get(key)
  colorCache.delete(key)
  colorCache.set(key, color)
  return color
}
function setCachedColor(key, color) {
  colorCache.set(key, color)
  if (colorCache.size > COLOR_CACHE_LIMIT) colorCache.delete(colorCache.keys().next().value)
}
// Remembers the last daily row used by each process across range reloads, so
// dragging a day-sized view does not reshuffle unchanged applications.
const stableDailyProcessRows = new Map()

// Handle time change from picker
function handleTimeChange({ start, end, valid, easterEgg }) {
  if (dataTooShort.value) return
  const previousStart = startDate.value.getTime()
  const previousEnd = endDate.value.getTime()
  startDate.value = start
  endDate.value = end
  isTimeValid.value = valid
  timeEasterEgg.value = easterEgg || ''
  if (valid) {
    // TimeRangePicker normalizes the initial seconds to minute precision while
    // the parent is mounting. The onMounted load below already uses that range.
    if (loading.value && !hasRenderedTimeline.value) return
    previewRange(start.getTime(), end.getTime())
    playRangeMotion(previousStart, previousEnd, start.getTime(), end.getTime())
    scheduleLoadData()
  }
}

onMounted(async () => {
  await loadTagRules()
  try {
    const r = await fetch(`${apiBase}/api/settings`, { signal: lifetimeController.signal })
    if (r.ok) {
      const s = await r.json()
      mergeSameProcess.value = s.mergeSameProcessSwitches ?? true
    }
  } catch (e) {
    console.error('Failed to load settings:', e)
  }

  // Fetch oldest record to constrain time picker (async, non-blocking)
  try {
    const r = await fetch(`${apiBase}/api/db/stats`, { signal: lifetimeController.signal })
    if (r.ok) {
      const stats = await r.json()
      if (stats.oldestRecord) {
        const oldest = new Date(stats.oldestRecord.endsWith('Z') ? stats.oldestRecord : stats.oldestRecord + 'Z')
        earliestDate.value = oldest
        if ((Date.now() - oldest.getTime()) < THREE_HOURS_MS) {
          startDate.value = oldest
        }
        // Check if total data span is less than 2 minutes
        dataTooShort.value = (Date.now() - oldest.getTime()) < TWO_MIN_MS
      } else {
        dataTooShort.value = true
      }
    }
  } catch {
    // Fall back to 3h default on failure
  }

  const queryStart = route.query.from ? new Date(String(route.query.from)) : null
  const queryEnd = route.query.to ? new Date(String(route.query.to)) : null
  if (queryStart && queryEnd && !Number.isNaN(queryStart.getTime()) && !Number.isNaN(queryEnd.getTime()) && queryEnd > queryStart) {
    startDate.value = queryStart
    endDate.value = queryEnd
    timeRangePickerRef.value?.setRange(queryStart, queryEnd)
  }
  if (route.query.process && route.query.isolate === '1') {
    await prepareProcessFocus(String(route.query.process))
  }

  await loadData()
  if (route.query.timeSelect === '1') {
    let requestedAnomaly = null
    if (route.query.anomalyId) {
      try {
        const response = await fetch(`${apiBase}/api/time-anomalies/${encodeURIComponent(route.query.anomalyId)}`, { signal: lifetimeController.signal })
        if (response.ok) {
          requestedAnomaly = await response.json()
        }
      } catch (error) {
        if (error.name !== 'AbortError') console.warn('Failed to load requested time anomaly:', error)
      }
    }
    beginTimeSelection({
      processName: route.query.process ? String(route.query.process) : '',
      timeAnomaly: requestedAnomaly,
    })
    router.replace({ query: { ...route.query, timeSelect: undefined, anomalyId: undefined } })
  }

  // Add window resize listener for chart responsiveness
  window.addEventListener('resize', handleResize)
})

// Watch for theme changes and re-render timeline
watch(isDark, () => {
  if (timeline.value && timeline.value.length > 0) {
    renderTimeline()
  }
})

// The picker can flip to an invalid range (easter egg replaces the chart div)
// while a load is in flight; the load's renderTimeline then bails on the
// missing div and the chart stays dead. Re-render as soon as the range is
// valid again.
watch(isTimeValid, (valid) => {
  if (!valid) {
    disposeTimelineChart()
    return
  }
  if (valid && timeline.value && timeline.value.length > 0) {
    renderTimeline()
  }
})

onUnmounted(() => {
  window.removeEventListener('resize', handleResize)
  lifetimeController.abort()
  activeLoadController?.abort()
  if (rangeLoadTimer) clearTimeout(rangeLoadTimer)
  if (rangeMotionTimer) clearTimeout(rangeMotionTimer)
  if (dragPreviewFrame) cancelAnimationFrame(dragPreviewFrame)
  if (filterLoadTimer) clearTimeout(filterLoadTimer)
  clearRepairAnimation()
  if (repairCaptureFrame) cancelAnimationFrame(repairCaptureFrame)
  disposeTimelineChart()
  // Cancel pending loads
  loadId++
})

watch(timelineViewMode, mode => {
  if (mode !== 'process') {
    focusedProcess.value = ''
    focusedProcessEntries.value = []
    relationProcessSessions.value = []
  }
  scheduleFilterLoad()
})

watch(timelineFilter, () => {
  if (timelineViewMode.value === 'processFilter' || timelineViewMode.value === 'titleFilter') scheduleFilterLoad()
})

function scheduleFilterLoad() {
  if (filterLoadTimer) clearTimeout(filterLoadTimer)
  filterLoadTimer = setTimeout(() => {
    filterLoadTimer = null
    loadData()
  }, 260)
}

async function loadTagRules() {
  try {
    const response = await fetch(`${apiBase}/api/tags/status`, { signal: lifetimeController.signal })
    if (!response.ok) return
    const status = await response.json()
    tagRules.value = status.tags?.rules || []
  } catch (error) {
    if (error.name !== 'AbortError') console.warn('Failed to load tag rules:', error)
  }
}

async function handleTagsSaved() {
  await loadTagRules()
  await loadData()
}

// Debounced resize handler
let resizeTimer = null
function handleResize() {
  if (resizeTimer) clearTimeout(resizeTimer)
  resizeTimer = setTimeout(() => {
    if (timelineChart && !timelineChart.isDisposed()) {
      timelineChart.resize()
      // Pixel positions shifted with the new size. Rebuild only if/when the
      // user actually hovers a bar.
      barGeometryDirty = true
      if (hoveredProcess.value) {
        buildAllBarsPx()
        paintDimmer()
      }
      scheduleRepairCapture()
    }
  }, 200)
}

function disposeTimelineChart() {
  if (timelineChart && !timelineChart.isDisposed()) {
    try {
      timelineChart.dispose()
    } catch (e) {
      console.warn('Error disposing timeline chart:', e)
    }
  }
  timelineChart = null
  allBarsPx = []
  barGeometryDirty = true
}

function openProcessActions(data, point = null) {
  if (contextMenuVisible.value) {
    setContextMenuVisible(false)
    return
  }
  if (performance.now() - contextClosedAt < 320) return
  const processName = data?.processName || data?._bgSession?.processName
  if (!processName) return
  selectedProcess.value = {
    processName,
    displayName: data.displayName || data._bgSession?.displayName || data._processSession?.displayName || processName,
    windowTitle: data.windowTitle || data._bgSession?.windowTitle || '',
    rawWindowTitle: data.rawWindowTitle || data._bgSession?.rawWindowTitle || '',
    timestamp: data.timestamp || data._bgSession?.openTime || data._processSession?.startTime || null,
    durationSeconds: data.durationSeconds || 0,
  }
  selectedProcessColor.value = data.itemColor || data.itemStyle?.color || 'var(--primary-color)'
  showActionHint.value = false
  localStorage.setItem('wta-process-actions-seen', '1')
  contextPoint.value = point || { x: window.innerWidth / 2, y: window.innerHeight / 2 }
  contextMenuVisible.value = true
}

function setContextMenuVisible(value) {
  if (!value && contextMenuVisible.value) contextClosedAt = performance.now()
  contextMenuVisible.value = value
}

function chooseContextAction(action) {
  if (action === 'isolate') {
    isolateProcess(selectedProcess.value.processName)
    return
  }
  drawerView.value = action
  actionDrawerVisible.value = true
}

function openNewTagDrawer(selectedTags) {
  drawerTagNames.value = [...selectedTags, '']
  drawerView.value = 'tag'
  actionDrawerVisible.value = true
}

function reopenContextMenu() {
  requestAnimationFrame(() => { contextMenuVisible.value = true })
}

async function loadProcessRelations(processName) {
  processRelations.value = { instances: [], parents: [], children: [] }
  try {
    const r = await fetch(`${apiBase}/api/processes/relations?process=${encodeURIComponent(processName)}`)
    if (r.ok) processRelations.value = await r.json()
  } catch { /* relationships are best-effort for currently running processes */ }
  return processRelations.value
}

async function prepareProcessFocus(processName) {
  const relations = await loadProcessRelations(processName)
  const exact = processName.toLowerCase()
  const targetInstances = relations.instances?.length
    ? relations.instances.map(instance => ({ ...instance, role: 'target' }))
    : [{ processId: null, name: processName, role: 'target' }]
  // Same-name parents are another instance of the selected executable, not a
  // useful separate "parent" label. They remain counted with the target row.
  const parents = (relations.parents || (relations.parent ? [relations.parent] : []))
    .filter(parent => parent.name.toLowerCase() !== exact)
    .map(parent => ({ ...parent, role: 'parent' }))
  const children = (relations.children || []).map(child => ({ ...child, role: 'child' }))
  const seen = new Set()
  focusedProcessEntries.value = [...targetInstances, ...parents, ...children].filter(entry => {
    const key = entry.processId != null ? `pid:${entry.processId}` : `${entry.role}:${entry.name.toLowerCase()}`
    if (seen.has(key)) return false
    seen.add(key)
    return true
  })
  focusedProcess.value = processName
}

async function isolateProcess(processName) {
  if (!processName) return
  timelineViewMode.value = 'process'
  const old = focusedProcess.value
  await prepareProcessFocus(processName)
  const keepNames = new Set(focusedProcessEntries.value.map(entry => entry.name.toLowerCase()))
  const oldStartMs = startDate.value.getTime()
  const oldEndMs = endDate.value.getTime()
  const nextSpan = Math.max(TWO_MIN_MS, (oldEndMs - oldStartMs) * .5)
  const clickedAt = selectedProcess.value.timestamp
    ? parseUtcTs(selectedProcess.value.timestamp).getTime()
    : (oldStartMs + oldEndMs) / 2
  const { earliest, latest } = getSelectableBounds()
  let nextStartMs = clickedAt - nextSpan / 2
  let nextEndMs = clickedAt + nextSpan / 2
  if (nextStartMs < earliest) { nextStartMs = earliest; nextEndMs = earliest + nextSpan }
  if (nextEndMs > latest) { nextEndMs = latest; nextStartMs = latest - nextSpan }
  const nextStart = new Date(nextStartMs)
  const nextEnd = new Date(nextEndMs)

  if (timelineChart && !timelineChart.isDisposed()) {
    timelineChart.setOption({
      series: [{}, {}, {
        data: [...backgroundWindowsData, ...focusedWindowsData].map(item => ({
          ...item,
          itemStyle: { ...item.itemStyle, opacity: keepNames.has((item.processName || item._bgSession?.processName || '').toLowerCase()) ? 1 : 0.04 },
        })),
      }],
    }, { lazyUpdate: false })
  }
  await new Promise(resolve => setTimeout(resolve, 220))
  if (old !== processName || endDate.value - startDate.value !== nextSpan) {
    startDate.value = nextStart
    endDate.value = nextEnd
    timeRangePickerRef.value?.setRange(nextStart, nextEnd)
  }
  await loadData()
}

async function clearProcessFocus() {
  focusedProcess.value = ''
  focusedProcessEntries.value = []
  relationProcessSessions.value = []
  processRelations.value = { instances: [], parents: [], children: [] }
  await loadData()
}

function powerBoundaries() {
  const boundaries = [startDate.value.getTime(), endDate.value.getTime()]
  for (const event of systemEvents.value) {
    if (!['Sleep', 'Shutdown'].includes(event.eventType) || event.durationSeconds <= 0) continue
    const start = parseUtcTs(event.timestamp).getTime()
    boundaries.push(start, start + event.durationSeconds * 1000)
  }
  return [...new Set(boundaries)].sort((a, b) => a - b)
}

function clearRepairAnimation() {
  clearTimeout(repairAnimationStartTimer)
  clearTimeout(repairAnimationEndTimer)
  repairAnimationStartTimer = null
  repairAnimationEndTimer = null
}

function scheduleRepairCapture() {
  if (!timeSelection.active) return
  if (repairCaptureFrame) cancelAnimationFrame(repairCaptureFrame)
  repairCaptureFrame = requestAnimationFrame(() => {
    repairCaptureFrame = null
    captureRepairPreview()
  })
}

function captureRepairPreview() {
  const output = repairPreviewCanvasRef.value
  const layer = timeSelectionLayerRef.value
  const chartHost = timelineChartRef.value
  if (!output || !layer || !chartHost) return
  const sourceCanvases = [...chartHost.querySelectorAll('canvas')]
  if (!sourceCanvases.length) return
  const layerRect = layer.getBoundingClientRect()
  const sliceX = layerRect.left + layerRect.width * selectionLeft.value / 100
  const sliceWidth = Math.max(2, layerRect.width * (selectionRight.value - selectionLeft.value) / 100)
  const dpr = window.devicePixelRatio || 1
  output.width = Math.max(1, Math.round(sliceWidth * dpr))
  output.height = Math.max(1, Math.round(layerRect.height * dpr))
  const context = output.getContext('2d')
  context.clearRect(0, 0, output.width, output.height)
  for (const source of sourceCanvases) {
    const sourceRect = source.getBoundingClientRect()
    if (!sourceRect.width || !sourceRect.height) continue
    const scaleX = source.width / sourceRect.width
    const scaleY = source.height / sourceRect.height
    const sx = (sliceX - sourceRect.left) * scaleX
    const sy = (layerRect.top - sourceRect.top) * scaleY
    const sw = sliceWidth * scaleX
    const sh = layerRect.height * scaleY
    try {
      context.drawImage(source, sx, sy, sw, sh, 0, 0, output.width, output.height)
    } catch {
      // A transient zero-sized renderer during resize should not cancel repair selection.
    }
  }
}

async function startRepairAnimation(animate) {
  clearRepairAnimation()
  timeSelection.animating = Boolean(animate) && !window.matchMedia('(prefers-reduced-motion: reduce)').matches
  timeSelection.animationPhase = 'source'
  await nextTick()
  scheduleRepairCapture()
  if (!timeSelection.animating) {
    timeSelection.animationPhase = 'settled'
    return
  }
  repairAnimationStartTimer = setTimeout(() => {
    timeSelection.animationPhase = 'moving'
  }, 700)
  repairAnimationEndTimer = setTimeout(() => {
    timeSelection.animationPhase = 'settled'
    timeSelection.animating = false
  }, 2950)
}

async function beginTimeSelection(item = {}) {
  const rangeStart = startDate.value.getTime()
  const rangeEnd = endDate.value.getTime()
  const anomaly = item.timeAnomaly || null
  const anomalyPoints = anomaly
    ? [anomaly.fromWall, anomaly.toWall, anomaly.oldTime, anomaly.newTime]
        .map(value => parseUtcTs(value)?.getTime())
        .filter(value => Number.isFinite(value))
    : []
  const anomalyStart = anomalyPoints.length ? Math.min(...anomalyPoints) : null
  const anomalyEnd = anomalyPoints.length > 1 ? Math.max(...anomalyPoints) : null
  const useAnomaly = anomalyStart != null && anomalyStart >= rangeStart && anomalyStart < rangeEnd
  const itemStart = useAnomaly
    ? anomalyStart
    : item.timestamp ? parseUtcTs(item.timestamp).getTime() : (rangeStart + rangeEnd) / 2
  const itemEnd = item.durationSeconds ? itemStart + item.durationSeconds * 1000 : itemStart
  const boundaries = powerBoundaries()
  const fallbackSpan = Math.min(60 * 60 * 1000, rangeEnd - rangeStart)
  const left = useAnomaly
    ? anomalyStart
    : [...boundaries].reverse().find(value => value <= itemStart) ?? rangeStart
  const right = useAnomaly && anomalyEnd != null && anomalyEnd > left
    ? anomalyEnd
    : boundaries.find(value => value >= itemEnd && value > left) ?? rangeEnd
  timeSelection.start = Math.max(rangeStart, left)
  timeSelection.end = Math.min(rangeEnd, right > left ? right : left + fallbackSpan)
  if (timeSelection.end - timeSelection.start < 60 * 1000) {
    timeSelection.start = Math.max(rangeStart, itemStart - fallbackSpan / 2)
    timeSelection.end = Math.min(rangeEnd, timeSelection.start + fallbackSpan)
  }
  const anomalyShiftMinutes = useAnomaly ? -Number(anomaly.offsetSeconds || 0) / 60 : 0
  timeSelection.shiftMinutes = anomalyShiftMinutes || 60
  timeSelection.preview = null
  timeSelection.active = true
  await startRepairAnimation(useAnomaly)
}

function cancelTimeSelection() {
  clearRepairAnimation()
  timeSelection.active = false
  timeSelection.preview = null
  timeSelection.animating = false
  timeSelection.animationPhase = ''
  selectionDrag = null
}

function onSelectionPointerDown(event, edge) {
  if (timeSelection.animating) return
  const rect = timeSelectionLayerRef.value?.getBoundingClientRect()
  if (!rect) return
  event.currentTarget.setPointerCapture?.(event.pointerId)
  const rangeStart = startDate.value.getTime()
  const rangeEnd = endDate.value.getTime()
  const pointerTime = rangeStart + Math.max(0, Math.min(1, (event.clientX - rect.left) / Math.max(1, rect.width))) * (rangeEnd - rangeStart)
  if (edge === 'create') {
    timeSelection.start = pointerTime
    timeSelection.end = Math.min(rangeEnd, pointerTime + 60 * 1000)
  }
  selectionDrag = {
    pointerId: event.pointerId,
    edge,
    startX: event.clientX,
    start: timeSelection.start,
    end: timeSelection.end,
    shiftMinutes: Number(timeSelection.shiftMinutes) || 0,
    anchor: pointerTime,
    width: Math.max(1, rect.width),
  }
  timeSelection.animationPhase = 'settled'
  timeSelection.preview = null
  scheduleRepairCapture()
}

function onSelectionPointerMove(event) {
  if (!selectionDrag || event.pointerId !== selectionDrag.pointerId) return
  const rangeStart = startDate.value.getTime()
  const rangeEnd = endDate.value.getTime()
  const delta = ((event.clientX - selectionDrag.startX) / selectionDrag.width) * (rangeEnd - rangeStart)
  const minSpan = 60 * 1000
  if (selectionDrag.edge === 'create') {
    const current = Math.max(rangeStart, Math.min(rangeEnd, selectionDrag.anchor + delta))
    timeSelection.start = Math.min(selectionDrag.anchor, current)
    timeSelection.end = Math.max(selectionDrag.anchor, current)
    if (timeSelection.end - timeSelection.start < minSpan) {
      timeSelection.end = Math.min(rangeEnd, timeSelection.start + minSpan)
      timeSelection.start = Math.max(rangeStart, timeSelection.end - minSpan)
    }
  } else if (selectionDrag.edge === 'target') {
    const minShift = rangeStart - selectionDrag.start
    const maxShift = rangeEnd - selectionDrag.end
    const nextShift = Math.max(minShift, Math.min(maxShift, selectionDrag.shiftMinutes * 60000 + delta))
    timeSelection.shiftMinutes = Number((nextShift / 60000).toFixed(2))
  } else if (selectionDrag.edge === 'start') {
    timeSelection.start = Math.max(rangeStart, Math.min(selectionDrag.end - minSpan, selectionDrag.start + delta))
  } else if (selectionDrag.edge === 'end') {
    timeSelection.end = Math.min(rangeEnd, Math.max(selectionDrag.start + minSpan, selectionDrag.end + delta))
  } else {
    const span = selectionDrag.end - selectionDrag.start
    const nextStart = Math.max(rangeStart, Math.min(rangeEnd - span, selectionDrag.start + delta))
    timeSelection.start = nextStart
    timeSelection.end = nextStart + span
  }
  timeSelection.preview = null
  scheduleRepairCapture()
}

function onSelectionPointerUp(event) {
  if (!selectionDrag || event.pointerId !== selectionDrag.pointerId) return
  selectionDrag = null
  timeSelection.animationPhase = 'settled'
  scheduleRepairCapture()
}

watch(() => timeSelection.shiftMinutes, () => {
  if (!timeSelection.active || timeSelection.animating) return
  timeSelection.preview = null
  timeSelection.animationPhase = 'settled'
})

function manualTimeBody() {
  return {
    from: toLocalDatetimeString(new Date(timeSelection.start)),
    to: toLocalDatetimeString(new Date(timeSelection.end)),
    shiftSeconds: Number(timeSelection.shiftMinutes) * 60,
    note: t('history.time.userNote'),
  }
}

async function previewManualTime() {
  timeSelection.preview = null
  const r = await fetch(`${apiBase}/api/time-anomalies/manual?preview=true`, {
    method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(manualTimeBody()),
  })
  if (!r.ok) return
  const result = await r.json()
  const counts = result.tableCounts || {}
  timeSelection.preview = { total: Object.values(counts).reduce((sum, value) => sum + value, 0), tables: Object.keys(counts) }
}

async function applyManualTime() {
  timeSelection.applying = true
  try {
    const r = await fetch(`${apiBase}/api/time-anomalies/manual`, {
      method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(manualTimeBody()),
    })
    if (!r.ok) return
    cancelTimeSelection()
    await loadData()
  } finally { timeSelection.applying = false }
}

function scheduleLoadData(delay = 180) {
  if (rangeLoadTimer) clearTimeout(rangeLoadTimer)
  rangeLoadTimer = setTimeout(() => {
    rangeLoadTimer = null
    loadData()
  }, delay)
}

function previewRange(min, max) {
  const chart = timelineChart
  if (!chart || chart.isDisposed() || !(max > min)) return
  renderedRangeMin = min
  renderedRangeMax = max
  chart.setOption({
    xAxis: [
      { min, max },
      { min, max },
    ],
  }, { lazyUpdate: false, silent: true })
  // Custom-series shapes must be repainted in the same frame. With a lazy
  // dirty-rect update they could remain blank until the next hover event.
  chart.getZr().refreshImmediately()
  barGeometryDirty = true
  hoveredProcess.value = null
  paintDimmer()
}

function playRangeMotion(oldStart, oldEnd, newStart, newEnd, pointerRatio = null) {
  if (!hasRenderedTimeline.value) return
  const startDelta = newStart - oldStart
  const endDelta = newEnd - oldEnd
  if (pointerRatio !== null) {
    rangeMotionOrigin.value = `${Math.round(pointerRatio * 100)}% 50%`
  } else if (startDelta !== 0 && endDelta === 0) {
    rangeMotionOrigin.value = '100% 50%'
  } else if (endDelta !== 0 && startDelta === 0) {
    rangeMotionOrigin.value = '0% 50%'
  } else if (startDelta + endDelta > 0) {
    rangeMotionOrigin.value = '100% 50%'
  } else if (startDelta + endDelta < 0) {
    rangeMotionOrigin.value = '0% 50%'
  } else {
    rangeMotionOrigin.value = '50% 50%'
  }

  rangeMotionClass.value = ''
  if (rangeMotionTimer) clearTimeout(rangeMotionTimer)
  requestAnimationFrame(() => {
    rangeMotionClass.value = 'range-retreating'
    rangeMotionTimer = setTimeout(() => {
      rangeMotionClass.value = ''
      rangeMotionTimer = null
    }, 240)
  })
}

function playZoomMotion(pointerRatio, zoomingIn) {
  if (!hasRenderedTimeline.value) return
  rangeMotionOrigin.value = `${Math.round(pointerRatio * 100)}% 50%`
  rangeMotionClass.value = ''
  if (rangeMotionTimer) clearTimeout(rangeMotionTimer)
  requestAnimationFrame(() => {
    rangeMotionClass.value = zoomingIn ? 'range-zooming-in' : 'range-zooming-out'
    rangeMotionTimer = setTimeout(() => {
      rangeMotionClass.value = ''
      rangeMotionTimer = null
    }, 180)
  })
}

function getSelectableBounds() {
  const earliestRaw = earliestDate.value?.getTime() ?? startDate.value.getTime()
  return {
    earliest: Math.ceil(earliestRaw / 60000) * 60000,
    latest: Math.floor(Date.now() / 60000) * 60000,
  }
}

function queueDragPreview(min, max) {
  pendingDragPreview = { min, max }
  if (dragPreviewFrame) return
  const flush = (now) => {
    if (now - lastDragPreviewAt < DRAG_PREVIEW_INTERVAL_MS) {
      dragPreviewFrame = requestAnimationFrame(flush)
      return
    }
    dragPreviewFrame = null
    lastDragPreviewAt = now
    const pending = pendingDragPreview
    pendingDragPreview = null
    if (!pending) return
    startDate.value = new Date(pending.min)
    endDate.value = new Date(pending.max)
    timeRangePickerRef.value?.setRange(startDate.value, endDate.value)
    previewRange(pending.min, pending.max)
  }
  dragPreviewFrame = requestAnimationFrame(flush)
}

function onTimelinePointerDown(event) {
  if (event.button !== 0 && event.button !== 1) return
  if (!hasRenderedTimeline.value || !timelineChart || timelineChart.isDisposed()) return

  event.preventDefault()
  if (rangeMotionTimer) clearTimeout(rangeMotionTimer)
  rangeMotionTimer = null
  rangeMotionClass.value = ''
  lastDragPreviewAt = 0
  const el = timelineChartRef.value
  const rect = el?.getBoundingClientRect()
  if (!rect) return

  dragState = {
    pointerId: event.pointerId,
    startX: event.clientX,
    startRange: startDate.value.getTime(),
    endRange: endDate.value.getTime(),
    plotWidth: Math.max(1, rect.width - 60),
    moved: false,
    rangeChanged: false,
    lastStart: startDate.value.getTime(),
    lastEnd: endDate.value.getTime(),
    clickData: hoveredTimelineData,
    element: event.currentTarget,
  }
  timelineDragging.value = true
  hoverSuppressedUntilMove = true
  hoveredProcess.value = null
  paintDimmer()
  try {
    event.currentTarget.setPointerCapture(event.pointerId)
  } catch {
    // Pointer capture is an enhancement; window-local dragging still works.
  }
}

function onTimelinePointerMove(event) {
  const drag = dragState
  if (!drag || drag.pointerId !== event.pointerId) {
    if (hoverSuppressedUntilMove) {
      hoverSuppressedUntilMove = false
      hoveredProcess.value = null
      paintDimmer()
    }
    return
  }
  const dx = event.clientX - drag.startX
  if (!drag.moved && Math.abs(dx) < 3) return

  event.preventDefault()
  drag.moved = true
  const span = drag.endRange - drag.startRange
  const shift = -(dx / drag.plotWidth) * span
  const { earliest, latest } = getSelectableBounds()
  let nextStart = Math.round((drag.startRange + shift) / 60000) * 60000
  let nextEnd = nextStart + span

  if (nextStart < earliest) {
    nextStart = earliest
    nextEnd = earliest + span
  }
  if (nextEnd > latest) {
    nextEnd = latest
    nextStart = latest - span
  }
  if (nextStart < earliest || !(nextEnd > nextStart)) return
  if (nextStart === drag.lastStart && nextEnd === drag.lastEnd) return

  drag.lastStart = nextStart
  drag.lastEnd = nextEnd
  drag.rangeChanged = true
  queueDragPreview(nextStart, nextEnd)
}

function onTimelinePointerUp(event) {
  const drag = dragState
  if (!drag || drag.pointerId !== event.pointerId) return
  if (drag.moved) event.preventDefault()
  try {
    drag.element.releasePointerCapture(event.pointerId)
  } catch {
    // It may already have been released by a pointercancel.
  }

  if (dragPreviewFrame) {
    cancelAnimationFrame(dragPreviewFrame)
    dragPreviewFrame = null
  }
  pendingDragPreview = null
  if (drag.rangeChanged) {
    startDate.value = new Date(drag.lastStart)
    endDate.value = new Date(drag.lastEnd)
    timeRangePickerRef.value?.setRange(startDate.value, endDate.value)
    previewRange(drag.lastStart, drag.lastEnd)
    scheduleLoadData(220)
  } else if (drag.clickData) {
    if (drag.clickData._mediaSession) focusMediaContext(drag.clickData._mediaSession)
    else openProcessActions(drag.clickData, { x: event.clientX, y: event.clientY })
  }
  dragState = null
  timelineDragging.value = false
}

function focusMediaContext(selected) {
  const sessions = mediaSessions.value
    .filter(item => item.playbackStatus !== 'SystemSleep')
    .map(item => ({
      item,
      start: parseUtcTs(item.startTime)?.getTime(),
      end: item.endTime ? parseUtcTs(item.endTime)?.getTime() : Date.now(),
    }))
    .filter(item => Number.isFinite(item.start) && Number.isFinite(item.end) && item.end > item.start)
    .sort((a, b) => a.start - b.start)
  const index = sessions.findIndex(entry =>
    (selected.id != null && entry.item.id === selected.id) || entry.item.startTime === selected.startTime)
  if (index < 0) return

  const durations = sessions.map(entry => entry.end - entry.start).sort((a, b) => a - b)
  const medianDuration = durations[Math.floor(durations.length / 2)] || (sessions[index].end - sessions[index].start)
  // Six typical neighboring tracks naturally lands around twenty minutes for
  // music, while podcasts and very short clips receive an adaptive window.
  const targetSpan = Math.max(8 * 60_000, Math.min(45 * 60_000, medianDuration * 6))
  let left = index
  let right = index
  while (right - left + 1 < sessions.length
    && sessions[right].end - sessions[left].start < targetSpan) {
    const leftGap = left > 0 ? sessions[left].start - sessions[left - 1].end : Infinity
    const rightGap = right + 1 < sessions.length ? sessions[right + 1].start - sessions[right].end : Infinity
    if (leftGap <= rightGap) left--
    else right++
  }
  const padding = Math.max(60_000, medianDuration * .35)
  const { earliest, latest } = getSelectableBounds()
  const nextStart = Math.max(earliest, sessions[left].start - padding)
  const nextEnd = Math.min(latest, sessions[right].end + padding)
  if (!(nextEnd > nextStart)) return
  startDate.value = new Date(nextStart)
  endDate.value = new Date(nextEnd)
  timeRangePickerRef.value?.setRange(startDate.value, endDate.value)
  playRangeMotion(renderedRangeMin || nextStart, renderedRangeMax || nextEnd, nextStart, nextEnd)
  loadData()
}

function onTimelineWheel(event) {
  const chart = timelineChart
  const el = timelineChartRef.value
  if (!chart || chart.isDisposed() || !el || (loading.value && !hasRenderedTimeline.value)) return

  const oldStart = startDate.value.getTime()
  const oldEnd = endDate.value.getTime()
  const oldSpan = oldEnd - oldStart
  if (!(oldSpan > 0)) return
  hoverSuppressedUntilMove = true
  hoveredProcess.value = null
  paintDimmer()

  const rect = el.getBoundingClientRect()
  const localX = event.clientX - rect.left
  const plotLeft = focusedProcess.value || timelineViewMode.value !== 'process' ? 220 : 20
  const plotRight = rect.width - 40
  // The label gutters and empty side margins belong to normal page scrolling.
  // Only a wheel gesture over the actual time plot is interpreted as zoom.
  if (localX < plotLeft || localX > plotRight) return
  event.preventDefault()
  // Match the chart grid's fixed horizontal insets (left 20 / right 40). This
  // avoids a layout conversion for every high-resolution wheel event.
  const plotRatio = Math.max(0, Math.min(1, (localX - plotLeft) / Math.max(1, plotRight - plotLeft)))
  const pointerRatio = Math.max(0, Math.min(1, localX / Math.max(1, rect.width)))
  const anchor = oldStart + oldSpan * plotRatio

  // Wheel up zooms in; wheel down zooms out. Exponential steps feel stable
  // with both a notched mouse wheel and a high-resolution trackpad.
  const factor = event.deltaY < 0 ? 0.8 : 1.25
  const { earliest, latest } = getSelectableBounds()
  const minSpan = 2 * 60 * 1000
  const maxSpan = Math.max(minSpan, latest - earliest)
  const newSpan = Math.max(minSpan, Math.min(maxSpan, oldSpan * factor))
  const anchorRatio = (anchor - oldStart) / oldSpan
  let newStart = anchor - newSpan * anchorRatio
  let newEnd = newStart + newSpan

  if (newStart < earliest) {
    newStart = earliest
    newEnd = Math.min(latest, earliest + newSpan)
  }
  if (newEnd > latest) {
    newEnd = latest
    newStart = Math.max(earliest, latest - newSpan)
  }

  // TimeRangePicker displays minute precision; align the chart to that exact
  // range so the two controls never drift apart.
  newStart = Math.floor(newStart / 60000) * 60000
  newEnd = Math.ceil(newEnd / 60000) * 60000
  if (newEnd > latest) newEnd = latest
  if (newEnd - newStart < minSpan) newStart = newEnd - minSpan
  if (newStart < earliest || !(newEnd > newStart)) return

  startDate.value = new Date(newStart)
  endDate.value = new Date(newEnd)
  timeRangePickerRef.value?.setRange(startDate.value, endDate.value)
  previewRange(newStart, newEnd)
  playZoomMotion(pointerRatio, factor < 1)
  scheduleLoadData(220)
}


async function loadData() {
  const myLoadId = ++loadId
  activeLoadController?.abort()
  const loadController = new AbortController()
  activeLoadController = loadController
  loading.value = true
  error.value = ''

  if (dataTooShort.value) {
    loading.value = false
    initialRenderReady.value = true
    return
  }

  try {
    const selectedFromStr = toLocalDatetimeString(startDate.value)
    const selectedToStr = toLocalDatetimeString(endDate.value)

    // Keep one nearby viewport in memory for short ranges, capped at three days
    // per side for long ranges. The summary remains scoped to the exact user
    // selection; only timeline-shaped data is prefetched.
    const visibleRangeMs = endDate.value - startDate.value
    const prefetchMs = Math.min(visibleRangeMs, PREFETCH_MAX_MS)
    const earliestMs = earliestDate.value?.getTime() ?? startDate.value.getTime() - prefetchMs
    const queryStart = new Date(Math.max(earliestMs, startDate.value.getTime() - prefetchMs))
    const queryEnd = new Date(Math.min(Date.now(), endDate.value.getTime() + prefetchMs))
    const fromStr = toLocalDatetimeString(queryStart)
    const toStr = toLocalDatetimeString(queryEnd)
    console.log('Loading selected range', selectedFromStr, 'to', selectedToStr, 'with timeline buffer', fromStr, 'to', toStr)

    // Build all URLs upfront so the four independent endpoints fire in parallel
    const summaryUrl = `${apiBase}/api/summary/range?from=${selectedFromStr}&to=${selectedToStr}`

    // Adjust limit for the prefetched range so both neighboring buffers retain
    // representative coverage.
    const rangeInMs = queryEnd - queryStart
    const rangeInHours = rangeInMs / (1000 * 60 * 60)
    const rangeInDays = rangeInMs / (1000 * 60 * 60 * 24)

    // Scale limit based on time range - smaller ranges need fewer records
    let timelineLimit
    if (rangeInHours <= 6) {
      timelineLimit = 1000  // 6 hours or less
    } else if (rangeInDays <= 1) {
      timelineLimit = 3000  // Up to 1 day
    } else if (rangeInDays <= 3) {
      timelineLimit = 10000  // Up to 3 days
    } else if (rangeInDays <= 7) {
      timelineLimit = 20000  // Up to 1 week
    } else {
      timelineLimit = 50000  // More than 1 week
    }

    const timelineUrl = `${apiBase}/api/windows/timeline?from=${fromStr}&to=${toStr}&limit=${timelineLimit}`
    const eventsUrl = `${apiBase}/api/system/events?from=${fromStr}&to=${toStr}`
    const sessionsUrl = `${apiBase}/api/windows/sessions?from=${fromStr}&to=${toStr}&limit=10000`
    const relationIds = focusedProcessEntries.value.map(entry => entry.processId).filter(id => id != null)
    const relationSessionsUrl = relationIds.length
      ? `${apiBase}/api/processes/sessions?from=${fromStr}&to=${toStr}&ids=${encodeURIComponent(relationIds.join(','))}`
      : null
    const viewSessionsUrl = timelineViewMode.value === 'tag'
      ? `${apiBase}/api/processes/sessions?from=${fromStr}&to=${toStr}&tagged=true&limit=50000`
      : timelineViewMode.value === 'processFilter' && timelineFilter.value.trim()
        ? `${apiBase}/api/processes/sessions?from=${fromStr}&to=${toStr}&process=${encodeURIComponent(timelineFilter.value.trim())}&limit=50000`
        : null

    console.log('Loading data from', fromStr, 'to', toStr, `(range: ${rangeInHours.toFixed(1)} hours / ${rangeInDays.toFixed(1)} days, limit: ${timelineLimit})`)
    const mediaUrl = mediaMode.value
      ? `${apiBase}/api/media/history?limit=1000&from=${toLocalDateString(queryStart)}&to=${toLocalDateString(queryEnd)}`
      : null
    const [r1, r2, r3, r4, r5, r6, r7] = await Promise.all([
      fetch(summaryUrl, { signal: loadController.signal }),
      fetch(timelineUrl, { signal: loadController.signal }),
      fetch(eventsUrl, { signal: loadController.signal }),
      fetch(sessionsUrl, { signal: loadController.signal }),
      relationSessionsUrl ? fetch(relationSessionsUrl, { signal: loadController.signal }) : Promise.resolve(null),
      viewSessionsUrl ? fetch(viewSessionsUrl, { signal: loadController.signal }) : Promise.resolve(null),
      mediaUrl ? fetch(mediaUrl, { signal: loadController.signal }) : Promise.resolve(null),
    ])

    if (!r1.ok) throw new Error(`Summary API ${r1.status}`)
    if (!r2.ok) throw new Error(`Timeline API ${r2.status}`)

    // Parse every response into locals first. A stale load must not partially
    // replace refs before the final generation check below.
    const [res, timelineRes, nextSystemEvents, nextWindowSessions,
      nextRelationSessions, nextFilteredSessions, nextMediaSessions] = await Promise.all([
      r1.json(),
      r2.json(),
      r3.ok ? r3.json() : Promise.resolve([]),
      r4.ok ? r4.json() : Promise.resolve([]),
      r5?.ok ? r5.json() : Promise.resolve([]),
      r6?.ok ? r6.json() : Promise.resolve([]),
      r7?.ok ? r7.json() : Promise.resolve([]),
    ])
    if (myLoadId !== loadId) return

    const rawData = Array.isArray(res) ? res : res.items || []
    const mergedData = mergeByProcessName(rawData, (item, acc) => {
      acc.totalSeconds += item.totalSeconds
      acc.switchCount += item.switchCount
      if (item.adjustedSwitchCount !== undefined) {
        acc.adjustedSwitchCount = (acc.adjustedSwitchCount || 0) + item.adjustedSwitchCount
      }
    })
    mergedData.sort((a, b) => b.totalSeconds - a.totalSeconds)

    const timelineData = timelineRes.data || timelineRes
    const timelineTotal = timelineRes.total ?? timelineData.length
    const sampled = timelineRes.sampled === true
    const truncated = !sampled && timelineTotal > timelineData.length
    console.log('Timeline API returned', timelineData.length, 'records (total available:', timelineTotal, ')', sampled ? '(server-sampled)' : '')

    if (truncated) {
      console.warn(`⚠️ Timeline data truncated: showing ${timelineData.length} of ${timelineTotal} records`)
    }

    // Systematic time-based sampling: keep every Nth point sorted by time,
    // so the chart spans the full range evenly instead of clustering on long-duration items.
    // Wide ranges are already sampled server-side (sampled=true) — skip re-sampling.
    let sampledData = timelineData
    const MAX_POINTS = 8000

    if (!sampled && timelineData.length > MAX_POINTS) {
      // Sort chronologically first
      const sorted = [...timelineData].sort((a, b) => new Date(a.timestamp) - new Date(b.timestamp))
      const step = Math.ceil(sorted.length / MAX_POINTS)
      sampledData = sorted.filter((_, i) => i % step === 0)
      console.log(`📊 Systematic sampling: ${sampledData.length} records from ${timelineData.length} (every ${step}th point)`)
    }

    // Commit one coherent generation after all parsing and transformation.
    data.value = mergedData
    totalSleepSeconds.value = Array.isArray(res) ? 0 : res.totalSleepSeconds || 0
    timeline.value = sampledData
    timelineMeta.value = {
      sampled: sampled || timelineData.length > MAX_POINTS,
      truncated,
    }
    systemEvents.value = nextSystemEvents
    windowSessions.value = nextWindowSessions
    relationProcessSessions.value = nextRelationSessions
    filteredProcessSessions.value = nextFilteredSessions
    mediaSessions.value = nextMediaSessions
    console.log('✅ timeline.value set to:', timeline.value.length, 'records')
    console.log('System events:', systemEvents.value.length, 'events')
    console.log('Window sessions:', windowSessions.value.length)

    loading.value = false
    await nextTick()
    await renderTimeline(myLoadId)
    if (myLoadId === loadId) initialRenderReady.value = true
  } catch (e) {
    if (e.name === 'AbortError') return
    // If this isn't the latest call, don't show the error
    if (myLoadId !== loadId) return
    console.error('Load error:', e)
    error.value = t('history.error.loadDataFailed', { message: e.message })
    loading.value = false
    initialRenderReady.value = true
  }
}

let clearTimer = null

function onTimelineMouseOver(params) {
  if (timelineDragging.value || hoverSuppressedUntilMove) return
  if (params.seriesName !== 'windows') return
  if (params.data?._mediaSession) {
    hoveredTimelineData = params.data
    hoveredProcess.value = null
    paintDimmer()
    return
  }
  const proc = params.data && params.data.processName
  if (!proc) return
  if (clearTimer) {
    clearTimeout(clearTimer)
    clearTimer = null
  }
  if (hoveredProcess.value === proc) return
  hoveredProcess.value = proc
  hoveredTimelineData = params.data
  if (barGeometryDirty) buildAllBarsPx()
  paintDimmer()
}

function onTimelineMouseOut(params) {
  // Leaving a bar: seriesName is set. Leaving all elements (blank chart area):
  // ECharts emits a global mouseout without seriesName — clear hover then too.
  if (params.seriesName && params.seriesName !== 'windows') return
  if (clearTimer) return
  // 50ms buffer: a quick move to another bar cancels this and switches
  // directly, so the dim/restore flicker is skipped.
  clearTimer = setTimeout(() => {
    clearTimer = null
    hoveredProcess.value = null
    hoveredTimelineData = null
    paintDimmer()
  }, 50)
}

function onTimelineMouseLeave() {
  if (clearTimer) {
    clearTimeout(clearTimer)
    clearTimer = null
  }
  hoveredProcess.value = null
  hoveredTimelineData = null
  hoverSuppressedUntilMove = false
  paintDimmer()
}

// Build pixel rects for every bar on first hover after a render/resize. Bars
// are grouped by row center Y; only row centers use convertToPixel and every
// x-coordinate uses direct arithmetic.
function buildAllBarsPx() {
  allBarsPx = []
  const chart = timelineChart
  if (!chart || chart.isDisposed()) return
  const ts = focusedWindowsData[0]?._ts ?? backgroundWindowsData[0]?.value?.[1]
  if (ts === undefined || ts === null) return
  // Use the bar height recorded by renderBar (api.size * 0.6) — an exact
  // match with what is drawn, for any number of rows. If there were no focused
  // bars, derive it from category spacing and use a single-row fallback.
  const px = { xAxisIndex: 0, yAxisIndex: 0 }
  let barH = lastFocusedBarH
  if (!(barH > 0) || !Number.isFinite(barH)) {
    if (lastRowCount > 1) {
      const p0 = chart.convertToPixel(px, [ts, 0])
      const p1 = chart.convertToPixel(px, [ts, 1])
      const rowH = p0 && p1 ? p1[1] - p0[1] : NaN
      if (rowH > 0 && Number.isFinite(rowH)) barH = rowH * 0.6
    }
  }
  if (!(barH > 0) || !Number.isFinite(barH)) {
    barH = ((timelineChartRef.value?.offsetHeight || 440) / 20) * 0.6
  }
  const min = renderedRangeMin || startDate.value.getTime()
  const max = renderedRangeMax || endDate.value.getTime()
  const axisStart = chart.convertToPixel(px, [min, 0])
  const axisEnd = chart.convertToPixel(px, [max, 0])
  if (!axisStart || !axisEnd || !(max > min)) return
  const x0 = axisStart[0]
  const xScale = (axisEnd[0] - x0) / (max - min)
  const rowCenters = new Map()
  const centerFor = (rowIdx) => {
    if (!rowCenters.has(rowIdx)) {
      const point = chart.convertToPixel(px, [min, rowIdx])
      rowCenters.set(rowIdx, point?.[1])
    }
    return rowCenters.get(rowIdx)
  }
  const rows = new Map()
  const push = (proc, x, w, centerY, focused) => {
    let row = rows.get(centerY)
    if (!row) {
      row = { centerY, barH, bars: [] }
      rows.set(centerY, row)
    }
    row.bars.push({ proc, x, w, focused })
  }
  for (const item of focusedWindowsData) {
    const centerY = centerFor(item._rowIdx)
    if (!Number.isFinite(centerY)) continue
    const x = x0 + (item._ts - min) * xScale
    const endX = x0 + (item._end - min) * xScale
    push(item.processName, x, Math.max(endX - x, 2), centerY, true)
  }
  for (const item of backgroundWindowsData) {
    const rowIdx = item.value[0]
    const centerY = centerFor(rowIdx)
    if (!Number.isFinite(centerY)) continue
    const x = x0 + (item.value[1] - min) * xScale
    const endX = x0 + (item.value[2] - min) * xScale
    const processName = item.processName || item._bgSession?.processName
    if (processName) push(processName, x, Math.max(endX - x, 2), centerY, false)
  }
  allBarsPx = [...rows.values()]
  barGeometryDirty = false
}

// Paint the dim layer: background-color rectangles over every bar except the
// hovered process's. Clearing it (no hover) restores the full-color chart.
// Runs on hover transitions only, never per mousemove. Intervals are unioned
// before painting so every pixel is painted exactly once — per-bar rects
// stacked alpha on overlaps (0.7 twice → 0.91) and left dark seams where a
// background line crossed a focused bar. Focused bars (barH-tall bands) and
// background lines (1px at the center) are painted separately so a band never
// dims empty space above/below a thin line.
function paintDimmer() {
  const canvas = hoverDimmerRef.value
  if (!canvas) return
  const el = timelineChartRef.value
  const dpr = window.devicePixelRatio || 1
  const cw = el ? el.offsetWidth : canvas.offsetWidth
  const ch = el ? el.offsetHeight : canvas.offsetHeight
  if (canvas.width !== cw * dpr || canvas.height !== ch * dpr) {
    canvas.width = cw * dpr
    canvas.height = ch * dpr
  }
  const ctx = canvas.getContext('2d')
  ctx.setTransform(dpr, 0, 0, dpr, 0, 0)
  ctx.clearRect(0, 0, cw, ch)
  const proc = hoveredProcess.value
  if (!proc || allBarsPx.length === 0) return
  ctx.fillStyle = withAlpha(surfaceCard, 0.7)
  for (const row of allBarsPx) {
    const focusedInts = []
    const bgInts = []
    for (const bar of row.bars) {
      if (bar.proc === proc) continue
      const iv = [bar.x, bar.x + bar.w]
      if (bar.focused) focusedInts.push(iv)
      else bgInts.push(iv)
    }
    const focused = mergePixelIntervals(focusedInts)
    // Focused bars: barH-tall band centered on the row.
    const y = row.centerY - row.barH / 2
    for (const [x0, x1] of focused) ctx.fillRect(x0, y, x1 - x0, row.barH)
    // Background lines: 1px at the center, clipped out of the focused band so
    // the crossing region is painted only once.
    for (const [b0, b1] of mergePixelIntervals(bgInts)) {
      let start = b0
      for (const [f0, f1] of focused) {
        if (f1 <= start) continue
        if (f0 >= b1) break
        if (f0 > start) ctx.fillRect(start, row.centerY, f0 - start, 1)
        start = Math.max(start, f1)
        if (start >= b1) break
      }
      if (start < b1) ctx.fillRect(start, row.centerY, b1 - start, 1)
    }
  }
}

async function renderTimeline(myLoadId) {
  console.log('=== renderTimeline called ===')
  console.log('timelineChartRef.value:', !!timelineChartRef.value)
  console.log('timeline.value.length:', timeline.value.length)

  if (!timelineChartRef.value) {
    // The chart div can be missing when the picker briefly showed an invalid
    // range (the easter egg replaces the div) while this load was in flight.
    // Wait for it to reappear instead of giving up — a permanent bail leaves
    // the chart area dead (no canvases, no adaptive height) until the next
    // range change.
    for (let i = 0; i < 40 && !timelineChartRef.value; i++) {
      await new Promise(r => setTimeout(r, 50))
    }
    if (!timelineChartRef.value) {
      console.warn('Timeline chart ref not ready')
      return
    }
  }

  if (!timeline.value.length) console.log('Rendering timeline without focus rows')

  // Bail if a newer load has started
  if (myLoadId !== undefined && myLoadId !== loadId) {
    console.log(`Stale renderTimeline call #${myLoadId} — current is #${loadId}, skipping`)
    return
  }

  console.log('Rendering timeline with', timeline.value.length, 'records')

  const xAxisMin = startDate.value.getTime()
  const xAxisMax = endDate.value.getTime()
  const rangeInDays = (xAxisMax - xAxisMin) / (1000 * 60 * 60 * 24)
  const isDefaultProcessView = timelineViewMode.value === 'process'
  const isTagView = timelineViewMode.value === 'tag'
  const isProcessFilterView = timelineViewMode.value === 'processFilter'
  const isTitleFilterView = timelineViewMode.value === 'titleFilter'
  const useDailyTop = isDefaultProcessView && !focusedProcess.value && rangeInDays >= 1
  const TOP_PROCESS_COUNT = 15

  const makeMatcher = pattern => {
    if (!pattern) return null
    try { return new RegExp(pattern, 'i') }
    catch { return new RegExp(pattern.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'), 'i') }
  }
  const filterMatcher = (isProcessFilterView || isTitleFilterView) ? makeMatcher(timelineFilter.value.trim()) : null
  const sourceTimeline = timeline.value.filter(item => {
    if (isProcessFilterView) return !!filterMatcher && filterMatcher.test(item.processName || '')
    if (isTitleFilterView) return !!filterMatcher && filterMatcher.test(item.windowTitle || '')
    return true
  })
  const sourceWindowSessions = windowSessions.value.filter(session => {
    if (isProcessFilterView) return !!filterMatcher && filterMatcher.test(session.processName || '')
    if (isTitleFilterView) return !!filterMatcher && filterMatcher.test(session.windowTitle || '')
    return true
  })
  const displayNameByProcess = new Map()
  for (const item of [...sourceTimeline, ...sourceWindowSessions]) {
    if (item.displayName && item.displayName !== item.processName)
      displayNameByProcess.set(item.processName, item.displayName)
  }
  const displayProcessName = processName => displayNameByProcess.get(processName) || processName
  const allTags = [...new Set(tagRules.value
    .map(rule => rule.tag)
    .filter(tag => tag && !tag.startsWith('_')))]

  // Step 1: Group the selected + prefetched data by the existing 4 AM day
  // boundary. Short ranges use one visible-range Top 15; day-sized ranges use
  // a separate Top 15 for every day.
  const DAY_START = 4 * 60 * 60 * 1000
  const getDayKey = (tsMs) => {
    const d = new Date(tsMs - DAY_START)
    return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
  }

  // Build one authoritative off-period map. SystemEvents also contains Idle;
  // only Sleep/Shutdown may subtract tracked activity.
  const rawSleepPeriods = systemEvents.value
    .filter(e => e.eventType === 'Sleep' || e.eventType === 'Shutdown')
    .map(e => ({
      start: parseUtcTs(e.timestamp).getTime(),
      end: parseUtcTs(e.timestamp).getTime() + e.durationSeconds * 1000,
    }))
    .filter(period => period.end > period.start)
    .sort((a, b) => a.start - b.start)
  const sleepPeriods = mergeTimePeriods(rawSleepPeriods)

  // Parse once and split every focus interval around Sleep/Shutdown. This keeps
  // a record that began seconds before suspend from rendering as an eight-hour
  // activity bar, while preserving its real pre/post-sleep fragments.
  const parsedSource = []
  const dayStats = new Map()
  for (const item of sourceTimeline) {
    const tsMs = parseUtcTs(item.timestamp).getTime()
    const rawEnd = tsMs + Math.max(0, item.durationSeconds) * 1000
    for (const [segmentStart, segmentEnd] of subtractTimePeriods(tsMs, rawEnd, sleepPeriods)) {
      if (segmentEnd <= segmentStart) continue
      const dk = getDayKey(segmentStart)
      const durationSeconds = (segmentEnd - segmentStart) / 1000
      parsedSource.push({
        ...item,
        timestamp: new Date(segmentStart).toISOString(),
        durationSeconds,
        _ts: segmentStart,
        _end: segmentEnd,
        _dayKey: dk,
      })
      let stats = dayStats.get(dk)
      if (!stats) {
        stats = new Map()
        dayStats.set(dk, stats)
      }
      stats.set(item.processName, (stats.get(item.processName) || 0) + durationSeconds)
    }
  }

  // Step 2: Select Top 15 and assign rows. In daily mode a process keeps its
  // most recent row whenever that row is free, minimizing visual reordering
  // while guaranteeing a hard maximum of 15 rows on long ranges.
  const dayTopSet = new Map()
  const processRowsByDay = new Map()
  const globalProcessRow = new Map()
  const allProcs = new Set()
  const isolatedRows = []
  const tagRowMap = new Map()
  const tagProcessDurations = new Map()
  let rowCount = 0

  if (isTagView) {
    allTags.forEach((tag, row) => tagRowMap.set(tag, row))
    rowCount = allTags.length
    for (const item of parsedSource) {
      for (const tag of item.tags || []) {
        if (!tagRowMap.has(tag)) continue
        allProcs.add(item.processName)
        if (!tagProcessDurations.has(tag)) tagProcessDurations.set(tag, new Map())
        const stats = tagProcessDurations.get(tag)
        stats.set(item.processName, (stats.get(item.processName) || 0) + item.durationSeconds)
      }
    }
    for (const session of [...sourceWindowSessions, ...filteredProcessSessions.value]) {
      for (const tag of session.tags || []) {
        if (!tagRowMap.has(tag)) continue
        allProcs.add(session.processName)
        const start = parseUtcTs(session.openTime || session.startTime).getTime()
        const endValue = session.closeTime || session.endTime
        const end = endValue ? parseUtcTs(endValue).getTime() : xAxisMax
        if (!tagProcessDurations.has(tag)) tagProcessDurations.set(tag, new Map())
        const stats = tagProcessDurations.get(tag)
        stats.set(session.processName, (stats.get(session.processName) || 0) + Math.max(0, end - start) / 1000)
      }
    }
  } else if (isProcessFilterView || isTitleFilterView) {
    const names = new Set([
      ...parsedSource.map(item => item.processName),
      ...sourceWindowSessions.map(session => session.processName),
      ...(isProcessFilterView ? filteredProcessSessions.value.map(session => session.processName) : []),
    ].filter(Boolean))
    ;[...names].sort((a, b) => a.localeCompare(b)).forEach((name, row) => {
      globalProcessRow.set(name, row)
      allProcs.add(name)
    })
    rowCount = names.size
    for (const dk of dayStats.keys()) dayTopSet.set(dk, new Set(names))
  } else if (focusedProcess.value) {
    const entries = focusedProcessEntries.value.length
      ? focusedProcessEntries.value
      : [{ name: focusedProcess.value, processId: null, role: 'target' }]
    for (const entry of entries) {
      const actualName = parsedSource.find(item => item.processName.toLowerCase() === entry.name.toLowerCase())?.processName
        || entry.name
      const row = isolatedRows.length
      if (!globalProcessRow.has(actualName)) globalProcessRow.set(actualName, row)
      allProcs.add(actualName)
      isolatedRows.push({ ...entry, name: actualName, row })
    }
    rowCount = Math.max(1, isolatedRows.length)
    for (const dk of dayStats.keys()) dayTopSet.set(dk, new Set(allProcs))
  } else if (useDailyTop) {
    const rememberedRows = new Map(stableDailyProcessRows)
    for (const dk of [...dayStats.keys()].sort()) {
      const ranked = [...dayStats.get(dk)]
        .sort((a, b) => b[1] - a[1])
        .slice(0, TOP_PROCESS_COUNT)
        .map(entry => entry[0])
      const topSet = new Set(ranked)
      const assignment = new Map()
      const usedRows = new Set()

      // First preserve rows from earlier days.
      for (const proc of ranked) {
        const preferred = rememberedRows.get(proc)
        if (preferred === undefined || usedRows.has(preferred)) continue
        assignment.set(proc, preferred)
        usedRows.add(preferred)
      }
      // New/conflicting processes prefer their rank, then the first free row.
      for (let rank = 0; rank < ranked.length; rank++) {
        const proc = ranked[rank]
        if (assignment.has(proc)) continue
        let row = usedRows.has(rank) ? 0 : rank
        while (usedRows.has(row)) row++
        assignment.set(proc, row)
        usedRows.add(row)
      }

      for (const [proc, row] of assignment) {
        rememberedRows.set(proc, row)
        allProcs.add(proc)
        rowCount = Math.max(rowCount, row + 1)
      }
      dayTopSet.set(dk, topSet)
      processRowsByDay.set(dk, assignment)
    }
    stableDailyProcessRows.clear()
    for (const [proc, row] of rememberedRows) stableDailyProcessRows.set(proc, row)
  } else {
    const visibleStats = new Map()
    for (const item of parsedSource) {
      const clippedMs = Math.min(item._end, xAxisMax) - Math.max(item._ts, xAxisMin)
      if (clippedMs <= 0) continue
      visibleStats.set(item.processName, (visibleStats.get(item.processName) || 0) + clippedMs)
    }
    const ranked = [...visibleStats]
      .sort((a, b) => b[1] - a[1])
      .slice(0, TOP_PROCESS_COUNT)
      .map(entry => entry[0])
    const topSet = new Set(ranked)
    ranked.forEach((proc, row) => {
      globalProcessRow.set(proc, row)
      allProcs.add(proc)
    })
    for (const dk of dayStats.keys()) dayTopSet.set(dk, topSet)
    rowCount = ranked.length
  }

  const getProcessRow = (processName, dayKey) => {
    if (useDailyTop) return processRowsByDay.get(dayKey)?.get(processName)
    return globalProcessRow.get(processName)
  }

  // Pre-filter and attach each item's row once. Adjacent buffered days remain
  // in the series so they can slide into view immediately during dragging.
  const parsedTimeline = []
  if (isTagView) {
    for (const item of parsedSource) {
      for (const tag of item.tags || []) {
        const rowIdx = tagRowMap.get(tag)
        if (rowIdx === undefined) continue
        parsedTimeline.push({ ...item, _rowIdx: rowIdx, _tag: tag })
      }
    }
  } else {
    for (const item of parsedSource) {
      const topSet = dayTopSet.get(item._dayKey)
      if (!topSet?.has(item.processName)) continue
      const rowIdx = getProcessRow(item.processName, item._dayKey)
      if (rowIdx === undefined) continue
      item._rowIdx = rowIdx
      parsedTimeline.push(item)
    }
  }

  const showRowLabels = mediaMode.value || !!focusedProcess.value || isTagView || isProcessFilterView || isTitleFilterView
  let processList = isTagView
    ? allTags
    : (isProcessFilterView || isTitleFilterView)
      ? [...globalProcessRow.keys()]
        .map(displayProcessName)
      : focusedProcess.value
        ? isolatedRows.map(row => {
        const role = row.role === 'target' ? '' : row.role === 'parent'
          ? `${t('processActions.parent')} · ` : `${t('processActions.children')} · `
        const pid = row.processId != null ? ` #${row.processId}` : ''
        return `${role}${displayProcessName(row.name)}${pid}`
      })
        : Array.from({ length: rowCount }, (_, i) => String(i + 1))
  const mediaRowIndex = mediaMode.value ? rowCount : -1
  if (mediaMode.value) {
    processList = [...processList, t('history.media.row')]
    rowCount++
  }
  const allProcessNames = [...allProcs]

  console.log(`${allProcessNames.length} Top-15 processes across ${dayStats.size} days, ${rowCount} stable rows`)

  // Quiet ranges still keep the normal canvas height. This prevents the card
  // from jumping upward when a drag lands on sleep/idle-only data.
  const namedRowsHeight = showRowLabels ? Math.min(5000, Math.max(DEFAULT_CHART_HEIGHT, rowCount * 30 + 90)) : DEFAULT_CHART_HEIGHT
  timelineChartRef.value.style.height = `${namedRowsHeight}px`
  if (timelineChart && !timelineChart.isDisposed()) timelineChart.resize({ height: namedRowsHeight })

  // Step 3.5: Fetch colors for all processes (time-aware — use range start date).
  // Keep the time-aware cache key so historical tag colors remain exact.
  // Request debouncing prevents transient wheel positions from reaching here.
  const atTime = startDate.value.toISOString()
  const colorPromises = allProcessNames.map(async (processName) => {
    const cacheKey = `${processName}|${atTime}`
    const cached = getCachedColor(cacheKey)
    if (cached) return cached
    try {
      const signal = activeLoadController?.signal ?? lifetimeController.signal
      const response = await fetch(`${apiBase}/api/icons/${encodeURIComponent(processName)}?at=${encodeURIComponent(atTime)}`, { signal })
      if (response.ok) {
        const iconData = await response.json()
        const color = iconData.colorPrimary || '#6B7FD7'
        setCachedColor(cacheKey, color)
        return color
      }
    } catch (e) {
      if (e.name === 'AbortError') return '#6B7FD7'
      console.warn(`Failed to fetch color for ${processName}:`, e)
    }
    setCachedColor(cacheKey, '#6B7FD7')
    return '#6B7FD7'
  })

  const processColors = await Promise.all(colorPromises)
  const colorMap = {}
  allProcessNames.forEach((name, idx) => {
    colorMap[name] = processColors[idx]
  })
  const tagColorMap = {}
  for (const tag of allTags) {
    const stats = tagProcessDurations.get(tag)
    const dominant = stats ? [...stats].sort((a, b) => b[1] - a[1])[0]?.[0] : null
    tagColorMap[tag] = dominant ? colorMap[dominant] : '#8A8F9D'
  }

  console.log('Rendering', parsedTimeline.length, `records (${useDailyTop ? 'daily' : 'range'} Top 15)`)

  // Step 3.5: use the merged off-period map built before ranking/splitting.
  // Binary search the last period that starts before tsMs. This changes the
  // repeated sleep lookup from O(records × events) to O(records × log events).
  function isDuringSleep(tsMs) {
    let lo = 0
    let hi = sleepPeriods.length
    while (lo < hi) {
      const mid = (lo + hi) >> 1
      if (sleepPeriods[mid].start <= tsMs) lo = mid + 1
      else hi = mid
    }
    return lo > 0 && tsMs < sleepPeriods[lo - 1].end
  }

  console.log('Sleep periods:', sleepPeriods.length)

  // Build idle areas from backend Idle SystemEvents (new data, precise)
  const backendIdleAreas = systemEvents.value
    .filter(e => e.eventType === 'Idle' && e.durationSeconds >= 10)
    .map(e => {
      const start = parseUtcTs(e.timestamp).getTime()
      const end = start + e.durationSeconds * 1000
      if (end <= xAxisMin || start >= xAxisMax) return null
      return {
        value: [Math.max(start, xAxisMin), Math.min(end, xAxisMax), 0],
        durationMs: e.durationSeconds * 1000,
        areaType: 'idle',
      }
    })
    .filter(Boolean)

  console.log('Idle events from backend:', backendIdleAreas.length)

  // Step 3.6: Detect idle periods from gaps in the FULL timeline data (before the per-day top-20 filter).
  // Fallback for old data before backend Idle events existed.
  // Each entry has a start (timestamp) and end (timestamp + duration). A real idle gap
  // is when the END of one entry is far from the START of the next — NOT when two
  // consecutive starts are far apart (which is normal for long-duration entries like games).
  const IDLE_GAP_MS = 2 * 60 * 1000  // 2 minutes
  const MIN_IDLE_MS = 10 * 1000       // ignore sub-10s idle fragments

  const gapIdleAreas = []
  const canInferIdleGaps = !timelineMeta.value.sampled
    && !timelineMeta.value.truncated
    && backendIdleAreas.length === 0
  const fullSorted = canInferIdleGaps
    ? parsedSource
        .map(item => ({ start: item._ts, end: item._end }))
        .filter(item => !isDuringSleep(item.start))
        .sort((a, b) => a.start - b.start || a.end - b.end)
    : []

  for (const gap of inferIdleGaps(fullSorted, {
    minimumGapMs: IDLE_GAP_MS,
    rangeStart: xAxisMin,
    rangeEnd: xAxisMax,
  })) {
    if (gap.end - gap.start < MIN_IDLE_MS) continue
    gapIdleAreas.push({
      value: [gap.start, gap.end, 0],
      durationMs: gap.end - gap.start,
      areaType: 'idle',
    })
  }

  console.log('Idle areas from gaps (fallback):', gapIdleAreas.length,
    canInferIdleGaps ? '' : '(disabled for explicit or incomplete idle data)')

  // Merge both sources: backend events + gap fallback for old data without Idle events
  let idleAreas = [...backendIdleAreas, ...gapIdleAreas]

  // Step 3.7: Subtract sleep/shutdown periods from idle areas so idle never overlaps with sleep.
  // Both backend Idle events and gap-detected idle can span into sleep when the
  // system auto-suspends while the user is away. Sleep takes precedence in the display.
  {
    const sleepPeriods = systemEvents.value
      .filter(e => (e.eventType === 'Sleep' || e.eventType === 'Shutdown') && e.durationSeconds > 3)
      .map(e => ({
        start: parseUtcTs(e.timestamp).getTime(),
        end: parseUtcTs(e.timestamp).getTime() + e.durationSeconds * 1000,
      }))
      .filter(s => s.end > xAxisMin && s.start < xAxisMax)
      .sort((a, b) => a.start - b.start)

    if (sleepPeriods.length > 0) {
      const clipped = []
      for (const idle of idleAreas) {
        let segStart = idle.value[0]
        const segEnd = idle.value[1]
        for (const s of sleepPeriods) {
          if (s.end <= segStart) continue       // sleep before segment
          if (s.start >= segEnd) break           // sleep after segment (sorted, no more overlap)
          if (s.start > segStart) {
            // Non-overlapping portion before the sleep
            clipped.push({
              value: [segStart, s.start, 0],
              durationMs: s.start - segStart,
              areaType: 'idle',
            })
          }
          segStart = Math.max(segStart, s.end)   // advance past this sleep
          if (segStart >= segEnd) break          // nothing left
        }
        if (segStart < segEnd) {
          // Remaining portion after all sleep periods
          clipped.push({
            value: [segStart, segEnd, 0],
            durationMs: segEnd - segStart,
            areaType: 'idle',
          })
        }
      }
      idleAreas = clipped
    }
  }

  // Build focusedWindows + activityPeriods in a single pass
  const focusedWindows = []
  const activityPeriods = []

  for (const item of parsedTimeline) {
    const rowIdx = item._rowIdx
    if (rowIdx === undefined) continue

    if (isDuringSleep(item._ts)) continue

    const end = item._ts + item.durationSeconds * 1000
    const color = item._tag ? tagColorMap[item._tag] : colorMap[item.processName]

    const focusStyle = { color, borderColor: color, borderWidth: 0 }
    focusedWindows.push({
      name: item.displayName || displayProcessName(item.processName),
      value: [rowIdx, item._ts, end, item.durationSeconds],
      itemStyle: focusStyle,
      processName: item.processName,
      displayName: item.displayName || displayProcessName(item.processName),
      windowTitle: item.windowTitle,
      rawWindowTitle: item.rawWindowTitle || item.windowTitle,
      timestamp: item.timestamp,
      durationSeconds: item.durationSeconds,
      duringsSleep: false,
      itemColor: color,
      _ts: item._ts,
      _end: end,
      _rowIdx: rowIdx,
      _tag: item._tag || null,
    })

    activityPeriods.push({ start: item._ts, end })
  }

  console.log('Created', focusedWindows.length, 'focused window chart items')

  // In focused mode, nearby fragments are annotated as one visual run. The
  // merge gap follows the viewport scale: wide ranges get one duration per
  // cluster/day, while zoomed ranges naturally split into smaller runs.
  const durationLabels = []
  if (focusedProcess.value && focusedWindows.length) {
    const plotWidth = Math.max(240, (timelineChartRef.value?.clientWidth || 1200) - (focusedProcess.value ? 260 : 60))
    const mergeGap = Math.max(5 * 60 * 1000, (xAxisMax - xAxisMin) * (78 / plotWidth))
    const byProcess = new Map()
    for (const item of focusedWindows) {
      if (!byProcess.has(item.processName)) byProcess.set(item.processName, [])
      byProcess.get(item.processName).push(item)
    }
    for (const items of byProcess.values()) {
      items.sort((a, b) => a._ts - b._ts)
      let run = null
      for (const item of items) {
        if (run && item._ts - run.end <= mergeGap) {
          run.end = Math.max(run.end, item._end)
          run.seconds += item.durationSeconds
        } else {
          if (run) durationLabels.push(run)
          run = { row: item._rowIdx, start: item._ts, end: item._end, seconds: item.durationSeconds }
        }
      }
      if (run) durationLabels.push(run)
    }
  }

  // Merge overlapping activity periods and sort
  activityPeriods.sort((a, b) => a.start - b.start)
  const mergedActivity = []
  for (const period of activityPeriods) {
    const last = mergedActivity[mergedActivity.length - 1]
    if (last && period.start <= last.end) {
      if (period.end > last.end) last.end = period.end
    } else {
      mergedActivity.push({ start: period.start, end: period.end })
    }
  }

  console.log('Merged activity periods:', mergedActivity.length)

  // Step 4: Add background running windows (thin lines)
  const backgroundWindows = []
  const laneOffset = key => {
    let hash = 0
    for (const char of String(key)) hash = ((hash << 5) - hash + char.charCodeAt(0)) | 0
    return ((Math.abs(hash) % 7) - 3) * .07
  }

  if (isTagView || isProcessFilterView || isTitleFilterView) {
    const pushSession = (session, start, end, rowIdx, color, tag = null, processSession = false) => {
      for (const [segmentStart, segmentEnd] of subtractTimePeriods(
        Math.max(start, xAxisMin), Math.min(end, xAxisMax), sleepPeriods)) {
        if (segmentEnd <= segmentStart) continue
        backgroundWindows.push({
          name: session.displayName || displayProcessName(session.processName),
          value: [rowIdx, segmentStart, segmentEnd, 0],
          itemStyle: { color: 'transparent', borderColor: color, borderWidth: 1, opacity: isTagView ? .62 : .42 },
          processName: session.processName,
          displayName: session.displayName || displayProcessName(session.processName),
          itemColor: color,
          _tag: tag,
          _tagBackground: isTagView,
          _laneOffset: isTagView ? laneOffset(`${session.processName}:${session.processId || ''}`) : 0,
          ...(processSession ? { _processSession: session } : { _bgSession: session }),
        })
      }
    }

    for (const session of sourceWindowSessions) {
      const start = parseUtcTs(session.openTime).getTime()
      const end = session.closeTime ? parseUtcTs(session.closeTime).getTime() : xAxisMax
      if (isTagView) {
        for (const tag of session.tags || []) {
          const row = tagRowMap.get(tag)
          if (row !== undefined) pushSession(session, start, end, row, tagColorMap[tag], tag)
        }
      } else {
        const row = getProcessRow(session.processName, getDayKey(start))
        if (row !== undefined) pushSession(session, start, end, row, colorMap[session.processName])
      }
    }

    for (const session of filteredProcessSessions.value) {
      const start = parseUtcTs(session.startTime).getTime()
      const end = session.endTime ? parseUtcTs(session.endTime).getTime() : xAxisMax
      if (isTagView) {
        for (const tag of session.tags || []) {
          const row = tagRowMap.get(tag)
          if (row !== undefined) pushSession(session, start, end, row, tagColorMap[tag], tag, true)
        }
      } else if (isProcessFilterView) {
        const row = globalProcessRow.get(session.processName)
        if (row !== undefined) pushSession(session, start, end, row, colorMap[session.processName], null, true)
      }
    }
  }

  if (!isTagView && !isProcessFilterView && !isTitleFilterView && mergedActivity.length > 0) {
    const MAX_BACKGROUND = 3000
    let bgSkipped = 0
    const actMin = mergedActivity[0].start
    const actMax = mergedActivity[mergedActivity.length - 1].end

    for (const session of sourceWindowSessions) {
      if (backgroundWindows.length >= MAX_BACKGROUND) break

      const color = colorMap[session.processName]
      const sessionOpenTime = parseUtcTs(session.openTime).getTime()
      const sessionCloseTime = session.closeTime ? parseUtcTs(session.closeTime).getTime() : xAxisMax

      // Quick-reject: session entirely outside activity range
      if (sessionCloseTime <= actMin || sessionOpenTime >= actMax) continue

      // Binary-search the first activity period whose end > sessionOpenTime
      let lo = 0, hi = mergedActivity.length
      while (lo < hi) {
        const mid = (lo + hi) >> 1
        if (mergedActivity[mid].end <= sessionOpenTime) lo = mid + 1
        else hi = mid
      }

      for (let i = lo; i < mergedActivity.length; i++) {
        if (backgroundWindows.length >= MAX_BACKGROUND) break
        const ap = mergedActivity[i]
        if (ap.start >= sessionCloseTime) break  // past the session

        const segStart = Math.max(sessionOpenTime, ap.start)
        const segEnd = Math.min(sessionCloseTime, ap.end)
        if (segStart >= segEnd) continue
        const rowIdx = getProcessRow(session.processName, getDayKey(segStart))
        if (rowIdx === undefined) continue

        // Quick sleep check with early exit (sleep periods are sorted)
        let inSleep = false
        for (const s of sleepPeriods) {
          if (segStart < s.start) break
          if (segStart < s.end) { inSleep = true; break }
        }
        if (inSleep) continue

        if (rangeInDays > 1 && (segEnd - segStart) < 60 * 1000) {
          bgSkipped++
          continue
        }

        // Store raw data for shared tooltip (no per-item closure)
        const bgStyle = {
          color: 'transparent',
          borderColor: color,
          borderWidth: 1,
          opacity: 0.3,
        }
        backgroundWindows.push({
          name: session.displayName || displayProcessName(session.processName),
          value: [rowIdx, segStart, segEnd, 0],
          itemStyle: bgStyle,
          displayName: session.displayName || displayProcessName(session.processName),
          processName: session.processName,
          _bgSession: session,
          itemColor: color,
        })
      }
    }

    if (bgSkipped > 0) console.log(`Skipped ${bgSkipped} short background segments (< 60s)`)
  }

  // Current live relation instances can be distinguished by PID even when
  // their executable names are identical. Their ProcessSession spans are
  // drawn as fine lines on separate rows; historical focus rows (which do not
  // store PID) stay on the first row for that executable.
  if (focusedProcess.value && relationProcessSessions.value.length) {
    for (const session of relationProcessSessions.value) {
      const relationRow = isolatedRows.find(row => row.processId === session.processId)
      if (!relationRow) continue
      const sessionStart = Math.max(xAxisMin, parseUtcTs(session.startTime).getTime())
      const sessionEnd = Math.min(xAxisMax, session.endTime ? parseUtcTs(session.endTime).getTime() : xAxisMax)
      if (!(sessionEnd > sessionStart)) continue
      const color = colorMap[relationRow.name] || '#6B7FD7'
      backgroundWindows.push({
        name: relationRow.name,
        value: [relationRow.row, sessionStart, sessionEnd, 0],
        itemStyle: { color: 'transparent', borderColor: color, borderWidth: 1, opacity: .65 },
        processName: relationRow.name,
        itemColor: color,
        _processSession: session,
      })
    }
  }

  console.log('Created', backgroundWindows.length, 'background window chart items')

  const defaultMediaColor = getComputedStyle(document.documentElement)
    .getPropertyValue('--primary-color').trim() || '#6B7FD7'
  const mediaWindows = mediaMode.value
    ? mediaSessions.value
        .filter(item => item.playbackStatus !== 'SystemSleep')
        .map(item => {
          const start = parseUtcTs(item.startTime)?.getTime()
          const rawEnd = item.endTime ? parseUtcTs(item.endTime)?.getTime() : Date.now()
          if (!Number.isFinite(start) || !Number.isFinite(rawEnd)) return null
          const end = Math.max(start, rawEnd)
          if (end <= xAxisMin || start >= xAxisMax) return null
          return {
            name: item.title || item.appName,
            value: [mediaRowIndex, Math.max(start, xAxisMin), Math.min(end, xAxisMax), -Math.max(1, (end - start) / 1000)],
            itemStyle: { color: defaultMediaColor, borderColor: defaultMediaColor, borderWidth: 0 },
            processName: '__mediaPlayback',
            displayName: item.title || item.appName,
            windowTitle: item.artist || item.appName,
            timestamp: item.startTime,
            durationSeconds: Math.max(0, (end - start) / 1000),
            itemColor: defaultMediaColor,
            _mediaSession: item,
          }
        })
        .filter(Boolean)
    : []

  // Combine background windows (rendered first, behind) and focused windows (on top)
  const allWindows = [...backgroundWindows, ...focusedWindows, ...mediaWindows]
  // Keep bars for hover-position computation (the chart isn't init'ed until
  // below, so convertToPixel can't run yet — buildAllBarsPx() runs after
  // setOption); reset hover state.
  focusedWindowsData = focusedWindows
  backgroundWindowsData = backgroundWindows
  lastRowCount = rowCount
  hoveredProcess.value = null

  // Build sleep/shutdown area overlays from backend events (exclude Idle)
  const sleepAreas = systemEvents.value
    .filter(e => (e.eventType === 'Sleep' || e.eventType === 'Shutdown') && e.durationSeconds > 3)
    .map(e => {
      const start = parseUtcTs(e.timestamp).getTime()
      const end = start + e.durationSeconds * 1000
      // Skip if entirely outside visible range
      if (end <= xAxisMin || start >= xAxisMax) return null
      return {
        value: [Math.max(start, xAxisMin), Math.min(end, xAxisMax), 1],
        eventType: e.eventType,
        timestamp: e.timestamp,
        durationSeconds: e.durationSeconds,
        areaType: 'sleep',
      }
    })
    .filter(Boolean)

  // Dynamic time format based on range
  let timeFormatter
  if (rangeInDays <= 2) {
    // 2 days or less: show time only (HH:mm)
    timeFormatter = '{HH}:{mm}'
  } else {
    // More than 2 days: show date and time (MM-DD HH:mm)
    timeFormatter = '{MM}-{dd} {HH}:{mm}'
  }

  // Dynamic interval for axis labels (to avoid crowding)
  let axisLabelInterval
  if (rangeInDays <= 1) {
    axisLabelInterval = 'auto' // hourly or so
  } else if (rangeInDays <= 7) {
    axisLabelInterval = 'auto' // every few hours
  } else {
    axisLabelInterval = 'auto' // daily
  }

  console.log('X-axis range (query dates):', {
    from: startDate.value.toISOString(),
    to: endDate.value.toISOString(),
    rangeInDays: rangeInDays.toFixed(1),
    timeFormatter,
    xAxisMin: new Date(xAxisMin).toISOString(),
    xAxisMax: new Date(xAxisMax).toISOString()
  })

  // Debug: log first item
  if (focusedWindows.length > 0) {
    console.log('First chart item:', {
      name: focusedWindows[0].name,
      value: focusedWindows[0].value,
      itemStyle: focusedWindows[0].itemStyle
    })
  }

  if (!timelineChart) {
    // Dirty-rect rendering is unreliable for custom-series geometry after an
    // axis-only range update: bars may stay blank until a hover repaints them.
    timelineChart = echarts.init(timelineChartRef.value, null, { renderer: 'canvas', useDirtyRect: false })
    timelineChart.on('mouseover', onTimelineMouseOver)
    timelineChart.on('mouseout', onTimelineMouseOut)
    timelineChart.on('click', params => {
      if (params.seriesName !== 'windows') return
      if (params.data?._mediaSession) {
        focusMediaContext(params.data._mediaSession)
        return
      }
      openProcessActions(params.data, {
          x: params.event?.event?.clientX ?? window.innerWidth / 2,
          y: params.event?.event?.clientY ?? window.innerHeight / 2,
        })
    })
    // DOM mouseleave is the reliable way to clear hover state — ECharts'
    // series mouseout may not fire when the cursor leaves the canvas.
    timelineChartRef.value.addEventListener('mouseleave', onTimelineMouseLeave)
  }

  // Bail if a newer load started while we were working
  if (myLoadId !== undefined && myLoadId !== loadId) return

  // Get computed CSS colors (ECharts renders on Canvas — CSS variables don't work)
  const computedStyle = getComputedStyle(document.documentElement)
  const tooltipBg = computedStyle.getPropertyValue('--surface-card').trim()
  surfaceCard = tooltipBg
  const tooltipBorder = computedStyle.getPropertyValue('--primary-color').trim()
  const tooltipText = computedStyle.getPropertyValue('--text-color').trim()
  const secondaryColor = computedStyle.getPropertyValue('--secondary-color').trim()
  const borderColor = computedStyle.getPropertyValue('--border-color').trim()
  const surface200 = computedStyle.getPropertyValue('--surface-200').trim()

  function renderIdleRect(params, api) {
    const startX = api.coord([api.value(0), 0])[0]
    const endX = api.coord([api.value(1), 0])[0]
    const rowH = api.size([0, 1])[1]
    const centerY = api.coord([0, 0])[1]

    return {
      type: 'rect',
      shape: {
        x: startX,
        y: centerY - rowH * 0.4,
        width: Math.max(endX - startX, 2),
        height: rowH * 0.8,
      },
      style: {
        fill: 'rgba(128, 128, 128, 0.15)',
        stroke: secondaryColor,
        lineWidth: 2,
        lineDash: [],
      },
    }
  }

  function renderSleepRect(params, api) {
    const startX = api.coord([api.value(0), 0])[0]
    const endX = api.coord([api.value(1), 0])[0]
    const rowH = api.size([0, 1])[1]
    const centerY = api.coord([0, 0])[1]

    return {
      type: 'rect',
      shape: {
        x: startX,
        y: centerY - rowH * 0.4,
        width: Math.max(endX - startX, 2),
        height: rowH * 0.8,
      },
      style: {
        fill: 'rgba(128, 128, 128, 0.12)',
        stroke: secondaryColor,
        lineWidth: 2,
        lineDash: [8, 4],
      },
    }
  }

  function renderDurationLabel(params, api) {
    const start = api.coord([api.value(1), api.value(0)])
    const end = api.coord([api.value(2), api.value(0)])
    const rowHeight = api.size([0, 1])[1]
    return {
      type: 'text',
      silent: true,
      x: (start[0] + end[0]) / 2,
      y: start[1] - rowHeight * 0.37,
      style: {
        text: fmtShortDur(api.value(3)),
        fill: tooltipText,
        stroke: tooltipBg,
        lineWidth: 3,
        paintOrder: 'stroke',
        font: "600 12px 'Ubuntu Mono'",
        align: 'center',
        verticalAlign: 'bottom',
      },
    }
  }

  const option = {
    animation: false,
    // progressive is disabled on purpose: with ECharts 6.1, custom-series
    // chunks advance via the animation timeline, so animation: false leaves
    // large series permanently unpainted. Bars are cheap to draw.
    progressive: 0,
    progressiveThreshold: 500,
    grid: [
      {
        left: showRowLabels ? 220 : 20,
        right: 40,
        top: 40,
        bottom: 40,
        containLabel: !showRowLabels,
      },
      {
        left: showRowLabels ? 220 : 20,
        right: 40,
        height: 32,
        bottom: 4,
        containLabel: false,
      },
    ],
    xAxis: [
      {
        type: 'time',
        gridIndex: 0,
        min: xAxisMin,
        max: xAxisMax,
        axisLabel: {
          color: tooltipText,
          fontWeight: 600,
          formatter: timeFormatter,
          rotate: rangeInDays > 3 ? 15 : 0,
        },
        axisLine: {
          lineStyle: { color: borderColor, width: 2 },
        },
        splitLine: {
          lineStyle: { type: 'dashed', color: surface200 },
        },
      },
      {
        type: 'time',
        gridIndex: 1,
        min: xAxisMin,
        max: xAxisMax,
        axisLabel: { show: false },
        axisLine: { show: false },
        axisTick: { show: false },
        splitLine: { show: false },
      },
    ],
    yAxis: [
      {
        type: 'category',
        gridIndex: 0,
        data: processList,
        axisLabel: {
          show: showRowLabels,
          color: tooltipText,
          fontFamily: 'Ubuntu Mono',
          fontWeight: 600,
          width: 195,
          overflow: 'truncate',
          textBorderColor: tooltipBg,
          textBorderWidth: 2,
          margin: 13,
          interval: 0,
        },
        axisLine: { show: showRowLabels, lineStyle: { color: surface200, width: 1 } },
        axisTick: { show: showRowLabels, length: 12, lineStyle: { color: tooltipText, width: 1 } },
        splitLine: { show: showRowLabels, lineStyle: { color: surface200, type: 'dashed', width: 1 } },
      },
      {
        type: 'category',
        gridIndex: 1,
        data: [''],
        axisLabel: { show: false },
        axisLine: { show: false },
        axisTick: { show: false },
        splitLine: { show: false },
      },
    ],
    series: [
      {
        name: 'idleAreas',
        type: 'custom',
        z: 0,
        xAxisIndex: 1,
        yAxisIndex: 1,
        renderItem: renderIdleRect,
        data: idleAreas,
        tooltip: {
          formatter: (params) => {
            const data = params.data
            if (!data) return ''
            const durMin = Math.floor(data.durationMs / 60000)
            const durHr = Math.floor(durMin / 60)
            const durStr = durHr > 0 ? `${durHr}h ${durMin % 60}m` : `${durMin}m`
            return `<div style="font-weight:600;margin-bottom:4px;color:${secondaryColor};">${t('history.status.idlePeriod')}</div>
                    <div style="margin-top:4px;color:var(--surface-400);">${t('common.duration')}: ${durStr}</div>`
          },
        },
      },
      {
        name: 'sleepAreas',
        type: 'custom',
        z: 0,
        xAxisIndex: 1,
        yAxisIndex: 1,
        renderItem: renderSleepRect,
        data: sleepAreas,
        tooltip: {
          formatter: (params) => {
            const data = params.data
            if (!data) return ''
            const typeName = data.eventType === 'Sleep'
              ? t('history.status.sleepEvent')
              : t('history.status.shutdownEvent')
            return `<div style="font-weight:600;margin-bottom:4px;color:${secondaryColor};">${typeName}</div>
                    <div style="color:var(--text-color);">${toLocalTime(data.timestamp)}</div>
                    <div style="margin-top:4px;color:var(--surface-400);">${t('common.duration')}: ${fmtShortDur(data.durationSeconds)}</div>`
          },
        },
      },
      {
        name: 'windows',
        type: 'custom',
        xAxisIndex: 0,
        yAxisIndex: 0,
        renderItem: renderBar,
        encode: {
          x: [1, 2],
          y: 0,
        },
        data: allWindows,
      },
      {
        name: 'durationLabels',
        type: 'custom',
        z: 7,
        silent: true,
        xAxisIndex: 0,
        yAxisIndex: 0,
        renderItem: renderDurationLabel,
        encode: { x: [1, 2], y: 0 },
        data: durationLabels.map(item => ({ value: [item.row, item.start, item.end, item.seconds] })),
      },
    ],
    tooltip: {
      backgroundColor: tooltipBg,
      borderColor: tooltipBorder,
      borderWidth: 2,
      textStyle: {
        color: tooltipText,
        fontFamily: 'Ubuntu Mono',
      },
      formatter: (params) => {
        const data = params.data
        if (!data) return ''

        if (data._mediaSession) {
          const media = data._mediaSession
          return `<div style="font-weight:600;margin-bottom:4px;color:${tooltipText};">${escapeHtml(media.title || media.appName)}</div>
                  <div style="font-size:.9em;">${escapeHtml(media.artist || media.appName || '')}</div>
                  <div style="margin-top:4px;color:var(--primary-color);">${toLocalTime(media.startTime)} · ${fmtShortDur(data.durationSeconds)}</div>`
        }

        if (data._processSession) {
          const session = data._processSession
          return `<div style="font-weight:600;margin-bottom:4px;font-family:'Ubuntu Mono';color:${tooltipText};-webkit-text-stroke:.3px ${tooltipBg};paint-order:stroke fill;">${escapeHtml(session.displayName || session.processName)} #${session.processId}</div>
                  <div style="color:var(--surface-500);">${toLocalTime(session.startTime)} — ${session.endTime ? toLocalTime(session.endTime) : t('history.status.now')}</div>`
        }

        if (data._bgSession) {
          const s = data._bgSession
          const status = s.closeTime ? t('history.status.closed') : t('history.status.running')
          return `<div style="font-weight:600;margin-bottom:4px;font-family:'Ubuntu Mono';color:${tooltipText};-webkit-text-stroke:.3px ${tooltipBg};paint-order:stroke fill;">${escapeHtml(s.displayName || s.processName)}</div>
                  <div style="font-size:0.9em;">${escapeHtml(s.windowTitle)}</div>
                  <div style="margin-top:4px;color:var(--surface-500);">
                    ${toLocalTime(s.openTime)} - ${s.closeTime ? toLocalTime(s.closeTime) : t('history.status.now')}
                  </div>
                  <div style="font-size:0.85em;color:var(--surface-400);">${escapeHtml(t('history.status.background'))} · ${escapeHtml(status)}</div>`
        }

        return `<div style="font-weight:600;margin-bottom:4px;font-family:'Ubuntu Mono';color:${tooltipText};-webkit-text-stroke:.3px ${tooltipBg};paint-order:stroke fill;">${escapeHtml(data.displayName || data.processName)}</div>
                <div style="font-size:0.9em;">${escapeHtml(data.windowTitle)}</div>
                <div style="margin-top:4px;color:var(--primary-color);">
                  ${toLocalTime(data.timestamp)} · ${fmtShortDur(data.durationSeconds)}
                </div>`
      }
    },
  }

  console.log(`Setting option with ${focusedWindows.length} focused + ${backgroundWindows.length} bg = ${allWindows.length} total points`)

  // Bail if stale
  if (myLoadId !== undefined && myLoadId !== loadId) return

  // Reset the renderBar-recorded bar height before the render: if this data
  // has no focused bars, renderBar won't run and the stale value from the
  // previous render would mis-size the dimmer bands (row counts differ).
  lastFocusedBarH = 0
  renderedRangeMin = xAxisMin
  renderedRangeMax = xAxisMax
  timelineChart.setOption(option, { notMerge: true, lazyUpdate: false })
  timelineChart.getZr().refreshImmediately()
  // Geometry for the hover dimmer is derived lazily on first hover instead of
  // doing thousands of coordinate conversions on every range render.
  barGeometryDirty = true
  paintDimmer()
  hasRenderedTimeline.value = true

  console.log('Timeline rendered successfully')
}

function renderBar(params, api) {
  const categoryIndex = api.value(0)
  const start = api.coord([api.value(1), categoryIndex])
  const end = api.coord([api.value(2), categoryIndex])
  const durationSeconds = api.value(3)
  const isMedia = durationSeconds < 0

  // Check if this is a background window (durationSeconds = 0) or focused window
  const isBackground = durationSeconds === 0

  let rectShape
  if (isBackground) {
    const lineHeight = params.data?._tagBackground ? 0.5 : 1
    const offset = (params.data?._laneOffset || 0) * api.size([0, 1])[1]
    // Render as a thin horizontal line in the middle
    rectShape = {
      x: start[0],
      y: start[1] + offset - lineHeight / 2,
      width: Math.max(end[0] - start[0], 2),
      height: lineHeight,
    }
  } else {
    // Render as a regular bar (focused window)
    const rowHeight = api.size([0, 1])[1]
    const height = isMedia
      ? Math.max(8, Math.min(20, rowHeight * .22))
      : rowHeight * 0.6
    // Remember it so the hover dimmer paints rects of exactly this height
    // (row counts vary, so api.size is the only correct source).
    if (!isMedia) lastFocusedBarH = height
    rectShape = {
      x: start[0],
      y: start[1] - height / 2,
      width: Math.max(end[0] - start[0], 2),
      height: height,
    }
  }

  const clippedShape = echarts.graphic.clipRectByRect(rectShape, {
    x: params.coordSys.x,
    y: params.coordSys.y,
    width: params.coordSys.width,
    height: params.coordSys.height,
  })
  if (!clippedShape) return null
  return { type: 'rect', shape: clippedShape, style: api.style() }
}
</script>

<style lang="scss" scoped src="../styles/views/history.scss"></style>
