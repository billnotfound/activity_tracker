<template>
  <Teleport to="body">
    <Transition name="context-menu">
    <div v-if="visible" class="context-layer" @pointerdown.self="close">
      <div ref="contextAnchorRef" class="context-anchor" :style="positionStyle">
        <div class="context-line" aria-hidden="true"></div>
        <div class="context-panel" :class="{ editing: inlineAction }" role="menu" :aria-label="processName">
          <Transition name="menu-swap" mode="out-in">
            <form v-if="inlineAction === 'replace'" key="replace" class="inline-replace" :aria-busy="replaceState === 'loading'" @submit.prevent="submitReplacement">
              <input ref="replacementInputRef" v-model="replacement" type="text" :placeholder="t('processActions.replaceWith')" :disabled="replaceState === 'loading'" />
              <button type="submit" :disabled="!replacement.trim() || replaceState === 'loading' || replaceState === 'saving'" :aria-label="t('common.save')">
                <Transition name="icon-swap" mode="out-in">
                  <Check v-if="replaceState === 'saved'" key="saved" :size="18" />
                  <LoaderCircle v-else-if="replaceState === 'loading' || replaceState === 'saving'" key="loading" :size="18" class="spin" />
                  <ArrowRight v-else key="submit" :size="18" />
                </Transition>
              </button>
            </form>
            <div v-else-if="inlineAction === 'tags'" key="tags" class="tag-quick-picker">
              <div class="tag-chip-list" :aria-busy="tagLoading">
                <button
                  v-for="tag in availableTags"
                  :key="tag"
                  type="button"
                  class="tag-chip"
                  :class="{ selected: selectedTags.includes(tag) }"
                  :title="tag"
                  @click="toggleTag(tag)"
                >
                  <span class="tag-check-slot" aria-hidden="true">
                    <Transition name="tag-check">
                      <Check v-if="selectedTags.includes(tag)" :size="13" />
                    </Transition>
                  </span>
                  <span class="tag-chip-label">{{ tag }}</span>
                </button>
              </div>
              <div class="tag-picker-actions">
                <button type="button" class="new-tag-button" :aria-label="t('tags.addRule')" @click="startNewTag"><Plus :size="18" /></button>
                <button type="button" class="apply-tags-button" :disabled="(!selectedTags.length && !existingExactTags.length) || tagState === 'saving'" :aria-label="t('common.save')" @click="submitQuickTags">
                  <Transition name="icon-swap" mode="out-in">
                    <Check v-if="tagState === 'saved'" key="saved" :size="18" />
                    <LoaderCircle v-else-if="tagState === 'saving'" key="saving" :size="18" class="spin" />
                    <ArrowRight v-else key="submit" :size="18" />
                  </Transition>
                </button>
              </div>
            </div>
            <div v-else key="items" class="context-items">
              <button
                v-for="item in items"
                :key="item.action"
                type="button"
                role="menuitem"
                :class="{ danger: item.action === 'hide' }"
                :data-hint="item.hint"
                @click="choose(item.action)"
              >
                <component :is="item.icon" :size="17" />
                <span>{{ item.label }}</span>
              </button>
            </div>
          </Transition>
        </div>
      </div>
    </div>
    </Transition>
  </Teleport>
</template>

<script setup>
import { computed, nextTick, ref, watch } from 'vue'
import { ArrowRight, CaseUpper, Check, Clock3, EyeOff, Focus, ListTree, LoaderCircle, Plus, Tags, Type } from '@lucide/vue'
import { useI18n } from '../i18n/index.js'
import { exactTagTitlePattern, isExactTagTarget } from '../utils/tagRules.js'
import { useFloatingMenuDismiss } from '../composables/useFloatingMenuDismiss.js'

const props = defineProps({
  visible: { type: Boolean, default: false },
  page: { type: String, default: 'history' },
  processName: { type: String, default: '' },
  windowTitle: { type: String, default: '' },
  rawWindowTitle: { type: String, default: '' },
  x: { type: Number, default: 0 },
  y: { type: Number, default: 0 },
  replaceHandler: { type: Function, default: null },
})
const emit = defineEmits(['update:visible', 'choose', 'create-tag', 'tags-saved'])
const { t } = useI18n()
const inlineAction = ref('')
const replacement = ref('')
const replaceState = ref('idle')
const replacementInputRef = ref(null)
const contextAnchorRef = ref(null)
const availableTags = ref([])
const selectedTags = ref([])
const tagState = ref('idle')
const tagLoading = ref(false)
const tagRules = ref([])
const processHasTagRule = ref(false)
const existingExactTags = ref([])

const items = computed(() => {
  if (props.page === 'dashboard') return [
      { action: 'replace', icon: Type, label: t('processActions.replace'), hint: t('processActions.replaceHelp') },
      { action: 'usage', icon: ListTree, label: t('processActions.usage'), hint: t('processActions.usageHelp') },
      { action: 'tag', icon: Tags, label: t('processActions.addTag'), hint: t('processActions.addTagHelp') },
      { action: 'hide', icon: EyeOff, label: t('processActions.hide'), hint: t('processActions.hideHelp') },
    ]
  if (props.page === 'title') return [
    { action: 'isolate', icon: Focus, label: t('processActions.onlyProcess'), hint: t('processActions.onlyProcessHelp') },
    { action: 'normalize', icon: CaseUpper, label: t('processActions.normalize'), hint: t('processActions.normalizeHelp') },
    { action: 'tag', icon: Tags, label: t('processActions.addTag'), hint: t('processActions.addTagHelp') },
    { action: 'hide', icon: EyeOff, label: t('processActions.hide'), hint: t('processActions.hideHelp') },
  ]
  return [
      { action: 'isolate', icon: Focus, label: t('processActions.onlyProcess'), hint: t('processActions.onlyProcessHelp') },
      { action: 'normalize', icon: CaseUpper, label: t('processActions.normalize'), hint: t('processActions.normalizeHelp') },
      { action: 'time', icon: Clock3, label: t('processActions.time'), hint: t('processActions.timeSelectHelp') },
      { action: 'tag', icon: Tags, label: t('processActions.addTag'), hint: t('processActions.addTagHelp') },
      { action: 'hide', icon: EyeOff, label: t('processActions.hide'), hint: t('processActions.hideHelp') },
    ]
})

const positionStyle = computed(() => {
  const width = 238
  const height = props.page === 'history' ? 255 : 210
  const left = Math.max(10, Math.min(props.x + 12, window.innerWidth - width - 10))
  const top = Math.max(10, Math.min(props.y - 8, window.innerHeight - height - 10))
  return { left: `${left}px`, top: `${top}px` }
})

function close() { emit('update:visible', false) }
async function choose(action) {
  if (props.page === 'dashboard' && action === 'replace') {
    await openReplacementEditor()
    return
  }
  if (action === 'tag') {
    if (props.page === 'history' && !props.rawWindowTitle && !props.windowTitle) {
      emit('create-tag', [''])
      close()
      return
    }
    openTagPicker()
    return
  }
  emit('choose', action)
  close()
}
async function openReplacementEditor() {
  inlineAction.value = 'replace'
  replacement.value = ''
  replaceState.value = 'loading'
  try {
    const response = await fetch('/api/tags/status')
    if (response.ok) {
      const status = await response.json()
      const processRules = (status.titleRules?.rules || []).filter(rule =>
        String(rule.process || '').toLocaleLowerCase() === props.processName.toLocaleLowerCase())
      const currentRule = processRules.find(rule => String(rule.title || '').trim())
        || processRules.find(rule => String(rule.titleReplacement || '').trim())
      replacement.value = String(currentRule?.title || currentRule?.titleReplacement || '')
    }
  } catch {
    // Keep the quick editor usable when the configuration status request is unavailable.
  } finally {
    replaceState.value = 'idle'
    await nextTick()
    replacementInputRef.value?.focus()
    replacementInputRef.value?.select()
  }
}
async function openTagPicker() {
  inlineAction.value = 'tags'
  selectedTags.value = []
  existingExactTags.value = []
  availableTags.value = []
  tagRules.value = []
  tagState.value = 'idle'
  tagLoading.value = true
  try {
    const response = await fetch('/api/tags/status')
    if (!response.ok) return
    const status = await response.json()
    tagRules.value = status.tags?.rules || []
    availableTags.value = [...new Set(tagRules.value
      .map(rule => rule.tag)
      .filter(tag => tag && !tag.startsWith('_')))].sort((a, b) => a.localeCompare(b))
    existingExactTags.value = availableTags.value.filter(tag => tagRules.value.some(rule =>
      rule.tag === tag && isExactTargetRule(rule)))
    selectedTags.value = [...existingExactTags.value]
    processHasTagRule.value = tagRules.value.some(rule =>
      String(rule.process || '').toLocaleLowerCase() === props.processName.toLocaleLowerCase()
      && rule.tag && !rule.tag.startsWith('_'))
  } finally {
    tagLoading.value = false
  }
}
function toggleTag(tag) {
  selectedTags.value = selectedTags.value.includes(tag)
    ? selectedTags.value.filter(value => value !== tag)
    : [...selectedTags.value, tag]
}
function targetTitlePattern() {
  return exactTagTitlePattern(props.page === 'title' ? 'history' : props.page,
    props.rawWindowTitle, props.windowTitle)
}
function isExactTargetRule(rule) {
  return isExactTagTarget(rule, props.processName, targetTitlePattern())
}
async function submitQuickTags() {
  if (!selectedTags.value.length && !existingExactTags.value.length) return
  tagState.value = 'saving'
  try {
    const stillSelected = new Set(selectedTags.value)
    const previouslySelected = new Set(existingExactTags.value)
    const rules = tagRules.value.filter(rule => !(
      previouslySelected.has(rule.tag)
      && !stillSelected.has(rule.tag)
      && isExactTargetRule(rule)))
    const titlePattern = targetTitlePattern()
    for (const tag of selectedTags.value) {
      if (previouslySelected.has(tag)) continue
      const tagDefaults = tagRules.value.filter(rule => rule.tag === tag)
      const weight = tagDefaults.length ? Math.max(...tagDefaults.map(rule => Number(rule.weight) || 0)) : 5
      rules.push({
        tag,
        process: props.processName,
        titlePattern,
        weight,
        mode: props.page !== 'dashboard' && selectedTags.value.length === 1 && !processHasTagRule.value
          ? 'Overwrite' : 'Coexist',
      })
    }
    const response = await fetch('/api/tags/save', {
      method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(rules),
    })
    if (!response.ok) throw new Error(`API ${response.status}`)
    tagState.value = 'saved'
    emit('tags-saved')
    setTimeout(close, 520)
  } catch {
    tagState.value = 'idle'
  }
}
function startNewTag() {
  emit('create-tag', [...selectedTags.value])
  close()
}
async function submitReplacement() {
  if (!replacement.value.trim() || !props.replaceHandler) return
  replaceState.value = 'saving'
  try {
    await props.replaceHandler(replacement.value.trim())
    replaceState.value = 'saved'
    setTimeout(close, 520)
  } catch {
    replaceState.value = 'idle'
  }
}
function handleEscape() {
  if (inlineAction.value) inlineAction.value = ''
  else close()
}
watch(() => props.visible, visible => {
  if (!visible) return
  inlineAction.value = ''
  replaceState.value = 'idle'
  selectedTags.value = []
  existingExactTags.value = []
  tagState.value = 'idle'
})
useFloatingMenuDismiss({
  rootRef: contextAnchorRef,
  isVisible: () => props.visible,
  onEscape: handleEscape,
  onClose: close,
})
</script>

<style lang="scss" scoped>
.context-layer { position: fixed; inset: 0; z-index: 10000020; pointer-events: none; }
.context-anchor { --menu-line: color-mix(in srgb, var(--text-color) 82%, transparent); position: fixed; width: 238px; pointer-events: auto; }
.context-line { width: 100%; height: 2px; background: var(--menu-line); transform-origin: left; animation: lineGrow 130ms cubic-bezier(.2,.8,.2,1) both; }
.context-panel {
  border: 2px solid var(--menu-line);
  border-top: 0;
  background: var(--surface-card);
  box-shadow: 6px 7px 0 color-mix(in srgb, var(--primary-color) 18%, transparent);
  padding: 5px;
  transform-origin: top;
  animation: frameDrop 170ms cubic-bezier(.2,.8,.2,1) 110ms both;
  transition: padding 180ms ease;
  font-family: inherit;
}
.context-panel button,
.context-panel input { font: inherit; }
.context-items { display: grid; }
.context-items > button {
  position: relative;
  width: 100%;
  min-height: 40px;
  padding: 7px 9px;
  border: 0;
  border-bottom: 1px solid var(--surface-200);
  background: transparent;
  color: var(--text-color);
  display: grid;
  grid-template-columns: 24px 1fr;
  align-items: center;
  gap: 8px;
  text-align: left;
  font-weight: 600;
  cursor: pointer;
  overflow: hidden;
  opacity: 0;
  transform: translateX(-12px);
  animation: itemDrawer 180ms cubic-bezier(.2,.8,.2,1) both;

  &:last-child { border-bottom: 0; }
  &:hover, &:focus-visible { background: var(--surface-100); outline: none; }
  &.danger { color: var(--danger-color); }
  &::after {
    content: attr(data-hint);
    position: absolute;
    inset: 2px 4px 2px 34px;
    padding-left: 7px;
    border-left: 2px solid var(--primary-color);
    background: var(--surface-card);
    color: var(--text-color-secondary);
    display: flex;
    align-items: center;
    font-size: .7rem;
    line-height: 1.25;
    opacity: 0;
    transform: translateX(4px);
    pointer-events: none;
    transition: opacity 120ms ease, transform 150ms ease;
  }
  &:hover::after, &:focus-visible::after { opacity: 1; transform: translateX(0); transition-delay: 3s; }
}
@for $i from 1 through 5 {
  .context-items > button:nth-child(#{$i}) { animation-delay: #{250 + ($i - 1) * 34}ms; }
}

.inline-replace { display: grid; grid-template-columns: minmax(0, 1fr) 38px; gap: 5px; animation: inlineReveal 180ms ease both; }
.inline-replace input { min-width: 0; height: 38px; padding: 7px 9px; border: 1px solid var(--surface-300); background: var(--surface-card); color: var(--text-color); }
.inline-replace input:focus { outline: 2px solid var(--primary-color); outline-offset: -2px; }
.inline-replace button { border: 1px solid var(--menu-line); background: transparent; color: var(--text-color); display: grid; place-items: center; cursor: pointer; }
.inline-replace button:disabled { opacity: .45; cursor: not-allowed; }
.tag-quick-picker { display: grid; gap: 6px; animation: inlineReveal 180ms ease both; }
.tag-chip-list { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 5px; max-height: 158px; overflow-y: auto; overflow-x: hidden; scrollbar-gutter: stable; padding: 4px 3px; }
.tag-chip { position: relative; min-width: 0; min-height: 30px; padding: 4px 5px; border: 1px solid var(--surface-300); background: transparent; color: var(--text-color); display: grid; grid-template-columns: 14px minmax(0, 1fr); align-items: center; gap: 2px; cursor: pointer; font-size: .76rem; text-align: left; transition: border-color 150ms ease, background-color 150ms ease, color 150ms ease, box-shadow 160ms ease; }
.tag-chip:hover { border-color: var(--primary-color); }
.tag-chip.selected { border-color: var(--primary-color); background: color-mix(in srgb, var(--primary-color) 12%, var(--surface-card)); color: var(--primary-color); box-shadow: inset 0 -2px 0 color-mix(in srgb, var(--primary-color) 70%, transparent); }
.tag-chip:active { box-shadow: inset 0 0 0 2px color-mix(in srgb, var(--primary-color) 32%, transparent); }
.tag-chip-label { min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.tag-check-slot { width: 14px; height: 14px; display: grid; place-items: center; }
.tag-check-enter-active, .tag-check-leave-active { transition: opacity 120ms ease, transform 160ms cubic-bezier(.2,.8,.2,1), width 160ms ease; }
.tag-check-enter-from, .tag-check-leave-to { opacity: 0; transform: rotate(-55deg) scale(.45); width: 0; }
.tag-picker-actions { display: grid; grid-template-columns: 36px 1fr; gap: 5px; border-top: 1px solid var(--surface-200); padding-top: 5px; }
.new-tag-button, .apply-tags-button { min-height: 34px; border: 1px solid var(--menu-line); background: transparent; color: var(--text-color); display: grid; place-items: center; cursor: pointer; }
.apply-tags-button { background: var(--primary-color); color: white; }
.apply-tags-button:disabled { opacity: .45; cursor: not-allowed; }
.spin { animation: spin 700ms linear infinite; }
.menu-swap-enter-active, .menu-swap-leave-active { transition: opacity 120ms ease, transform 150ms ease; }
.menu-swap-enter-from { opacity: 0; transform: scaleX(.88); }
.menu-swap-leave-to { opacity: 0; transform: scaleX(.94); }
.icon-swap-enter-active, .icon-swap-leave-active { transition: opacity 100ms ease, transform 140ms ease; }
.icon-swap-enter-from { opacity: 0; transform: rotate(-45deg) scale(.65); }
.icon-swap-leave-to { opacity: 0; transform: rotate(45deg) scale(.65); }
@keyframes lineGrow { from { transform: scaleX(0); } to { transform: scaleX(1); } }
@keyframes frameDrop { from { opacity: 0; transform: scaleY(.02); } to { opacity: 1; transform: scaleY(1); } }
@keyframes itemDrawer { from { opacity: 0; transform: translateX(-12px); } to { opacity: 1; transform: translateX(0); } }
@keyframes inlineReveal { from { opacity: 0; transform: scaleX(.82); } to { opacity: 1; transform: scaleX(1); } }
@keyframes spin { to { transform: rotate(360deg); } }
.context-menu-leave-active { pointer-events: none; animation: layerHold 260ms linear both; }
.context-menu-leave-active .context-anchor { overflow: hidden; }
.context-menu-leave-active .context-items,
.context-menu-leave-active .inline-replace,
.context-menu-leave-active .tag-quick-picker { animation: menuContentsUp 150ms ease-in both; }
.context-menu-leave-active .context-panel { animation: menuFrameUp 210ms cubic-bezier(.4,0,.8,.2) both; overflow: hidden; }
.context-menu-leave-active .context-line { animation: lineRetract 90ms ease-in 165ms both; }
@keyframes menuContentsUp { to { opacity: 0; transform: translateY(-18px); } }
@keyframes menuFrameUp { 0% { opacity: 1; transform: scaleY(1); } 100% { opacity: 0; transform: translateY(-14px) scaleY(.04); } }
@keyframes lineRetract { to { transform: scaleX(0); } }
@keyframes layerHold { from { opacity: 1; } to { opacity: 1; } }
@media (prefers-reduced-motion: reduce) { .context-line, .context-panel, .context-items > button, .inline-replace { animation: none; opacity: 1; transform: none; } }
</style>
