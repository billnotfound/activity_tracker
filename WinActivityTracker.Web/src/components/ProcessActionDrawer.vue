<template>
  <Teleport to="body">
    <Transition name="drawer-fade">
      <div v-if="visible" class="action-shell" @pointerdown.self="close">
        <aside class="action-drawer" role="dialog" aria-modal="true" :aria-label="t('processActions.title')">
          <div class="origin-line" aria-hidden="true"><span></span></div>
          <header class="drawer-header">
            <div class="process-mark" :style="{ '--process-color': processColor }">
              <img v-if="displayIcon" :src="displayIcon" alt="" />
              <span v-else>{{ processInitial }}</span>
            </div>
            <div class="drawer-heading">
              <h2>{{ processName }}</h2>
            </div>
            <button class="drawer-close" type="button" :aria-label="t('common.close')" @click="close">
              <X :size="20" />
            </button>
          </header>

          <div class="drawer-content">
            <button v-if="view !== 'menu'" class="back-button" type="button" @click="goBack">
              <ArrowLeft :size="16" /> {{ t('processActions.back') }}
            </button>

            <div v-if="message" class="drawer-message" :class="messageType">{{ message }}</div>

            <div v-if="view === 'menu'" class="action-menu">
              <button type="button" :data-hint="t('processActions.usageHelp')" @click="openUsage">
                <ListTree :size="20" />
                <span><b>{{ t('processActions.usage') }}</b></span>
                <ChevronRight :size="18" />
              </button>
              <button v-if="allowIsolate" type="button" :data-hint="t('processActions.onlyProcessHelp')" @click="isolate">
                <Focus :size="20" />
                <span><b>{{ t('processActions.onlyProcess') }}</b></span>
                <ChevronRight :size="18" />
              </button>
              <button type="button" :data-hint="t('processActions.normalizeHelp')" @click="view = 'normalize'">
                <CaseUpper :size="20" />
                <span><b>{{ t('processActions.normalize') }}</b></span>
                <ChevronRight :size="18" />
              </button>
              <button type="button" :data-hint="t('processActions.addTagHelp')" @click="view = 'tag'">
                <Tags :size="20" />
                <span><b>{{ t('processActions.addTag') }}</b></span>
                <ChevronRight :size="18" />
              </button>
              <button type="button" :data-hint="hasAnomalies ? t('processActions.timeDetected') : t('processActions.timeClear')" @click="view = 'time'">
                <Clock3 :size="20" />
                <span>
                  <b>{{ t('processActions.time') }}</b>
                </span>
                <ChevronRight :size="18" />
              </button>
              <button type="button" class="danger-action" :data-hint="t('processActions.hideHelp')" @click="view = 'hide'">
                <EyeOff :size="20" />
                <span><b>{{ t('processActions.hide') }}</b></span>
                <ChevronRight :size="18" />
              </button>
            </div>

            <section v-else-if="view === 'usage'" class="drawer-section usage-section">
              <div class="range-summary">{{ rangeSummary }}</div>

              <div v-if="visibleParents.length || relations.children?.length" class="relations-card">
                <div v-for="parent in visibleParents" :key="`p-${parent.processId}`" class="relation-line parent-line">
                  <span>{{ t('processActions.parent') }}</span><i></i><b>{{ parent.name }} <small>#{{ parent.processId }}</small></b>
                </div>
                <div v-for="child in relations.children" :key="`c-${child.processId}`" class="relation-line">
                  <span>{{ t('processActions.children') }}</span><i></i><b>{{ child.name }} <small>#{{ child.processId }}</small></b>
                </div>
              </div>

              <div class="usage-list" :aria-busy="usageLoading">
                <div v-if="usageLoading" class="usage-empty">{{ t('common.loading') }}</div>
                <article v-for="item in usageItems" v-else :key="`${item.timestamp}-${item.windowTitle}`" class="usage-item">
                  <time>{{ formatDateTime(item.timestamp) }}</time>
                  <b>{{ item.windowTitle || processName }}</b>
                  <span>{{ fmtShortDur(item.durationSeconds) }}</span>
                </article>
                <div v-if="!usageLoading && !usageItems.length" class="usage-empty">{{ t('history.noData') }}</div>
              </div>

              <button type="button" class="primary-action" @click="goHistory(true)">
                <History :size="17" /> {{ t('processActions.openHistory') }}
              </button>
            </section>

            <section v-else-if="view === 'replace'" class="drawer-section">
              <div class="field-block readonly-field">
                <label>{{ t('processActions.processField') }}</label>
                <div>{{ processName }}</div>
              </div>
              <div class="field-block">
                <label for="replacement-title">{{ t('processActions.replaceWith') }}</label>
                <input id="replacement-title" v-model="canonicalTitle" class="action-input" type="text" :placeholder="t('processActions.canonicalPlaceholder')" />
              </div>
              <button type="button" class="primary-action" :disabled="saving || !canonicalTitle.trim()" @click="saveReplacement">
                <Save :size="17" /> {{ t('common.save') }}
              </button>
            </section>

            <section v-else-if="view === 'normalize'" class="drawer-section">
              <div class="field-block readonly-field">
                <label>{{ t('processActions.processField') }}</label>
                <div>{{ processName }}</div>
              </div>
              <div class="field-block">
                <label for="canonical-title">{{ t('processActions.canonicalTitle') }}</label>
                <input id="canonical-title" v-model="canonicalTitle" class="action-input" type="text" :placeholder="t('processActions.canonicalPlaceholder')" />
              </div>
              <RegexBuilder v-model="titleRegex" :initial-text="windowTitle" />
              <button type="button" class="primary-action" :disabled="saving || !canonicalTitle || !titleRegex" @click="saveTitleRule">
                <Save :size="17" /> {{ t('common.save') }}
              </button>
            </section>

            <section v-else-if="view === 'tag'" class="drawer-section">
              <div class="field-block readonly-field">
                <label>{{ t('processActions.processField') }}</label>
                <div>{{ processName }}</div>
              </div>

              <div class="tag-drafts">
                <article v-for="(draft, index) in tagDrafts" :key="draft.id" class="tag-draft-card">
                  <div class="tag-input-row">
                    <input v-model="draft.name" class="action-input" type="text" :placeholder="t('tags.placeholder.tag')" />
                    <button type="button" class="tag-row-action" :aria-label="index === tagDrafts.length - 1 ? t('tags.addRule') : t('common.delete')" @click="index === tagDrafts.length - 1 ? addTagRow() : removeTagRow(index)">
                      <Transition name="icon-morph" mode="out-in">
                        <Plus v-if="index === tagDrafts.length - 1" key="plus" :size="18" />
                        <X v-else key="remove" :size="18" />
                      </Transition>
                    </button>
                  </div>

                  <div class="parameter-grid">
                    <label>
                      <span>{{ t('tags.column.weight') }}</span>
                      <input v-model.number="draft.weight" class="action-input" type="number" />
                    </label>
                    <label>
                      <span>{{ t('tags.column.mode') }}</span>
                      <span class="mode-toggle" role="radiogroup">
                        <button type="button" :class="{ active: draft.mode === 'Coexist' }" @click="setDraftMode(draft, 'Coexist')">{{ t('processActions.mode.coexist') }}</button>
                        <button type="button" :class="{ active: draft.mode === 'Overwrite' }" @click="setDraftMode(draft, 'Overwrite')">{{ t('processActions.mode.overwrite') }}</button>
                      </span>
                    </label>
                  </div>

                  <button v-if="draft.mode === 'Coexist' && !draft.useTitle" type="button" class="title-match-toggle" @click="draft.useTitle = true">
                    <Plus :size="15" /> {{ t('processActions.optionalTitleMatch') }}
                  </button>
                  <div v-if="draft.mode === 'Overwrite' || draft.useTitle" class="draft-matcher">
                    <div class="draft-matcher-title">
                      <span>{{ draft.mode === 'Overwrite' ? t('processActions.requiredTitleMatch') : t('processActions.optionalTitleMatch') }}</span>
                      <button v-if="draft.mode === 'Coexist'" type="button" :aria-label="t('common.close')" @click="draft.useTitle = false; draft.titleRegex = ''"><X :size="15" /></button>
                    </div>
                    <RegexBuilder v-model="draft.titleRegex" :initial-text="draft.seedTitle" />
                    <small v-if="draft.mode === 'Overwrite' && !draft.titleRegex" class="draft-error">{{ t('processActions.overwriteNeedsTitle') }}</small>
                  </div>
                </article>
              </div>

              <button type="button" class="primary-action" :disabled="saving || !validTagDrafts" @click="saveTags">
                <Save :size="17" /> {{ t('common.save') }}
              </button>
              <button type="button" class="secondary-action" @click="goHistory(false)">
                <History :size="17" /> {{ t('processActions.openHistory') }}
              </button>
            </section>

            <section v-else-if="view === 'time'" class="drawer-section time-section">
              <div class="time-state" :class="{ clear: !hasAnomalies }">
                <CircleCheck v-if="!hasAnomalies" :size="28" />
                <TriangleAlert v-else :size="28" />
                <div>
                  <b>{{ hasAnomalies ? t('processActions.timeStatusDetected') : t('processActions.timeStatusClear') }}</b>
                </div>
              </div>
              <button type="button" class="primary-action" @click="selectTime">
                <MousePointer2 :size="17" /> {{ t('processActions.selectTime') }}
              </button>
            </section>

            <section v-else-if="view === 'hide'" class="drawer-section hide-section">
              <div class="danger-warning">
                <TriangleAlert :size="30" />
                <div>
                  <b>{{ t('processActions.hideWarningTitle') }}</b>
                  <p>{{ t('processActions.hideWarningBody', { process: processName }) }}</p>
                </div>
              </div>
              <label class="confirm-check">
                <input v-model="hideConfirmed" type="checkbox" />
                <span>{{ t('processActions.hideConfirm') }}</span>
              </label>
              <button type="button" class="danger-confirm" :disabled="saving || !hideConfirmed" @click="hideProcess">
                <EyeOff :size="17" /> {{ t('processActions.hide') }}
              </button>
            </section>
          </div>
        </aside>
      </div>
    </Transition>
  </Teleport>
</template>

<script setup>
import { computed, inject, onMounted, onUnmounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import {
  ArrowLeft, CaseUpper, ChevronRight, CircleCheck, Clock3, EyeOff, Focus,
  History, ListTree, MousePointer2, Plus, Save, Tags, TriangleAlert, X,
} from '@lucide/vue'
import RegexBuilder from './RegexBuilder.vue'
import { useI18n } from '../i18n/index.js'
import { fmtShortDur, parseUtcTs, toLocalDatetimeString } from '../utils/time.js'

const props = defineProps({
  visible: { type: Boolean, default: false },
  process: { type: Object, default: () => ({}) },
  rangeStart: { type: Date, default: null },
  rangeEnd: { type: Date, default: null },
  allowIsolate: { type: Boolean, default: false },
  initialView: { type: String, default: 'menu' },
  returnToPopup: { type: Boolean, default: false },
  initialTagNames: { type: Array, default: () => [] },
  processColor: { type: String, default: 'var(--primary-color)' },
  icon: { type: String, default: '' },
})
const emit = defineEmits(['update:visible', 'isolate', 'select-time', 'changed', 'return-to-menu'])
const apiBase = inject('apiBase')
const router = useRouter()
const { t } = useI18n()

const view = ref('menu')
const usageItems = ref([])
const usageLoading = ref(false)
const relations = ref({ parents: [], children: [] })
const anomalyItems = ref([])
const message = ref('')
const messageType = ref('success')
const canonicalTitle = ref('')
const titleRegex = ref('')
const tagDrafts = ref([])
let tagDraftId = 0
const hideConfirmed = ref(false)
const saving = ref(false)
const fetchedIcon = ref('')

const processName = computed(() => props.process?.processName || props.process?.name || '')
const windowTitle = computed(() => props.process?.windowTitle || props.process?.title || '')
const processInitial = computed(() => (processName.value[0] || '?').toUpperCase())
const displayIcon = computed(() => props.icon || fetchedIcon.value)
const validTagDrafts = computed(() => {
  const active = tagDrafts.value.filter(draft => draft.name.trim())
  return active.length > 0 && active.every(draft => draft.mode !== 'Overwrite' || !!draft.titleRegex)
})
const hasAnomalies = computed(() => anomalyItems.value.some(item => !['Ignored', 'Reverted'].includes(item.status)))
const visibleParents = computed(() => (relations.value.parents || (relations.value.parent ? [relations.value.parent] : []))
  .filter(parent => parent.name.toLocaleLowerCase() !== processName.value.toLocaleLowerCase()))
const rangeStart = computed(() => props.rangeStart || new Date(Date.now() - 24 * 60 * 60 * 1000))
const rangeEnd = computed(() => props.rangeEnd || new Date())
const rangeSummary = computed(() => `${rangeStart.value.toLocaleString()} — ${rangeEnd.value.toLocaleString()}`)

watch(() => props.visible, async value => {
  if (!value) return
  view.value = props.initialView || 'menu'
  message.value = ''
  canonicalTitle.value = props.initialView === 'replace' ? '' : (windowTitle.value || '')
  titleRegex.value = ''
  const initialNames = props.initialTagNames.length ? props.initialTagNames : ['']
  tagDrafts.value = initialNames.map(name => createTagDraft(String(name || '')))
  if (!tagDrafts.value.some(draft => !draft.name)) tagDrafts.value.push(createTagDraft(''))
  hideConfirmed.value = false
  fetchedIcon.value = ''
  await Promise.all([loadRelations(), loadAnomalies(), loadIcon()])
})

function close() { emit('update:visible', false) }
function goBack() {
  if (props.returnToPopup) {
    emit('return-to-menu')
    close()
  } else {
    view.value = 'menu'
  }
}
function onKeydown(event) {
  if (event.key !== 'Escape' || !props.visible) return
  if (view.value !== 'menu') goBack()
  else close()
}
onMounted(() => window.addEventListener('keydown', onKeydown))
onUnmounted(() => window.removeEventListener('keydown', onKeydown))
function createTagDraft(name = '') {
  return { id: ++tagDraftId, name, weight: 5, mode: 'Coexist', useTitle: false, titleRegex: '', seedTitle: windowTitle.value || '' }
}
function addTagRow() { tagDrafts.value.push(createTagDraft('')) }
function removeTagRow(index) { tagDrafts.value.splice(index, 1) }
function setDraftMode(draft, mode) {
  draft.mode = mode
  if (mode === 'Overwrite') draft.useTitle = true
}

function showMessage(value, type = 'success') {
  message.value = value
  messageType.value = type
}

async function loadRelations() {
  relations.value = { parents: [], children: [] }
  if (!processName.value) return
  try {
    const r = await fetch(`${apiBase}/api/processes/relations?process=${encodeURIComponent(processName.value)}`)
    if (r.ok) relations.value = await r.json()
  } catch { /* process may no longer be running */ }
}

async function loadAnomalies() {
  try {
    const r = await fetch(`${apiBase}/api/time-anomalies?limit=50`)
    if (r.ok) anomalyItems.value = (await r.json()).items || []
  } catch { anomalyItems.value = [] }
}

async function loadIcon() {
  if (props.icon || !processName.value) return
  try {
    const at = rangeEnd.value.toISOString()
    const response = await fetch(`${apiBase}/api/icons/${encodeURIComponent(processName.value)}?at=${encodeURIComponent(at)}`)
    if (!response.ok) return
    const data = await response.json()
    if (data.iconData) fetchedIcon.value = `data:image/png;base64,${data.iconData}`
  } catch { /* fallback initial remains visible */ }
}

async function openUsage() {
  view.value = 'usage'
  usageLoading.value = true
  usageItems.value = []
  const query = new URLSearchParams({
    from: toLocalDatetimeString(rangeStart.value),
    to: toLocalDatetimeString(rangeEnd.value),
    limit: '500',
    process: processName.value,
  })
  try {
    const r = await fetch(`${apiBase}/api/windows/timeline?${query}`)
    if (r.ok) {
      const result = await r.json()
      usageItems.value = result.data || []
    }
  } finally {
    usageLoading.value = false
  }
}

function formatDateTime(value) {
  const date = parseUtcTs(value)
  return date ? date.toLocaleString() : '—'
}

function goHistory(isolateProcess) {
  close()
  router.push({
    path: '/history',
    query: {
      process: processName.value,
      isolate: isolateProcess ? '1' : undefined,
      from: toLocalDatetimeString(rangeStart.value),
      to: toLocalDatetimeString(rangeEnd.value),
    },
  })
}

function isolate() {
  emit('isolate', processName.value)
  close()
}

function selectTime() {
  emit('select-time', { ...props.process, processName: processName.value })
  close()
}

async function getConfig() {
  const r = await fetch(`${apiBase}/api/tags/status`)
  if (!r.ok) throw new Error(`API ${r.status}`)
  return await r.json()
}

async function saveTitleRule() {
  saving.value = true
  message.value = ''
  try {
    const status = await getConfig()
    // The server stores one title rule per process. Replace that exact process
    // entry case-insensitively while leaving every unrelated rule untouched.
    const kept = (status.titleRules?.rules || []).filter(rule =>
      String(rule.process || '').toLocaleLowerCase() !== processName.value.toLocaleLowerCase())
    kept.push({
      process: processName.value,
      title: null,
      titleRegex: titleRegex.value,
      titleReplacement: canonicalTitle.value.trim(),
      // Real-time rewriting can permanently discard title detail. The quick
      // action stays display-only; the high-risk switch remains in Settings.
      applyOnWrite: false,
    })
    const r = await fetch(`${apiBase}/api/title-rules/save`, {
      method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(kept),
    })
    if (!r.ok) throw new Error(`API ${r.status}`)
    showMessage(t('processActions.saved'))
    emit('changed', 'title')
    if (!props.returnToPopup) view.value = 'menu'
  } catch (e) {
    showMessage(t('tags.error.backendUnreachable', { message: e.message }), 'error')
  } finally { saving.value = false }
}

async function saveReplacement() {
  saving.value = true
  message.value = ''
  try {
    const status = await getConfig()
    const kept = (status.titleRules?.rules || []).filter(rule =>
      String(rule.process || '').toLocaleLowerCase() !== processName.value.toLocaleLowerCase())
    kept.push({
      process: processName.value,
      title: canonicalTitle.value.trim(),
      titleRegex: null,
      titleReplacement: null,
      applyOnWrite: false,
    })
    const r = await fetch(`${apiBase}/api/title-rules/save`, {
      method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(kept),
    })
    if (!r.ok) throw new Error(`API ${r.status}`)
    showMessage(t('processActions.saved'))
    emit('changed', 'title')
    if (!props.returnToPopup) view.value = 'menu'
  } catch (e) {
    showMessage(t('tags.error.backendUnreachable', { message: e.message }), 'error')
  } finally { saving.value = false }
}

async function saveTags() {
  saving.value = true
  message.value = ''
  try {
    const status = await getConfig()
    const drafts = tagDrafts.value.filter(draft => draft.name.trim())
    const names = new Set(drafts.map(draft => draft.name.trim()))
    const rules = (status.tags?.rules || []).filter(rule => !(
      names.has(rule.tag)
      && String(rule.process || '').toLocaleLowerCase() === processName.value.toLocaleLowerCase()))
    for (const draft of drafts) {
      rules.push({
        tag: draft.name.trim(),
        process: processName.value,
        titlePattern: (draft.mode === 'Overwrite' || draft.useTitle) && draft.titleRegex
          ? `regex:${draft.titleRegex}` : null,
        weight: Number(draft.weight) || 0,
        mode: draft.mode,
      })
    }
    const r = await fetch(`${apiBase}/api/tags/save`, {
      method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(rules),
    })
    if (!r.ok) throw new Error(`API ${r.status}`)
    showMessage(t('processActions.saved'))
    emit('changed', 'tags')
    if (!props.returnToPopup) view.value = 'menu'
  } catch (e) {
    showMessage(t('tags.error.backendUnreachable', { message: e.message }), 'error')
  } finally { saving.value = false }
}

async function hideProcess() {
  saving.value = true
  message.value = ''
  try {
    const status = await getConfig()
    const rules = [...(status.tags?.rules || [])]
    const exists = rules.some(rule => rule.tag === '__hidden'
      && String(rule.process || '').toLocaleLowerCase() === processName.value.toLocaleLowerCase()
      && !rule.titlePattern)
    if (!exists) rules.push({ tag: '__hidden', process: processName.value, titlePattern: null, weight: 100, mode: 'Overwrite' })
    const r = await fetch(`${apiBase}/api/tags/save`, {
      method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(rules),
    })
    if (!r.ok) throw new Error(`API ${r.status}`)
    emit('changed', 'hidden')
    close()
  } catch (e) {
    showMessage(t('tags.error.backendUnreachable', { message: e.message }), 'error')
  } finally { saving.value = false }
}
</script>

<style lang="scss" scoped>
.action-shell {
  position: fixed;
  inset: 0;
  z-index: 2100;
  background: color-mix(in srgb, var(--surface-ground) 35%, transparent);
  backdrop-filter: blur(2px);
  display: flex;
  justify-content: flex-end;
}

.action-drawer {
  position: relative;
  width: min(440px, 100vw);
  height: 100dvh;
  max-height: 100dvh;
  background: var(--surface-card);
  border-left: 3px solid var(--border-color);
  box-shadow: -14px 0 0 color-mix(in srgb, var(--primary-color) 16%, transparent);
  overflow: hidden;
  display: grid;
  grid-template-rows: auto minmax(0, 1fr);
}

.origin-line {
  position: absolute;
  top: 86px;
  right: 100%;
  width: min(18vw, 180px);
  height: 3px;
  background: var(--primary-color);

  span { position: absolute; left: -7px; top: -6px; width: 15px; height: 15px; border: 3px solid var(--primary-color); background: var(--surface-card); }
}

.drawer-header {
  min-height: 104px;
  padding: 22px 20px;
  border-bottom: 2px solid var(--surface-200);
  display: grid;
  grid-template-columns: 48px minmax(0, 1fr) 36px;
  gap: 14px;
  align-items: center;
}

.process-mark {
  width: 48px;
  height: 48px;
  border: 3px solid var(--process-color);
  background: color-mix(in srgb, var(--process-color) 15%, var(--surface-card));
  display: grid;
  place-items: center;
  color: var(--process-color);
  font: 700 1.1rem 'Ubuntu Mono', monospace;

  img { width: 30px; height: 30px; object-fit: contain; }
}

.drawer-heading {
  min-width: 0;
  h2 { margin: 2px 0; color: var(--text-color); font: 700 1.15rem 'Ubuntu Mono', monospace; overflow-wrap: anywhere; -webkit-text-stroke: .35px var(--surface-card); paint-order: stroke fill; }
}

.drawer-close,
.back-button {
  border: 2px solid var(--surface-200);
  background: transparent;
  color: var(--text-color);
  cursor: pointer;
}

.drawer-close { width: 36px; height: 36px; display: grid; place-items: center; }
.drawer-close:hover { border-color: var(--primary-color); }

.drawer-content { overflow: auto; overscroll-behavior: contain; padding: 18px 20px 28px; }
.back-button { min-height: 34px; padding: 5px 9px; display: inline-flex; align-items: center; gap: 6px; margin-bottom: 14px; }

.drawer-message {
  padding: 10px 12px;
  margin-bottom: 14px;
  border-left: 4px solid var(--success-color);
  background: color-mix(in srgb, var(--success-color) 12%, var(--surface-card));
  color: var(--text-color);
  &.error { border-left-color: var(--danger-color); }
}

.action-menu { display: grid; gap: 9px; }
.action-menu > button {
  position: relative;
  width: 100%;
  min-height: 52px;
  padding: 10px 12px;
  border: 2px solid var(--surface-200);
  background: var(--surface-card);
  color: var(--text-color);
  display: grid;
  grid-template-columns: 28px minmax(0, 1fr) 20px;
  align-items: center;
  gap: 10px;
  text-align: left;
  cursor: pointer;
  transition: transform 150ms ease, border-color 150ms ease, background 150ms ease;

  span { display: grid; }
  b { font-size: 0.93rem; }
  &:hover { transform: translateX(-4px); border-color: var(--primary-color); background: var(--surface-100); }
  &.danger-action { color: var(--danger-color); border-color: color-mix(in srgb, var(--danger-color) 45%, var(--surface-200)); }

  &::after {
    content: attr(data-hint);
    position: absolute;
    z-index: 4;
    left: 38px;
    right: 24px;
    top: 3px;
    bottom: 3px;
    padding: 6px 8px;
    border: 0;
    border-left: 2px solid var(--primary-color);
    background: var(--surface-card);
    color: var(--text-color-secondary);
    font-size: .74rem;
    line-height: 1.35;
    display: flex;
    align-items: center;
    opacity: 0;
    transform: translateX(4px);
    pointer-events: none;
    transition: opacity 120ms ease 0s, transform 120ms ease 0s;
  }
  &:hover::after, &:focus-visible::after { opacity: 1; transform: translateX(0); transition-delay: 3s; }
}

.drawer-section { display: grid; gap: 16px; }
.section-kicker { color: var(--text-color-secondary); font-size: 0.74rem; text-transform: uppercase; letter-spacing: 0.08em; }
.range-summary { color: var(--text-color); font: 600 0.86rem 'Ubuntu Mono', monospace; }

.usage-list {
  max-height: min(48vh, 420px);
  overflow-y: auto;
  border: 2px solid var(--surface-200);
  scrollbar-gutter: stable;
}

.usage-item {
  display: grid;
  grid-template-columns: minmax(0, 1fr) auto;
  gap: 4px 10px;
  padding: 10px 12px;
  border-bottom: 1px solid var(--surface-200);
  color: var(--text-color);

  time { grid-column: 1 / -1; color: var(--text-color-secondary); font-size: 0.74rem; }
  b { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  span { color: var(--primary-color); font: 600 0.82rem 'Ubuntu Mono', monospace; }
}
.usage-empty { padding: 28px 12px; text-align: center; color: var(--text-color-secondary); }

.relations-card { display: grid; gap: 8px; padding: 11px 12px; background: var(--surface-100); border-left: 3px solid var(--accent-color); }
.relation-line { display: grid; grid-template-columns: auto 28px minmax(0, 1fr); align-items: center; gap: 7px; font-size: 0.78rem; color: var(--text-color-secondary); }
.relation-line i { height: 1px; background: var(--accent-color); position: relative; }
.relation-line i::after { content: ''; position: absolute; right: 0; top: -3px; border-left: 5px solid var(--accent-color); border-top: 3px solid transparent; border-bottom: 3px solid transparent; }
.relation-line b { color: var(--text-color); overflow-wrap: anywhere; }

.field-block { display: grid; gap: 6px; }
.field-block > label,
.parameter-grid label > span { color: var(--text-color); font-size: 0.84rem; font-weight: 600; }
.readonly-field > div { padding: 10px; background: var(--surface-100); border-left: 4px solid var(--primary-color); font-family: 'Ubuntu Mono', monospace; }
.action-input { width: 100%; min-height: 40px; border: 2px solid var(--surface-200); padding: 8px 10px; background: var(--surface-card); color: var(--text-color); }
.action-input:focus { outline: none; border-color: var(--primary-color); }

.tag-drafts { display: grid; gap: 12px; }
.tag-draft-card { display: grid; gap: 11px; padding: 11px; border: 2px solid var(--surface-200); background: var(--surface-card); }
.tag-input-row { display: grid; grid-template-columns: 1fr 36px; gap: 8px; }
.tag-row-action { border: 2px solid var(--surface-200); background: transparent; color: var(--text-color); cursor: pointer; display: grid; place-items: center; overflow: hidden; transition: border-color 160ms ease, color 160ms ease, transform 160ms ease; }
.tag-row-action:hover { border-color: var(--primary-color); color: var(--primary-color); transform: rotate(3deg); }
.icon-morph-enter-active, .icon-morph-leave-active { transition: opacity 130ms ease, transform 180ms cubic-bezier(.2,.8,.2,1); }
.icon-morph-enter-from { opacity: 0; transform: rotate(-90deg) scale(.55); }
.icon-morph-leave-to { opacity: 0; transform: rotate(90deg) scale(.55); }

.matcher-details { border: 2px solid var(--surface-200); padding: 10px 12px; }
.matcher-details summary { cursor: pointer; color: var(--text-color); font-weight: 600; }
.matcher-details[open] summary { margin-bottom: 14px; }
.parameter-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 12px; }
.parameter-grid label { display: grid; gap: 6px; }
.confirm-check { display: flex; align-items: flex-start; gap: 9px; color: var(--text-color); font-size: 0.88rem; }
.confirm-check input { width: 18px; height: 18px; accent-color: var(--primary-color); flex: 0 0 auto; }

.mode-toggle { display: grid; grid-template-columns: 1fr 1fr; min-height: 40px; border: 2px solid var(--surface-200); }
.mode-toggle button { border: 0; background: transparent; color: var(--text-color); font-weight: 600; cursor: pointer; }
.mode-toggle button + button { border-left: 2px solid var(--surface-200); }
.mode-toggle button.active { background: var(--primary-color); color: white; }
.title-match-toggle { justify-self: start; min-height: 32px; padding: 5px 8px; border: 1px solid var(--surface-300); background: transparent; color: var(--text-color-secondary); display: inline-flex; align-items: center; gap: 6px; cursor: pointer; }
.draft-matcher { display: grid; gap: 9px; padding-top: 9px; border-top: 1px solid var(--surface-200); }
.draft-matcher-title { display: flex; align-items: center; justify-content: space-between; color: var(--text-color); font-size: .8rem; font-weight: 600; }
.draft-matcher-title button { width: 28px; height: 28px; border: 1px solid var(--surface-300); background: transparent; color: var(--text-color); display: grid; place-items: center; cursor: pointer; }
.draft-error { color: var(--danger-color); font-size: .75rem; }

.primary-action,
.secondary-action,
.danger-confirm {
  min-height: 42px;
  border: 2px solid var(--primary-color);
  padding: 8px 12px;
  background: var(--primary-color);
  color: white;
  font-weight: 700;
  cursor: pointer;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 8px;
}
.secondary-action { background: transparent; color: var(--text-color); }
.danger-confirm { background: var(--danger-color); border-color: var(--danger-color); }
.primary-action:disabled,
.danger-confirm:disabled { opacity: 0.45; cursor: not-allowed; }

.time-state,
.danger-warning { display: grid; grid-template-columns: 34px minmax(0, 1fr); gap: 12px; padding: 16px; border: 2px solid var(--warning-color); color: var(--warning-color); }
.time-state.clear { border-color: var(--success-color); color: var(--success-color); }
.time-state b,
.danger-warning b { display: block; margin-bottom: 5px; }
.time-state p,
.danger-warning p { color: var(--text-color-secondary); line-height: 1.45; font-size: 0.86rem; }
.danger-warning { border-color: var(--danger-color); color: var(--danger-color); background: color-mix(in srgb, var(--danger-color) 8%, var(--surface-card)); }

.drawer-fade-enter-active,
.drawer-fade-leave-active { transition: background 220ms ease; }
.drawer-fade-enter-active .action-drawer,
.drawer-fade-leave-active .action-drawer { transition: transform 260ms cubic-bezier(.2,.8,.2,1); }
.drawer-fade-enter-from,
.drawer-fade-leave-to { background: transparent; }
.drawer-fade-enter-from .action-drawer,
.drawer-fade-leave-to .action-drawer { transform: translateX(105%); }

@media (max-width: 600px) {
  .origin-line { display: none; }
  .action-drawer { width: 100vw; border-left: 0; }
  .parameter-grid { grid-template-columns: 1fr; }
}

@media (prefers-reduced-motion: reduce) {
  .drawer-fade-enter-active,
  .drawer-fade-leave-active,
  .drawer-fade-enter-active .action-drawer,
  .drawer-fade-leave-active .action-drawer { transition: none; }
}
</style>
