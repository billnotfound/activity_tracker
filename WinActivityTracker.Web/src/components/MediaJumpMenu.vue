<template>
  <Teleport to="body">
    <Transition name="media-menu">
      <div v-if="visible" class="media-menu-layer" @pointerdown.self="close">
        <div ref="menuRef" class="media-menu-anchor" :style="positionStyle">
          <div class="media-menu-line"></div>
          <div class="media-menu-panel" role="dialog" :aria-label="t('dashboard.media.openHistoryQuestion')">
            <strong>{{ t('dashboard.media.openHistoryQuestion') }}</strong>
            <span class="media-menu-title">{{ item?.title || item?.appName }}</span>
            <div class="media-menu-actions">
              <button type="button" @click="close"><X :size="16" />{{ t('common.cancel') }}</button>
              <button type="button" class="confirm" @click="confirm"><ArrowRight :size="16" />{{ t('dashboard.media.openHistory') }}</button>
            </div>
          </div>
        </div>
      </div>
    </Transition>
  </Teleport>
</template>

<script setup>
import { computed, ref } from 'vue'
import { ArrowRight, X } from '@lucide/vue'
import { useI18n } from '../i18n/index.js'
import { useFloatingMenuDismiss } from '../composables/useFloatingMenuDismiss.js'

const props = defineProps({
  visible: { type: Boolean, default: false },
  item: { type: Object, default: null },
  x: { type: Number, default: 0 },
  y: { type: Number, default: 0 },
})
const emit = defineEmits(['update:visible', 'confirm'])
const { t } = useI18n()
const menuRef = ref(null)
const positionStyle = computed(() => ({
  left: `${Math.max(10, Math.min(props.x + 10, window.innerWidth - 286))}px`,
  top: `${Math.max(10, Math.min(props.y - 8, window.innerHeight - 150))}px`,
}))

function close() { emit('update:visible', false) }
function confirm() { emit('confirm', props.item); close() }
useFloatingMenuDismiss({
  rootRef: menuRef,
  isVisible: () => props.visible,
  onEscape: close,
  onClose: close,
})
</script>

<style scoped>
.media-menu-layer { position: fixed; inset: 0; z-index: 10000020; pointer-events: none; }
.media-menu-anchor { position: fixed; width: 276px; pointer-events: auto; font-family: inherit; }
.media-menu-line { height: 2px; background: var(--text-color); transform-origin: left; animation: mediaLine 120ms ease-out both; }
.media-menu-panel { padding: 11px; border: 2px solid var(--text-color); border-top: 0; background: var(--surface-card); color: var(--text-color); box-shadow: 5px 6px 0 color-mix(in srgb, var(--primary-color) 20%, transparent); display: grid; gap: 8px; transform-origin: top; animation: mediaPanel 170ms cubic-bezier(.2,.8,.2,1) 90ms both; }
.media-menu-title { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; color: var(--text-color-secondary); font-size: .78rem; }
.media-menu-actions { display: grid; grid-template-columns: 1fr 1.35fr; gap: 6px; }
.media-menu-actions button { min-height: 34px; padding: 5px 8px; border: 1px solid var(--surface-300); background: transparent; color: var(--text-color); font: inherit; display: inline-flex; align-items: center; justify-content: center; gap: 5px; cursor: pointer; }
.media-menu-actions .confirm { border-color: var(--primary-color); background: var(--primary-color); color: white; }
@keyframes mediaLine { from { transform: scaleX(0); } }
@keyframes mediaPanel { from { opacity: 0; transform: scaleY(.05); } }
.media-menu-leave-active { pointer-events: none; animation: mediaHold 260ms linear both; }
.media-menu-leave-active .media-menu-anchor { overflow: hidden; }
.media-menu-leave-active .media-menu-panel { animation: mediaPanelUp 210ms cubic-bezier(.4,0,.8,.2) both; overflow: hidden; }
.media-menu-leave-active .media-menu-panel > strong,
.media-menu-leave-active .media-menu-title,
.media-menu-leave-active .media-menu-actions { animation: mediaContentsUp 150ms ease-in both; }
.media-menu-leave-active .media-menu-line { animation: mediaLineRetract 90ms ease-in 165ms both; }
@keyframes mediaContentsUp { to { opacity: 0; transform: translateY(-16px); } }
@keyframes mediaPanelUp { to { opacity: 0; transform: translateY(-12px) scaleY(.04); } }
@keyframes mediaLineRetract { to { transform: scaleX(0); } }
@keyframes mediaHold { from { opacity: 1; } to { opacity: 1; } }
@media (prefers-reduced-motion: reduce) { .media-menu-line, .media-menu-panel { animation: none; } }
</style>
