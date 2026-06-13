<template>
  <div class="time-wheel">
    <div class="wheel-container">
      <div
        class="wheel-items"
        ref="wheelRef"
        @wheel.prevent="handleWheel"
        @mousedown="handleMouseDown"
      >
        <div
          v-for="(item, index) in displayItems"
          :key="index"
          class="wheel-item"
          :class="{ active: item === modelValue, dimmed: Math.abs(items.indexOf(item) - items.indexOf(modelValue)) > 2 }"
          @click="selectItem(item)"
        >
          {{ formatItem(item) }}{{ suffix }}
        </div>
      </div>
      <div class="wheel-indicator"></div>
    </div>
  </div>
</template>

<script setup>
import { ref, computed, onMounted, onUnmounted } from 'vue'

const props = defineProps({
  modelValue: {
    type: Number,
    required: true
  },
  items: {
    type: Array,
    required: true
  },
  suffix: {
    type: String,
    default: ''
  }
})

const emit = defineEmits(['update:modelValue'])

const wheelRef = ref(null)
const isDragging = ref(false)
const startY = ref(0)
const currentIndex = ref(0)

const displayItems = computed(() => {
  // Show 7 items centered on current value
  const index = props.items.indexOf(props.modelValue)
  const start = Math.max(0, index - 3)
  const end = Math.min(props.items.length, index + 4)
  return props.items.slice(start, end)
})

function formatItem(item) {
  return String(item).padStart(2, '0')
}

function selectItem(item) {
  emit('update:modelValue', item)
}

function handleWheel(event) {
  const delta = event.deltaY
  const currentIndex = props.items.indexOf(props.modelValue)

  if (delta > 0 && currentIndex < props.items.length - 1) {
    // Scroll down - next item
    emit('update:modelValue', props.items[currentIndex + 1])
  } else if (delta < 0 && currentIndex > 0) {
    // Scroll up - previous item
    emit('update:modelValue', props.items[currentIndex - 1])
  }
}

function handleMouseDown(event) {
  isDragging.value = true
  startY.value = event.clientY
  currentIndex.value = props.items.indexOf(props.modelValue)
  document.addEventListener('mousemove', handleMouseMove)
  document.addEventListener('mouseup', handleMouseUp)
}

function handleMouseMove(event) {
  if (!isDragging.value) return

  const deltaY = startY.value - event.clientY
  const itemHeight = 40 // Approximate height of each item
  const steps = Math.round(deltaY / itemHeight)

  if (steps !== 0) {
    const newIndex = Math.max(0, Math.min(props.items.length - 1, currentIndex.value + steps))
    if (newIndex !== props.items.indexOf(props.modelValue)) {
      emit('update:modelValue', props.items[newIndex])
      startY.value = event.clientY
      currentIndex.value = newIndex
    }
  }
}

function handleMouseUp() {
  isDragging.value = false
  document.removeEventListener('mousemove', handleMouseMove)
  document.removeEventListener('mouseup', handleMouseUp)
}

onUnmounted(() => {
  document.removeEventListener('mousemove', handleMouseMove)
  document.removeEventListener('mouseup', handleMouseUp)
})
</script>

<style lang="scss" scoped>
.time-wheel {
  position: relative;
  user-select: none;
}

.wheel-container {
  position: relative;
  width: 80px;
  height: 200px;
  overflow: hidden;
  background: var(--surface-card);
  border: 2px solid var(--surface-200);
}

.wheel-items {
  position: absolute;
  top: 50%;
  left: 0;
  right: 0;
  transform: translateY(-50%);
  cursor: grab;

  &:active {
    cursor: grabbing;
  }
}

.wheel-item {
  height: 40px;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 1rem;
  font-weight: 600;
  color: var(--text-color);
  transition: all 0.2s ease;
  cursor: pointer;

  &.active {
    font-size: 1.4rem;
    font-weight: 700;
    color: var(--primary-color);
    background: var(--surface-100);
  }

  &.dimmed {
    opacity: 0.4;
    font-size: 0.9rem;
  }

  &:hover:not(.active) {
    background: var(--surface-100);
  }
}

.wheel-indicator {
  position: absolute;
  top: 50%;
  left: 0;
  right: 0;
  height: 40px;
  transform: translateY(-50%);
  border-top: 2px solid var(--primary-color);
  border-bottom: 2px solid var(--primary-color);
  pointer-events: none;
  z-index: 1;
}
</style>
