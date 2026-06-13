<template>
  <Transition :name="currentTransition">
    <slot></slot>
  </Transition>
</template>

<script setup>
import { ref, computed, watch } from 'vue'
import { useRoute } from 'vue-router'
import { useTheme } from '../composables/useTheme.js'

const route = useRoute()
const { pageTransition } = useTheme()

// Route order for direction detection
const routeOrder = ['/', '/history', '/tags', '/settings']

const prevRouteIndex = ref(0)
const isForward = ref(true)

const currentTransition = computed(() => {
  if (pageTransition.value === 'geometric') {
    return 'geometric'
  }
  // For slide, determine direction
  return isForward.value ? 'slide-left' : 'slide-right'
})

watch(() => route.path, (newPath, oldPath) => {
  const newIndex = routeOrder.indexOf(newPath)
  const oldIndex = routeOrder.indexOf(oldPath || '/')
  
  if (newIndex !== -1 && oldIndex !== -1) {
    isForward.value = newIndex > oldIndex
  }
  
  prevRouteIndex.value = newIndex !== -1 ? newIndex : 0
}, { immediate: true })
</script>

<style scoped>
/* Slide left (forward) */
.slide-left-enter-active,
.slide-left-leave-active {
  transition: transform 0.3s ease, opacity 0.3s ease;
  position: absolute;
  width: 100%;
}

.slide-left-enter-from {
  transform: translateX(100%);
  opacity: 0;
}

.slide-left-leave-to {
  transform: translateX(-100%);
  opacity: 0;
}

/* Slide right (backward) */
.slide-right-enter-active,
.slide-right-leave-active {
  transition: transform 0.3s ease, opacity 0.3s ease;
  position: absolute;
  width: 100%;
}

.slide-right-enter-from {
  transform: translateX(-100%);
  opacity: 0;
}

.slide-right-leave-to {
  transform: translateX(100%);
  opacity: 0;
}

/* Geometric transition */
.geometric-enter-active,
.geometric-leave-active {
  transition: clip-path 0.5s cubic-bezier(0.68, -0.55, 0.265, 1.55), opacity 0.5s ease;
  position: absolute;
  width: 100%;
}

.geometric-enter-from {
  clip-path: polygon(0 0, 0 0, 0 100%, 0 100%);
  opacity: 0;
}

.geometric-enter-to {
  clip-path: polygon(0 0, 100% 0, 100% 100%, 0 100%);
  opacity: 1;
}

.geometric-leave-from {
  clip-path: polygon(0 0, 100% 0, 100% 100%, 0 100%);
  opacity: 1;
}

.geometric-leave-to {
  clip-path: polygon(100% 0, 100% 0, 100% 100%, 100% 100%);
  opacity: 0;
}
</style>
