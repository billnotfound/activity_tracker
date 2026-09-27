<template>
  <span ref="rootRef" class="setting-help">
    <button
      type="button"
      class="setting-help-trigger"
      :aria-label="text"
      :aria-expanded="open"
      @click.stop.prevent="togglePinned"
      @pointerenter="scheduleOpen"
      @pointerleave="scheduleClose"
      @focus="show"
      @keydown.esc.stop="close"
    >
      <CircleHelp :size="14" aria-hidden="true" />
    </button>
    <Transition name="help-pop">
      <span v-if="open" class="setting-help-popover" role="tooltip" @pointerenter="cancelClose" @pointerleave="scheduleClose">
        {{ text }}
      </span>
    </Transition>
  </span>
</template>

<script setup>
import { onMounted, onUnmounted, ref } from 'vue'
import { CircleHelp } from '@lucide/vue'

defineProps({ text: { type: String, required: true } })

const rootRef = ref(null)
const open = ref(false)
const pinned = ref(false)
let openTimer = null
let closeTimer = null

function clearTimers() {
  clearTimeout(openTimer)
  clearTimeout(closeTimer)
}

function show() {
  clearTimers()
  open.value = true
}

function close() {
  clearTimers()
  pinned.value = false
  open.value = false
}

function scheduleOpen() {
  clearTimeout(closeTimer)
  clearTimeout(openTimer)
  openTimer = setTimeout(() => { open.value = true }, 220)
}

function scheduleClose() {
  clearTimeout(openTimer)
  if (pinned.value) return
  closeTimer = setTimeout(() => { open.value = false }, 120)
}

function cancelClose() {
  clearTimeout(closeTimer)
}

function togglePinned() {
  pinned.value = !pinned.value
  open.value = pinned.value
}

function onDocumentPointerDown(event) {
  if (!rootRef.value?.contains(event.target)) close()
}

onMounted(() => document.addEventListener('pointerdown', onDocumentPointerDown, true))
onUnmounted(() => {
  clearTimers()
  document.removeEventListener('pointerdown', onDocumentPointerDown, true)
})
</script>

<style scoped>
.setting-help {
  position: relative;
  display: inline-grid;
  place-items: center;
  margin-left: 3px;
  vertical-align: middle;
}

.setting-help-trigger {
  width: 24px;
  height: 24px;
  padding: 0;
  border: 0;
  background: transparent;
  color: var(--text-color-secondary);
  cursor: help;
  display: grid;
  place-items: center;
  transition: color 140ms ease, transform 140ms ease;
}

.setting-help-trigger:hover,
.setting-help-trigger:focus-visible,
.setting-help-trigger[aria-expanded="true"] {
  color: var(--primary-color);
  transform: translateY(-1px);
  outline: none;
}

.setting-help-popover {
  position: absolute;
  z-index: 80;
  left: -10px;
  top: calc(100% + 7px);
  width: min(280px, calc(100vw - 36px));
  padding: 9px 11px;
  border: 2px solid var(--text-color);
  background: var(--surface-card);
  color: var(--text-color);
  box-shadow: 4px 4px 0 color-mix(in srgb, var(--primary-color) 24%, transparent);
  font-size: .78rem;
  font-weight: 500;
  line-height: 1.45;
  text-align: left;
  white-space: normal;
}

.help-pop-enter-active,
.help-pop-leave-active { transition: opacity 130ms ease, transform 160ms cubic-bezier(.2,.8,.2,1); }
.help-pop-enter-from,
.help-pop-leave-to { opacity: 0; transform: translateY(-5px) scale(.97); }

@media (prefers-reduced-motion: reduce) {
  .setting-help-trigger,
  .help-pop-enter-active,
  .help-pop-leave-active { transition-duration: .01ms; }
}
</style>
