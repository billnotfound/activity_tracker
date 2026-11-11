<template>
  <Teleport to="body">
    <div v-if="visible" class="usage-dialog-backdrop" @pointerdown.self="close">
      <section ref="dialog" class="usage-dialog" role="dialog" aria-modal="true" :aria-label="heading" tabindex="-1">
        <header>
          <div class="usage-app-icon"><img v-if="icon" :src="icon" alt="" @error="icon = ''" /><span v-else>{{ name.slice(0,1).toUpperCase() }}</span></div>
          <div><small>{{ heading }}</small><h2>{{ displayName }}</h2><span class="usage-exe">{{ name }}</span></div>
          <button class="icon-btn" :aria-label="t('common.close')" @click="close"><X :size="20" /></button>
        </header>
        <button v-if="titleMode" type="button" class="p-button p-button-secondary usage-back" @click="titleMode = false">
          <ArrowLeft :size="16" />{{ t('usage.backToProgram') }}
        </button>
        <label v-if="!titleMode && programs.length > 1" class="usage-field">{{ t('usage.program') }}
          <select v-model="name"><option v-for="app in programs" :key="app.processName" :value="app.processName">{{ app.displayName || app.processName }}</option></select>
        </label>
        <section v-if="titleMode" class="usage-title-filter">
          <h3>{{ t('usage.titleFilter') }}</h3>
          <RegexBuilder :key="`${name}:${titleText}`" v-model="titlePattern" :initial-text="titleText" @validity-change="titleValid = $event" />
          <small v-if="titleValid && !titlePattern">{{ t('usage.allTitles') }}</small>
          <div v-if="parentTags.length" class="usage-matched-tags"><b>{{ t('processActions.parentTags') }}</b> {{ parentTags.join(' · ') }}</div>
          <div v-if="matchedTags.length" class="usage-matched-tags"><b>{{ t('processActions.matchedTags') }}</b> {{ matchedTags.join(' · ') }}</div>
        </section>
        <p class="usage-help">{{ t('usage.definition') }}</p>
        <UiNotice v-if="error" tone="error">{{ error }} <button class="p-button p-button-secondary" @click="load">{{ t('common.retry') }}</button></UiNotice>
        <div class="usage-totals" :aria-busy="loading">
          <article><span>{{ t('usage.today') }}</span><strong>{{ loading ? '…' : filterDirty ? '—' : formatDuration(totals.today) }}</strong><small>{{ todayRange }}</small></article>
          <article><span>{{ t('usage.week') }}</span><strong>{{ loading ? '…' : filterDirty ? '—' : formatDuration(totals.week) }}</strong><small>{{ weekRange }}</small></article>
        </div>
        <form @submit.prevent="load">
          <h3>{{ t('usage.custom') }}</h3>
          <div class="usage-range-fields">
            <label class="usage-field">{{ t('history.range.start') }}<input v-model="from" type="datetime-local" required /></label>
            <label class="usage-field">{{ t('history.range.end') }}<input v-model="to" type="datetime-local" required /></label>
          </div>
          <button class="p-button" :disabled="loading || !validRange || (titleMode && !titleValid)">{{ t('usage.calculate') }}</button>
          <p v-if="!validRange" class="usage-invalid">{{ t('history.range.endAfterStart') }}</p>
          <div class="usage-custom-total" aria-live="polite"><span>{{ t('usage.total') }}</span><strong>{{ loading ? '…' : customDirty || filterDirty ? '—' : formatDuration(totals.custom) }}</strong></div>
          <small v-if="customDirty">{{ t('usage.recalculate') }}</small>
          <div v-if="titleMode && !filterDirty && !customDirty" class="usage-matching-titles">
            <h3>{{ t('usage.matchedTitles') }}</h3>
            <p v-if="!titleResults.length">{{ t('usage.noMatchingTitles') }}</p>
            <ul v-else><li v-for="item in titleResults" :key="item.title"><span>{{ item.title }}</span><b>{{ formatDuration(item.totalSeconds) }}</b></li></ul>
          </div>
        </form>
        <p class="usage-help">{{ t('usage.boundary', { time: boundary }) }}</p>
      </section>
    </div>
  </Teleport>
</template>
<script setup>
import { ref, computed, watch, inject, onBeforeUnmount, nextTick } from 'vue'
import { ArrowLeft, X } from '@lucide/vue'
import { useI18n } from '../i18n/index.js'
import { useActivityDay } from '../composables/useActivityDay.js'
import { useModalFocus } from '../composables/useModalFocus.js'
import { activityDayStart, summaryQuery } from '../utils/activityDay.js'
import { addLocalDays, toLocalDatetimeString } from '../utils/date.js'
import { fmtShortDur } from '../utils/time.js'
import UiNotice from './UiNotice.vue'
import RegexBuilder from './RegexBuilder.vue'

const props = defineProps({
  visible: Boolean,
  titleScope: Boolean,
  process: { type: Object, default: () => ({}) },
  programs: { type: Array, default: () => [] },
  rangeStart: Date,
  rangeEnd: Date,
})
const emit = defineEmits(['update:visible'])
const { t } = useI18n()
const apiBase = inject('apiBase', '')
const { effectiveDayStart } = useActivityDay()
const dialog = ref(null)
const name = ref('')
const icon = ref('')
const from = ref('')
const to = ref('')
const titleMode = ref(false)
const titleText = ref('')
const titlePattern = ref('')
const titleValid = ref(true)
const titleResults = ref([])
const parentTags = ref([])
const matchedTags = ref([])
const loading = ref(false)
const error = ref('')
const totals = ref({ today: null, week: null, custom: null })
const todayRange = ref('')
const weekRange = ref('')
const computedRange = ref('')
const computedFilter = ref('')
let controller
let revision = 0
let reloadTimer
const close = () => emit('update:visible', false)
useModalFocus(dialog, () => props.visible, close)
const displayName = computed(() => props.programs.find(p => p.processName === name.value)?.displayName || (props.process.processName === name.value && props.process.displayName) || name.value)
const heading = computed(() => t(titleMode.value ? 'usage.titleScoped' : 'usage.title'))
const boundary = computed(() => `${String(Math.floor(effectiveDayStart.value/60)).padStart(2,'0')}:${String(effectiveDayStart.value%60).padStart(2,'0')}`)
const validRange = computed(() => Number.isFinite(+new Date(from.value)) && +new Date(to.value) > +new Date(from.value))
const customDirty = computed(() => computedRange.value !== `${from.value}|${to.value}`)
const filterKey = computed(() => JSON.stringify([name.value, titleMode.value, titlePattern.value, titleValid.value]))
const filterDirty = computed(() => computedFilter.value !== filterKey.value)
const formatDuration = value => value == null ? '—' : fmtShortDur(value)
const rangeText = (start,end) => `${start.toLocaleString()} — ${end.toLocaleString()}`
async function load() {
  clearTimeout(reloadTimer)
  controller?.abort()
  const id = ++revision
  if (!props.visible || !name.value || !validRange.value || (titleMode.value && !titleValid.value)) {
    loading.value = false
    return
  }
  controller = new AbortController()
  const signal = controller.signal
  loading.value = true
  error.value = ''
  totals.value = { today: null, week: null, custom: null }
  const now = new Date()
  const day = activityDayStart(now, effectiveDayStart.value)
  const week = addLocalDays(day, -6)
  const rangeKey = `${from.value}|${to.value}`
  const currentFilter = filterKey.value
  todayRange.value = rangeText(day,now); weekRange.value = rangeText(week,now)
  const program = name.value
  const scoped = titleMode.value
  const pattern = titlePattern.value ? `regex:${titlePattern.value}` : ''
  const total = async (start,end) => {
    const query = new URLSearchParams(summaryQuery(start, end))
    const endpoint = scoped ? 'title-breakdown' : 'range'
    if (scoped) {
      query.set('process', program)
      if (pattern) query.set('titlePattern', pattern)
    }
    const r = await fetch(`${apiBase}/api/summary/${endpoint}?${query}`, { signal })
    if (!r.ok) throw new Error(`HTTP ${r.status}`)
    const data = await r.json()
    const rows = scoped ? data.titles || [] : (data.items || []).filter(item => item.processName?.toLowerCase() === program.toLowerCase())
    return { seconds: rows.reduce((sum,item) => sum + Number(item.totalSeconds || 0), 0), data }
  }
  try {
    const [todayTotal, weekTotal, customTotal] = await Promise.all([total(day,now),total(week,now),total(new Date(from.value),new Date(to.value))])
    if (id !== revision || currentFilter !== filterKey.value) return
    totals.value = { today: todayTotal.seconds, week: weekTotal.seconds, custom: customTotal.seconds }
    titleResults.value = customTotal.data.titles || []
    parentTags.value = customTotal.data.processTags || []
    matchedTags.value = customTotal.data.matchedTags || []
    computedRange.value = rangeKey
    computedFilter.value = currentFilter
  } catch(e) { if (e.name !== 'AbortError' && id === revision) error.value = t('settings.operationFailed',{message:e.message}) }
  finally { if (id === revision) loading.value = false }
}
function scheduleLoad(delay = 180) {
  clearTimeout(reloadTimer)
  revision++
  controller?.abort()
  if (props.visible) reloadTimer = setTimeout(load, delay)
}
watch([() => props.visible, () => props.process, () => props.titleScope], async ([open]) => {
  clearTimeout(reloadTimer)
  revision++
  controller?.abort()
  if (!open) { loading.value = false; return }
  const now = new Date()
  from.value = toLocalDatetimeString(props.rangeStart || activityDayStart(now,effectiveDayStart.value))
  to.value = toLocalDatetimeString(props.rangeEnd && props.rangeEnd < now ? props.rangeEnd : now)
  name.value = props.process.processName || props.programs[0]?.processName || ''
  titleText.value = props.process.rawWindowTitle || props.process.windowTitle || ''
  titleMode.value = props.titleScope
  titlePattern.value = ''
  titleValid.value = true
  titleResults.value = []
  parentTags.value = []
  matchedTags.value = []
  await nextTick()
  if (props.visible) scheduleLoad(0)
}, { immediate: true })
watch([name, effectiveDayStart, titleMode, titlePattern, titleValid], () => scheduleLoad())
watch([name, () => props.visible], async ([program, open], _, cleanup) => {
  icon.value = ''
  if (!program || !open) return
  const abort = new AbortController()
  cleanup(() => abort.abort())
  try {
    const r = await fetch(`${apiBase}/api/icons/${encodeURIComponent(program)}`,{signal:abort.signal})
    if (!r.ok) return
    const data = await r.json()
    if (!abort.signal.aborted) icon.value = data.iconData ? `data:image/png;base64,${data.iconData}` : ''
  } catch { /* The app initial remains visible when no icon is available. */ }
})
onBeforeUnmount(() => { revision++; controller?.abort(); clearTimeout(reloadTimer) })
</script>
<style scoped>
.usage-dialog-backdrop { position:fixed; inset:0; z-index:2200; background:#0006; display:grid; place-items:center; padding:20px; }
.usage-dialog { width:min(620px,100%); max-height:calc(100dvh - 40px); overflow:auto; border:3px solid var(--text-color); background:var(--surface-card); color:var(--text-color); box-shadow:7px 7px 0 var(--primary-color); padding:24px; }
header { display:grid; grid-template-columns:80px minmax(0,1fr) 36px; align-items:center; gap:16px; margin-bottom:18px; }
h2 { font-size:1.5rem; overflow-wrap:anywhere; margin:4px 0; } h3 { margin-bottom:12px; font-size:1rem; }
.usage-app-icon { width:80px; height:80px; display:grid; place-items:center; background:var(--surface-100); border:2px solid var(--border-color); font-size:2.5rem; font-weight:700; }
.usage-app-icon img { width:64px; height:64px; object-fit:contain; }
.usage-exe, small, .usage-help { color:var(--text-color-secondary); font-size:.8rem; overflow-wrap:anywhere; line-height:1.6; }
.usage-help { margin:12px 0; }
.usage-back { margin-bottom: 16px; }
.usage-title-filter { display: grid; gap: 10px; margin: 16px 0; }
.usage-matched-tags { font-size: .8rem; line-height: 1.5; }
.usage-matching-titles { margin-top: 16px; }
.usage-matching-titles ul { list-style: none; padding: 0; max-height: 220px; overflow-y: auto; }
.usage-matching-titles li { display: flex; justify-content: space-between; gap: 12px; padding: 8px 0; border-bottom: 1px solid var(--surface-200); font-size: .85rem; }
.usage-matching-titles li span { overflow-wrap: anywhere; }
.usage-totals { display:grid; grid-template-columns:1fr 1fr; gap:12px; margin:20px 0; }
.usage-totals article { border-top:4px solid var(--primary-color); padding:14px; background:var(--surface-100); display:grid; gap:8px; }
.usage-totals article + article { border-color:var(--secondary-color); }
strong { font-size:1.7rem; font-variant-numeric:tabular-nums; }
.usage-field { display:grid; gap:6px; font-size:.85rem; }
input, select { width:100%; min-width:0; padding:8px; border:2px solid var(--surface-300); background:var(--surface-card); color:var(--text-color); font:inherit; }
.usage-range-fields { display:grid; grid-template-columns:1fr 1fr; gap:12px; margin-bottom:14px; }
.usage-custom-total { display:flex; justify-content:space-between; align-items:center; margin-top:18px; padding:14px 0; border-block:2px solid var(--surface-200); }
.usage-invalid { color:var(--danger-color); }
@media(max-width:520px) { .usage-dialog { padding:16px; } header { grid-template-columns:64px minmax(0,1fr) 36px; gap:10px; } .usage-app-icon { width:64px;height:64px; } .usage-app-icon img { width:52px;height:52px; } .usage-totals,.usage-range-fields { grid-template-columns:1fr; } }
</style>
