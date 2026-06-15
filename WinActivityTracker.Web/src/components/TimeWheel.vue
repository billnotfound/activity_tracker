<template>
  <div class="time-wheel">
    <div class="wheel-frame" :class="{ wide, scrolling, frameless }" :data-label="label" ref="frameRef">
      <div
        class="wheel-track"
        ref="trackRef"
        :style="trackStyle"
        @wheel.prevent="onWheel"
        @mousedown="startDrag"
        @touchstart.prevent="startTouch"
      >
        <div
          v-for="item in items"
          :key="item"
          class="wheel-item"
          :class="{ active: item === renderValue }"
          @click="selectItem(item)"
        >
          {{ wide ? String(item) : formatItem(item) }}<span v-if="suffix && item === renderValue" class="suffix">{{ suffix }}</span>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, computed, watch, onUnmounted } from 'vue'

const props = defineProps({
  modelValue: { type: [Number, String], required: true },
  items: { type: Array, required: true },
  suffix: { type: String, default: '' },
  wide: { type: Boolean, default: false },
  frameless: { type: Boolean, default: false },
  label: { type: String, default: '' }
})

const emit = defineEmits(['update:modelValue', 'carry'])

const frameRef = ref(null)
const trackRef = ref(null)
const dragStartY = ref(0)
const dragAccum = ref(0)
const scrolling = ref(false)
let scrollTimer = null

const ITEM_HEIGHT = 44

// ── RAF-throttled render value ──

const renderValue = ref(props.modelValue)
let pendingValue = null
let rafScheduled = false

function scheduleRender(val) {
  pendingValue = val
  if (!rafScheduled) {
    rafScheduled = true
    requestAnimationFrame(() => {
      if (pendingValue !== null) {
        renderValue.value = pendingValue
        pendingValue = null
      }
      rafScheduled = false
    })
  }
}

watch(() => props.modelValue, (val) => scheduleRender(val))

function selectItem(item) {
  renderValue.value = item
  pendingValue = null
  emit('update:modelValue', item)
}

// ── Track positioning — always animated ──

const trackStyle = computed(() => {
  const idx = props.items.indexOf(renderValue.value)
  if (idx < 0) return {}
  const offset = idx * ITEM_HEIGHT + ITEM_HEIGHT / 2
  return {
    transform: `translateY(${-offset}px)`,
    transition: 'transform 0.08s ease-out'
  }
})

function formatItem(item) {
  return String(item)
}

function clampIdx(idx) {
  return Math.max(0, Math.min(props.items.length - 1, idx))
}

function tryChange(delta) {
  const idx = props.items.indexOf(props.modelValue)
  const newIdx = clampIdx(idx + delta)
  if (newIdx !== idx) {
    const val = props.items[newIdx]
    scheduleRender(val)
    emit('update:modelValue', val)
  } else {
    emit('carry', delta > 0 ? 1 : -1)
  }
}

function onWheel(event) {
  scrolling.value = true
  if (scrollTimer) clearTimeout(scrollTimer)
  scrollTimer = setTimeout(() => { scrolling.value = false }, 150)
  if (event.deltaY > 0) {
    tryChange(1)
  } else if (event.deltaY < 0) {
    tryChange(-1)
  }
}

// ── Vertical drag ──

function startDrag(event) {
  scrolling.value = true
  if (scrollTimer) clearTimeout(scrollTimer)
  dragStartY.value = event.clientY
  dragAccum.value = 0
  document.addEventListener('mousemove', onDrag)
  document.addEventListener('mouseup', stopDrag)
}

const DRAG_THRESHOLD = 32

function onDrag(event) {
  dragAccum.value += dragStartY.value - event.clientY
  dragStartY.value = event.clientY

  const steps = Math.round(dragAccum.value / DRAG_THRESHOLD)
  if (steps !== 0) {
    dragAccum.value -= steps * DRAG_THRESHOLD
    for (let i = 0; i < Math.abs(steps); i++) {
      tryChange(steps > 0 ? 1 : -1)
    }
  }
}

function stopDrag() {
  scrollTimer = setTimeout(() => { scrolling.value = false }, 150)
  document.removeEventListener('mousemove', onDrag)
  document.removeEventListener('mouseup', stopDrag)
}

function startTouch(event) {
  if (event.touches.length === 1) {
    dragStartY.value = event.touches[0].clientY
    dragAccum.value = 0
    document.addEventListener('touchmove', onTouchMove, { passive: false })
    document.addEventListener('touchend', stopTouch)
  }
}

function onTouchMove(event) {
  event.preventDefault()
  dragAccum.value += dragStartY.value - event.touches[0].clientY
  dragStartY.value = event.touches[0].clientY

  const steps = Math.round(dragAccum.value / DRAG_THRESHOLD)
  if (steps !== 0) {
    dragAccum.value -= steps * DRAG_THRESHOLD
    for (let i = 0; i < Math.abs(steps); i++) {
      tryChange(steps > 0 ? 1 : -1)
    }
  }
}

function stopTouch() {
  document.removeEventListener('touchmove', onTouchMove)
  document.removeEventListener('touchend', stopTouch)
}

onUnmounted(() => {
  if (scrollTimer) clearTimeout(scrollTimer)
  document.removeEventListener('mousemove', onDrag)
  document.removeEventListener('mouseup', stopDrag)
  document.removeEventListener('touchmove', onTouchMove)
  document.removeEventListener('touchend', stopTouch)
})
</script>

<style lang="scss" scoped>
.time-wheel {
  position: relative;
  user-select: none;
  contain: layout style;
}

.wheel-frame {
  position: relative;
  width: 44px;
  height: 44px;
  overflow: hidden;
  background: transparent;
  border: 3px solid var(--text-color);
  box-shadow: 3px 3px 0 color-mix(in srgb, var(--primary-color) 80%, transparent);
  cursor: grab;
  transition: transform 0.12s ease-out, box-shadow 0.12s ease-out;
  contain: layout style paint;

  &:hover {
    transform: translate(-2px, -2px);
    box-shadow: 5px 5px 0 color-mix(in srgb, var(--primary-color) 80%, transparent);
  }

  &:active {
    cursor: grabbing;
  }

  &.scrolling {
    transform: translate(0, -1px);
    box-shadow: 4px 4px 0 color-mix(in srgb, var(--primary-color) 80%, transparent);
  }

  &.frameless {
    border: none;
    box-shadow: none;
    background: transparent;
    width: auto;
    min-width: 48px;

    &:hover {
      transform: none;
      box-shadow: none;
    }

    &.scrolling {
      transform: none;
      box-shadow: none;
    }

    .wheel-item {
      padding-left: 4px;
      padding-right: 4px;
    }
  }

  &.wide {
    width: 60px;
  }

  // Hover label
  &[data-label]:hover::after {
    content: attr(data-label);
    position: absolute;
    top: -28px;
    left: 50%;
    transform: translateX(-50%);
    padding: 2px 8px;
    font-size: 0.7rem;
    font-weight: 700;
    color: var(--primary-color);
    background: var(--surface-card);
    border: 2px solid var(--text-color);
    box-shadow: 3px 3px 0 color-mix(in srgb, var(--primary-color) 80%, transparent);
    white-space: nowrap;
    z-index: 10;
    pointer-events: none;
  }
}

.wheel-track {
  position: absolute;
  left: 0;
  top: 50%;
  display: flex;
  flex-direction: column;
  align-items: center;
  width: 100%;
  will-change: transform;
  contain: layout style;
}

.wheel-item {
  flex-shrink: 0;
  width: 100%;
  height: 44px;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 0.95rem;
  font-weight: 500;
  color: var(--surface-400);
  cursor: pointer;
  transition: color 0.12s ease, font-weight 0.12s ease, opacity 0.12s ease;

  &.active {
    font-size: 1.05rem;
    font-weight: 700;
    color: var(--primary-color);
    animation: wheelFlicker 0.18s ease;

    .suffix {
      font-size: 0.65rem;
      font-weight: 400;
      margin-left: 1px;
      opacity: 0.7;
    }
  }
}

@keyframes wheelFlicker {
  0% { opacity: 0.3; }
  100% { opacity: 1; }
}

// ── Frameless: no frame chrome, only active item visible ──

.wheel-frame.frameless .wheel-item {
  width: auto;
  white-space: nowrap;
}

.wheel-frame.frameless .wheel-item:not(.active) {
  opacity: 0;
}

.wheel-frame.frameless .wheel-item.active {
  font-size: inherit;
  font-weight: inherit;
  color: inherit;
}
</style>
