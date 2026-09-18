<template>
  <Transition :name="currentTransition" :mode="transitionMode">
    <div :key="displayKey" class="page-transition-layer" :style="geometryStyle">
      <component :is="displayComponent" />
    </div>
  </Transition>

  <div
    v-if="geometryVisible"
    :key="geometryKey"
    class="geometry-overlay"
    :style="geometryStyle"
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
import { ref, shallowRef, computed, watch, onBeforeUnmount } from 'vue'
import { useRoute } from 'vue-router'
import { useTheme } from '../composables/useTheme.js'

const props = defineProps({
  component: { type: [Object, Function], required: true },
  routeKey: { type: String, required: true },
})

const route = useRoute()
const { pageTransition } = useTheme()
const displayComponent = shallowRef(props.component)
const displayKey = ref(props.routeKey)
const displayedRouteKey = ref(props.routeKey)
let viewRevision = 0

const routeOrder = ['/', '/history', '/tags', '/settings']
const isForward = ref(true)
const geometryVisible = ref(false)
const geometryKey = ref(0)
const geometryDirection = ref({ x: '0vw', y: '-145vh' })
const geometryShape = ref('triangle')
const geometryRotation = ref(17)
const revealClip = ref('polygon(50% 50%, 50% 50%, 50% 50%)')
const collapsedClip = ref('polygon(50% 50%, 50% 50%, 50% 50%)')
let geometryTimer = null

const points = {
  triangle: {
    edge: '50,3 98,91 2,91',
    core: '50,15 87,83 13,83',
    corePoints: [[50, 15], [87, 83], [13, 83]],
  },
  star: {
    edge: '50,2 61,36 97,36 68,57 79,92 50,71 21,92 32,57 3,36 39,36',
    core: '50,14 58,42 86,42 63,59 72,82 50,67 28,82 37,59 14,42 42,42',
    corePoints: [[50, 14], [58, 42], [86, 42], [63, 59], [72, 82], [50, 67], [28, 82], [37, 59], [14, 42], [42, 42]],
  },
}

const currentTransition = computed(() =>
  pageTransition.value === 'geometric'
    ? 'shape-page'
    : (isForward.value ? 'slide-left' : 'slide-right')
)
const transitionMode = computed(() => pageTransition.value === 'geometric' ? undefined : 'out-in')
const shapePoints = computed(() => points[geometryShape.value])
const geometryStyle = computed(() => ({
  '--from-x': geometryDirection.value.x,
  '--from-y': geometryDirection.value.y,
  '--shape-rotation': `${geometryRotation.value}deg`,
  '--shape-reveal-clip': revealClip.value,
  '--shape-collapsed-clip': collapsedClip.value,
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

function buildRevealClips(shape, rotation) {
  const hostRect = document.querySelector('.page-container')?.getBoundingClientRect()
  const centerX = window.innerWidth / 2 - (hostRect?.left || 0)
  const centerY = window.innerHeight / 2 - (hostRect?.top || 0)
  const size = Math.min(window.innerWidth, window.innerHeight) * 0.4714 * 1.1
  const radians = rotation * Math.PI / 180
  const cosine = Math.cos(radians)
  const sine = Math.sin(radians)
  const rotated = points[shape].corePoints.map(([x, y]) => {
    const localX = (x - 50) / 100 * size
    const localY = (y - 50) / 100 * size
    return [
      centerX + localX * cosine - localY * sine,
      centerY + localX * sine + localY * cosine,
    ]
  })
  revealClip.value = `polygon(${rotated.map(([x, y]) => `${x.toFixed(2)}px ${y.toFixed(2)}px`).join(', ')})`
  collapsedClip.value = `polygon(${rotated.map(() => `${centerX.toFixed(2)}px ${centerY.toFixed(2)}px`).join(', ')})`
}

function triggerGeometryTransition() {
  geometryShape.value = Math.random() < 0.5 ? 'triangle' : 'star'
  const direction = Math.random() * Math.PI * 2
  geometryDirection.value = {
    x: `${(Math.cos(direction) * 145).toFixed(2)}vw`,
    y: `${(Math.sin(direction) * 145).toFixed(2)}vh`,
  }
  geometryRotation.value = randomRotation(geometryShape.value)
  buildRevealClips(geometryShape.value, geometryRotation.value)
  geometryKey.value += 1
  geometryVisible.value = true
  clearTimeout(geometryTimer)
  geometryTimer = setTimeout(() => { geometryVisible.value = false }, 1020)
}

watch(() => route.path, (newPath, oldPath) => {
  const newIndex = routeOrder.indexOf(newPath)
  const oldIndex = routeOrder.indexOf(oldPath || '/')
  if (newIndex !== -1 && oldIndex !== -1) isForward.value = newIndex > oldIndex
}, { immediate: true })

watch(() => props.component, (nextComponent) => {
  if (!nextComponent || props.routeKey === displayedRouteKey.value) return
  if (pageTransition.value === 'geometric') triggerGeometryTransition()
  displayComponent.value = nextComponent
  displayedRouteKey.value = props.routeKey
  displayKey.value = `${props.routeKey}:${++viewRevision}`
})

onBeforeUnmount(() => clearTimeout(geometryTimer))
</script>

<style scoped>
.page-transition-layer {
  width: 100%;
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

.shape-page-enter-active,
.shape-page-leave-active {
  position: absolute;
  inset: 0;
  width: 100%;
}

.shape-page-enter-active {
  z-index: 2;
  animation: reveal-new-page 1s linear both;
}

.shape-page-leave-active {
  z-index: 1;
  animation: hold-old-page 1s linear both;
}

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
  /* Even a 45° rotation at the 1.2 pulse stays within 80% of the narrow side. */
  width: min(47.14vw, 47.14vh);
  height: min(47.14vw, 47.14vh);
  overflow: visible;
  transform-origin: center;
  filter: drop-shadow(0 14px 28px color-mix(in srgb, var(--text-color) 24%, transparent));
  animation: geometry-arrive 1s cubic-bezier(0.2, 0.78, 0.2, 1) both;
}

.shape-edge {
  fill: var(--primary-color);
  animation: edge-turn-white 1s linear both;
}

@keyframes geometry-arrive {
  0% { transform: translate(var(--from-x), var(--from-y)) scale(0.42) rotate(calc(var(--shape-rotation) - 18deg)); }
  42% { transform: translate(0, 0) scale(1) rotate(var(--shape-rotation)); }
  56% { transform: translate(0, 0) scale(1.2) rotate(var(--shape-rotation)); }
  69%, 100% { transform: translate(0, 0) scale(1.1) rotate(var(--shape-rotation)); }
}

@keyframes reveal-new-page {
  0%, 38% { clip-path: var(--shape-collapsed-clip); }
  44%, 68% { clip-path: var(--shape-reveal-clip); }
  69%, 100% { clip-path: inset(0); }
}

@keyframes hold-old-page {
  0%, 68% { opacity: 1; }
  76%, 100% { opacity: 0; }
}

@keyframes edge-turn-white {
  0%, 54% { fill: var(--primary-color); opacity: 1; }
  55%, 84% { fill: #ffffff; opacity: 1; }
  100% { fill: #ffffff; opacity: 0; }
}

@media (prefers-reduced-motion: reduce) {
  .geometry-overlay { display: none; }
  .shape-page-enter-active,
  .shape-page-leave-active,
  .slide-left-enter-active,
  .slide-left-leave-active,
  .slide-right-enter-active,
  .slide-right-leave-active {
    animation: none;
    transition: opacity 0.01ms linear;
  }
}
</style>
