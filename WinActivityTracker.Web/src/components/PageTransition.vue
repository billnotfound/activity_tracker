<template>
  <Transition v-if="pageTransition !== 'geometric'" :name="slideTransition" mode="out-in">
    <div :key="routeKey" class="page-transition-layer">
      <component :is="component" />
    </div>
  </Transition>

  <template v-else>
    <div
      v-for="layer in geometryLayers"
      :key="layer.key"
      :class="layer.current ? 'page-transition-layer' : ['page-preload-layer', { revealing: stageRevealing }]"
      :style="layer.current ? undefined : {
        '--stage-clip': stageRevealClip,
        '--stage-scroll': `${stageScrollOffset}px`,
      }"
      :aria-hidden="layer.current ? undefined : 'true'"
    >
      <component :is="layer.component" />
    </div>
  </template>

  <div
    v-if="geometryVisible"
    :key="geometryKey"
    class="geometry-overlay"
    :class="{ finishing: geometryFinishing, burst: geometryExitVariant === 'burst' }"
    :style="geometryStyle"
    :data-exit-variant="geometryExitVariant"
    aria-hidden="true"
  >
    <svg class="geometry-shape" viewBox="0 0 100 100" role="presentation">
      <defs>
        <mask id="geometry-center-cutout" maskUnits="userSpaceOnUse" x="0" y="0" width="100" height="100">
          <rect width="100" height="100" fill="white" />
          <polygon :points="shapePoints.core" fill="black" />
        </mask>
      </defs>
      <polygon class="shape-edge" :points="shapePoints.edge" mask="url(#geometry-center-cutout)" />
    </svg>
  </div>
</template>

<script setup>
import { ref, shallowRef, computed, watch, nextTick, onBeforeUnmount } from 'vue'
import { useRoute } from 'vue-router'
import { useTheme } from '../composables/useTheme.js'

const props = defineProps({
  component: { type: [Object, Function], required: true },
  routeKey: { type: String, required: true },
})

const route = useRoute()
const { pageTransition } = useTheme()
const routeOrder = ['/', '/history', '/tags', '/settings']
const isForward = ref(true)
const geometryLayers = shallowRef([{ key: props.routeKey, component: props.component, current: true }])
const displayedRouteKey = ref(props.routeKey)
const geometryVisible = ref(false)
const geometryFinishing = ref(false)
const geometryKey = ref(0)
const geometryDirection = ref({ x: '0vw', y: '-145vh' })
const geometryShape = ref('triangle')
const geometryRotation = ref(17)
const geometryExitVariant = ref('fade')
const stageRevealing = ref(false)
const stageRevealClip = ref('polygon(50% 50%, 50% 50%, 50% 50%)')
const stageScrollOffset = ref(0)
let transitionStartedAt = 0
let transitionRevision = 0
let finishTimer = null
let removeTimer = null
let readyTimer = null

const points = {
  triangle: {
    edge: '50,3 98,91 2,91',
    core: '50,15 87,83 13,83',
    vertices: [[50, 15], [87, 83], [13, 83]],
  },
  star: {
    edge: '50,2 61,36 97,36 68,57 79,92 50,71 21,92 32,57 3,36 39,36',
    core: '50,14 58,42 86,42 63,59 72,82 50,67 28,82 37,59 14,42 42,42',
    vertices: [[50, 14], [58, 42], [86, 42], [63, 59], [72, 82],
      [50, 67], [28, 82], [37, 59], [14, 42], [42, 42]],
  },
}

const slideTransition = computed(() => isForward.value ? 'slide-left' : 'slide-right')
const shapePoints = computed(() => points[geometryShape.value])
const geometryStyle = computed(() => ({
  '--from-x': geometryDirection.value.x,
  '--from-y': geometryDirection.value.y,
  '--shape-rotation': `${geometryRotation.value}deg`,
  '--burst-scale': (Math.hypot(window.innerWidth, window.innerHeight)
    / (Math.min(window.innerWidth, window.innerHeight) * 0.4714) * 1.6).toFixed(2),
}))

function randomRotation(shape) {
  const symmetry = shape === 'star' ? 72 : 120
  let degrees
  do {
    degrees = Math.random() * 360
    const remainder = degrees % symmetry
    if (Math.min(remainder, symmetry - remainder) > 10) break
  } while (true)
  return Number(degrees.toFixed(2))
}

function revealClipForShape() {
  const host = document.querySelector('.page-container')?.getBoundingClientRect()
  const centerX = window.innerWidth / 2 - (host?.left || 0)
  const centerY = window.innerHeight / 2 - (host?.top || 0) - stageScrollOffset.value
  const size = Math.min(window.innerWidth, window.innerHeight) * 0.4714 * 1.1
  const radians = geometryRotation.value * Math.PI / 180
  const cosine = Math.cos(radians)
  const sine = Math.sin(radians)
  return `polygon(${points[geometryShape.value].vertices.map(([x, y]) => {
    const localX = (x - 50) / 100 * size
    const localY = (y - 50) / 100 * size
    const px = centerX + localX * cosine - localY * sine
    const py = centerY + localX * sine + localY * cosine
    return `${px.toFixed(2)}px ${py.toFixed(2)}px`
  }).join(', ')})`
}

function beginGeometry() {
  transitionRevision += 1
  transitionStartedAt = performance.now()
  stageScrollOffset.value = window.scrollY
  geometryLayers.value = geometryLayers.value.filter(layer => layer.current)
  geometryShape.value = Math.random() < 0.5 ? 'triangle' : 'star'
  geometryExitVariant.value = Math.random() < 0.5 ? 'fade' : 'burst'
  const direction = Math.random() * Math.PI * 2
  geometryDirection.value = {
    x: `${(Math.cos(direction) * 145).toFixed(2)}vw`,
    y: `${(Math.sin(direction) * 145).toFixed(2)}vh`,
  }
  geometryRotation.value = randomRotation(geometryShape.value)
  geometryKey.value += 1
  geometryFinishing.value = false
  stageRevealing.value = false
  geometryVisible.value = true
  clearTimeout(finishTimer)
  clearTimeout(removeTimer)
  clearTimeout(readyTimer)
}

function afterTwoFrames(callback) {
  requestAnimationFrame(() => requestAnimationFrame(callback))
}

async function stageNextComponent(nextComponent) {
  const revision = transitionRevision
  geometryLayers.value = [
    ...geometryLayers.value.filter(layer => layer.current),
    { key: props.routeKey, component: nextComponent, current: false },
  ]
  await nextTick()
  afterTwoFrames(() => awaitStageReady(revision))
}

function awaitStageReady(revision) {
  if (revision !== transitionRevision || !geometryLayers.value.some(layer => !layer.current)) return
  const stage = document.querySelector('.page-preload-layer')
  const signal = stage?.querySelector('[data-page-ready]')
  if (signal?.getAttribute('data-page-ready') === 'false'
      && performance.now() - transitionStartedAt < 6000) {
    readyTimer = setTimeout(() => awaitStageReady(revision), 50)
    return
  }
  afterTwoFrames(() => {
    if (revision !== transitionRevision) return
    const remaining = window.matchMedia('(prefers-reduced-motion: reduce)').matches
      ? 0 : Math.max(0, 560 - (performance.now() - transitionStartedAt))
    finishTimer = setTimeout(() => finishGeometry(revision), remaining)
  })
}

function resetScroll() {
  window.scrollTo({ top: 0, left: 0, behavior: 'auto' })
  document.documentElement.scrollTop = 0
  document.body.scrollTop = 0
}

function finishGeometry(revision) {
  if (revision !== transitionRevision || !geometryLayers.value.some(layer => !layer.current)) return
  stageRevealClip.value = revealClipForShape()
  stageRevealing.value = true
  geometryFinishing.value = true
  const reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches
  const exitDuration = reducedMotion ? 0 : 300
  removeTimer = setTimeout(() => {
    if (revision !== transitionRevision) return
    const next = geometryLayers.value.find(layer => !layer.current)
    if (!next) return
    geometryLayers.value = [{ ...next, current: true }]
    displayedRouteKey.value = next.key
    stageRevealing.value = false
    resetScroll()
    geometryVisible.value = false
    geometryFinishing.value = false
  }, exitDuration)
}

watch(() => route.path, (newPath, oldPath) => {
  const newIndex = routeOrder.indexOf(newPath)
  const oldIndex = routeOrder.indexOf(oldPath || '/')
  if (newIndex !== -1 && oldIndex !== -1) isForward.value = newIndex > oldIndex
  if (oldPath && pageTransition.value === 'geometric' && newPath !== oldPath) beginGeometry()
}, { immediate: true })

watch(() => props.component, (nextComponent) => {
  if (!nextComponent) return
  if (pageTransition.value !== 'geometric') {
    geometryLayers.value = [{ key: props.routeKey, component: nextComponent, current: true }]
    displayedRouteKey.value = props.routeKey
    return
  }
  if (props.routeKey === displayedRouteKey.value) return
  stageNextComponent(nextComponent)
})

watch(pageTransition, value => {
  if (value !== 'geometric') {
    transitionRevision += 1
    clearTimeout(finishTimer)
    clearTimeout(removeTimer)
    clearTimeout(readyTimer)
    geometryLayers.value = [{ key: props.routeKey, component: props.component, current: true }]
    displayedRouteKey.value = props.routeKey
    stageRevealing.value = false
    geometryVisible.value = false
  }
})

onBeforeUnmount(() => {
  clearTimeout(finishTimer)
  clearTimeout(removeTimer)
  clearTimeout(readyTimer)
})
</script>

<style scoped>
.page-transition-layer { width: 100%; }

.page-preload-layer {
  position: absolute;
  inset: 0;
  width: 100%;
  visibility: hidden;
  pointer-events: none;
  contain: layout paint style;
  z-index: 2;
  transform: translateY(var(--stage-scroll));
}

.page-preload-layer.revealing {
  visibility: visible;
  clip-path: var(--stage-clip);
}

.slide-left-enter-active,
.slide-left-leave-active,
.slide-right-enter-active,
.slide-right-leave-active {
  transition: transform 0.3s ease, opacity 0.3s ease;
}
.slide-left-enter-from { transform: translateX(30%); opacity: 0; }
.slide-left-leave-to { transform: translateX(-30%); opacity: 0; }
.slide-right-enter-from { transform: translateX(-30%); opacity: 0; }
.slide-right-leave-to { transform: translateX(30%); opacity: 0; }

.geometry-overlay {
  position: fixed;
  inset: 0;
  z-index: 2500;
  pointer-events: none;
  display: grid;
  place-items: center;
  overflow: hidden;
}

.geometry-shape {
  width: min(47.14vw, 47.14vh);
  height: min(47.14vw, 47.14vh);
  overflow: visible;
  transform-origin: center;
  filter: drop-shadow(0 14px 28px color-mix(in srgb, var(--text-color) 24%, transparent));
  animation: geometry-arrive 0.56s cubic-bezier(0.2, 0.78, 0.2, 1) both;
}

.shape-edge { fill: var(--primary-color); }

.geometry-overlay.finishing .geometry-shape {
  animation: geometry-finish 0.3s ease both;
}

.geometry-overlay.finishing .shape-edge {
  animation: edge-finish 0.3s ease both;
}

.geometry-overlay.finishing.burst .geometry-shape {
  animation: geometry-burst 0.3s cubic-bezier(0.62, 0.08, 0.96, 0.48) both;
}

.geometry-overlay.finishing.burst .shape-edge {
  animation: edge-burst 0.3s ease both;
}

@keyframes geometry-arrive {
  0% { transform: translate(var(--from-x), var(--from-y)) scale(0.42) rotate(calc(var(--shape-rotation) - 18deg)); }
  74% { transform: translate(0, 0) scale(1) rotate(var(--shape-rotation)); }
  100% { transform: translate(0, 0) scale(1.2) rotate(var(--shape-rotation)); }
}

@keyframes geometry-finish {
  from { transform: translate(0, 0) scale(1.2) rotate(var(--shape-rotation)); opacity: 1; }
  35% { transform: translate(0, 0) scale(1.1) rotate(var(--shape-rotation)); opacity: 1; }
  to { transform: translate(0, 0) scale(1.1) rotate(var(--shape-rotation)); opacity: 0; }
}

@keyframes edge-finish {
  from { fill: var(--primary-color); }
  25%, 100% { fill: #ffffff; }
}

@keyframes geometry-burst {
  0% { transform: translate(0, 0) scale(1.2) rotate(var(--shape-rotation)); opacity: 1; }
  55% { transform: translate(0, 0) scale(0.82) rotate(var(--shape-rotation)); opacity: 1; }
  88% { opacity: 1; }
  100% { transform: translate(0, 0) scale(var(--burst-scale)) rotate(var(--shape-rotation)); opacity: 0; }
}

@keyframes edge-burst {
  0%, 55% { fill: var(--primary-color); }
  68%, 100% { fill: #ffffff; }
}

@media (prefers-reduced-motion: reduce) {
  .geometry-overlay { display: none; }
  .slide-left-enter-active,
  .slide-left-leave-active,
  .slide-right-enter-active,
  .slide-right-leave-active {
    transition: opacity 0.01ms linear;
  }
}
</style>
