<template>
  <div class="time-wheel">
    <div class="wheel-frame" :class="{ wide }" :data-label="label" ref="frameRef">
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
  modelValue: { type: Number, required: true },
  items: { type: Array, required: true },
  suffix: { type: String, default: '' },
  wide: { type: Boolean, default: false },
  label: { type: String, default: '' }
})

const emit = defineEmits(['update:modelValue', 'carry'])

const frameRef = ref(null)
const trackRef = ref(null)
const dragStartY = ref(0)
const dragAccum = ref(0)

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
  return String(item).padStart(2, '0')
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
  if (event.deltaY > 0) {
    tryChange(1)
  } else if (event.deltaY < 0) {
    tryChange(-1)
  }
}

// ── Vertical drag ──

function startDrag(event) {
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
  border: 3px solid #1a1a1a;
  box-shadow: 5px 5px 0 color-mix(in srgb, var(--primary-color) 40%, transparent);
  cursor: grab;
  contain: layout style paint;

  &:active {
    cursor: grabbing;
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
    border: 2px solid #1a1a1a;
    box-shadow: 3px 3px 0 color-mix(in srgb, var(--primary-color) 30%, transparent);
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
  color: #555;
  cursor: pointer;

  &.active {
    font-size: 1.05rem;
    font-weight: 700;
    color: var(--primary-color);

    .suffix {
      font-size: 0.65rem;
      font-weight: 400;
      margin-left: 1px;
      opacity: 0.7;
    }
  }
}
</style>
