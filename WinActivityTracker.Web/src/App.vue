<!--
  Root Vue component — Memphis style navbar + router-view with page transitions.
  Theme classes (theme-* / dark-mode) are applied to <html> by useTheme().
-->
<template>
  <div id="app">
    <nav class="memphis-navbar">
      <div class="navbar-container">
        <div class="navbar-brand">
          <div class="brand-deco"></div>
          <router-link to="/" class="brand-link">
            <span v-html="brandIconRaw" class="brand-icon"></span>
          </router-link>
        </div>
        <div class="navbar-menu" ref="navMenuRef" @pointerleave="releaseNavAttraction">
          <div class="nav-frame" :style="navFrameStyle" :class="{ moving: navMoving, attracted: navAttracted }"></div>
          <router-link class="nav-item" data-nav-key="dashboard" to="/" :class="{ active: $route.path === '/', magnetic: navMagneticKey === 'dashboard' }" @focus="focusNavItem($event.currentTarget)">
            {{ t('nav.dashboard') }}
          </router-link>
          <router-link class="nav-item" data-nav-key="history" to="/history" :class="{ active: $route.path === '/history', magnetic: navMagneticKey === 'history' }" @focus="focusNavItem($event.currentTarget)">
            {{ t('nav.history') }}
          </router-link>
          <router-link class="nav-item" data-nav-key="settings" to="/settings" :class="{ active: $route.path === '/settings', magnetic: navMagneticKey === 'settings' }" @focus="focusNavItem($event.currentTarget)">
            {{ t('nav.settings') }}
          </router-link>
        </div>
        <div class="navbar-actions">
          <button
            class="theme-toggle"
            @click="toggleDark"
            :title="isDark ? t('app.lightMode') : t('app.darkMode')"
            :aria-label="isDark ? t('app.lightMode') : t('app.darkMode')"
            :aria-pressed="isDark"
          >
            <component :is="isDark ? Sun : Moon" :size="20" aria-hidden="true" />
          </button>
        </div>
      </div>
    </nav>

    <div class="page-color-wash" aria-hidden="true"></div>

    <main class="main-content memphis-background">
      <div class="page-container">
        <router-view v-slot="{ Component, route: viewRoute }">
          <PageTransition v-if="Component" :component="Component" :route-key="viewRoute.path" />
        </router-view>
      </div>
    </main>
  </div>
</template>

<script setup>
import { useI18n } from './i18n/index.js'
import { useTheme } from './composables/useTheme.js'
import { useRoute } from 'vue-router'
import { watch, onMounted, onBeforeUnmount, nextTick, ref, computed } from 'vue'
import PageTransition from './components/PageTransition.vue'
import { Sun, Moon } from '@lucide/vue'
import timerIconRaw from './ico/timer.svg?raw'
import settingsIconRaw from './ico/settings.svg?raw'

const { t } = useI18n()
const { isDark, toggleDark } = useTheme()
const route = useRoute()

// Brand icon raw SVG content changes based on current route
const brandIconRaw = computed(() => {
  const path = route.path
  if (path === '/settings') {
    return settingsIconRaw
  }
  return timerIconRaw
})

// Update favicon based on route and dark mode
function updateFavicon(path) {
  const isSettings = path === '/settings'
  const svgRaw = isSettings ? settingsIconRaw : timerIconRaw

  const dark = document.documentElement.classList.contains('dark-mode')
  const strokeColor = dark
    ? '#ffffff'
    : getComputedStyle(document.documentElement).getPropertyValue('--primary-color').trim() || '#6B7FD7'
  const svgContent = svgRaw.replace(/stroke:\s*currentColor/g, 'stroke: ' + strokeColor)
  const dataUri = 'data:image/svg+xml,' + encodeURIComponent(svgContent)

  const favicon = document.getElementById('favicon')
  if (favicon) {
    favicon.type = 'image/svg+xml'
    favicon.href = dataUri
  }
  const shortcutIcon = document.getElementById('shortcut-icon')
  if (shortcutIcon) {
    shortcutIcon.type = 'image/svg+xml'
    shortcutIcon.href = dataUri
  }
}

// Dynamic favicon based on route and dark mode
watch([() => route.path, isDark], () => {
  updateFavicon(route.path)
}, { immediate: true })

// Update on mount to ensure initial state is correct
onMounted(() => {
  initNavFrame()
  window.addEventListener('pointermove', onNavMouseMove, { passive: true })
})

onBeforeUnmount(() => {
  window.removeEventListener('pointermove', onNavMouseMove)
  if (moveTimer) clearTimeout(moveTimer)
})

// ── Nav sliding frame ──
// Each item owns a magnetic capture area. Pointer movement only retargets the
// frame inside that area; CSS keeps the current velocity visually continuous
// when the target changes while a previous slide is still running.

const navMenuRef = ref(null)
const navFrameStyle = ref({})
const navMoving = ref(false)
const navAttracted = ref(false)
const navMagneticKey = ref('')
let moveTimer = null
let navTargetEl = null
const NAV_ATTRACTION_RADIUS = 12

function startNavAnim(el) {
  if (!el) return
  navTargetEl = el
  navMoving.value = true
  navFrameStyle.value = {
    left: el.offsetLeft + 'px',
    width: el.offsetWidth + 'px',
  }
  if (moveTimer) clearTimeout(moveTimer)
  // Match the longest position transition (0.34s) + a small buffer.
  moveTimer = setTimeout(() => { navMoving.value = false }, 380)
}

function onNavMouseMove(e) {
  if (e.pointerType === 'touch') return
  const menu = navMenuRef.value
  if (!menu) return
  const items = menu.querySelectorAll('.nav-item')
  let nearest = null, minDist = Infinity, nearestRadius = 0
  for (const item of items) {
    const r = item.getBoundingClientRect()
    const dx = Math.max(r.left - e.clientX, 0, e.clientX - r.right)
    const dy = Math.max(r.top - e.clientY, 0, e.clientY - r.bottom)
    const dist = Math.hypot(dx, dy)
    if (dist < minDist) {
      minDist = dist
      nearest = item
      nearestRadius = Math.min(NAV_ATTRACTION_RADIUS, r.height)
    }
  }
  if (nearest && minDist <= nearestRadius) {
    navAttracted.value = true
    navMagneticKey.value = nearest.dataset.navKey || ''
    if (nearest !== navTargetEl) startNavAnim(nearest)
    return
  }
  releaseNavAttraction()
}

function releaseNavAttraction() {
  if (!navAttracted.value) return
  navAttracted.value = false
  navMagneticKey.value = ''
  const menu = navMenuRef.value
  if (!menu) return
  const active = menu.querySelector('.nav-item.active')
  if (active && active !== navTargetEl) startNavAnim(active)
}

function focusNavItem(element) {
  if (!navAttracted.value) navMagneticKey.value = ''
  startNavAnim(element)
}

function initNavFrame() {
  nextTick(() => {
    const menu = navMenuRef.value
    if (!menu) return
    const active = menu.querySelector('.nav-item.active')
    if (active) {
      navTargetEl = active
      navFrameStyle.value = {
        left: active.offsetLeft + 'px',
        width: active.offsetWidth + 'px',
      }
    }
  })
}

watch(() => route.path, () => {
  nextTick(() => {
    const menu = navMenuRef.value
    if (!menu) return
    const active = menu.querySelector('.nav-item.active')
    if (active) startNavAnim(active)
  })
})
</script>

<style lang="scss" scoped>
.memphis-navbar {
  background: var(--surface-card);
  border-bottom: 2px solid var(--border-color);
  position: sticky;
  top: 0;
  z-index: 1000;
  transition: all 0.3s;
}

.navbar-container {
  max-width: 1400px;
  margin: 0 auto;
  padding: 0 24px;
  display: flex;
  align-items: center;
  justify-content: space-between;
  height: 64px;
  gap: 32px;
  flex-wrap: wrap;

  @media (max-width: 768px) {
    height: auto;
    min-height: 64px;
    padding: 12px 16px;
    gap: 16px;
  }
}

.navbar-brand {
  position: relative;

  .brand-deco {
    position: absolute;
    top: -6px;
    left: -6px;
    width: 12px;
    height: 12px;
    background: var(--accent-color);
    clip-path: polygon(50% 0%, 100% 100%, 0% 100%);
    opacity: 0;
    transition: opacity 0.3s;
  }

  .brand-link {
    font-size: 1.5rem;
    font-weight: 700;
    color: var(--text-color);
    text-decoration: none;
    padding: 6px 12px;
    border: 2px solid transparent;
    display: flex;
    align-items: center;
    justify-content: center;
    position: relative;
    transition: all 0.3s ease;

    .brand-icon {
      width: 28px;
      height: 28px;
      display: block;
      color: color-mix(in srgb, var(--primary-color) 80%, transparent);
      transition: color 0.3s;

      :deep(svg) {
        display: block;
        width: 100%;
        height: 100%;
      }
    }

    .dark-mode & .brand-icon {
      color: #ffffff;
    }

    &:hover {
      border-color: var(--primary-color);
      transform: translateY(-2px);

      & + .brand-deco {
        opacity: 1;
      }
    }
  }

  &:hover .brand-deco {
    opacity: 1;
  }
}

.navbar-menu {
  position: relative;
  display: flex;
  gap: 4px;

  @media (max-width: 768px) {
    flex-wrap: wrap;
    gap: 2px;
    width: 100%;
    order: 3;
    justify-content: center;
  }
}

.nav-frame {
  position: absolute;
  top: 0;
  left: 0;
  height: 100%;
  border: 3px solid color-mix(in srgb, var(--text-color) 80%, transparent);
  pointer-events: none;
  box-shadow: 0 0 0 transparent;
  z-index: 0;
  transition:
    left 0.34s cubic-bezier(0.22, 0.72, 0.18, 1),
    width 0.3s cubic-bezier(0.22, 0.72, 0.18, 1),
    transform 0.12s ease-out,
    box-shadow 0.15s ease-out,
    border-color 0.12s ease-out;
  will-change: left, width, transform;
}

.nav-frame.attracted {
  border-color: var(--text-color);
  transform: translateY(-2px);
  box-shadow: 4px 4px 0 color-mix(in srgb, var(--primary-color) 80%, transparent);
}

.nav-item {
  position: relative;
  z-index: 1;
  padding: 10px 18px;
  color: var(--text-color);
  text-decoration: none;
  font-weight: 600;
  letter-spacing: 0.5px;
  font-size: 0.9rem;
  border: 2px solid transparent;
  background: transparent;
  transition: all 0.2s ease;

  @media (max-width: 768px) {
    padding: 8px 12px;
    font-size: 0.8rem;
    letter-spacing: 0.3px;
  }

  &:hover,
  &.magnetic {
    transform: translateY(-2px);
  }
}

@media (prefers-reduced-motion: reduce) {
  .nav-frame,
  .nav-item { transition-duration: 0.01ms; }
}

.navbar-actions {
  display: flex;
  gap: 12px;
}

.theme-toggle {
  width: 40px;
  height: 40px;
  border: 2px solid var(--border-color);
  background: transparent;
  color: var(--text-color);
  cursor: pointer;
  font-size: 1.1rem;
  transition: all 0.3s ease;
  display: flex;
  align-items: center;
  justify-content: center;
  position: relative;

  // Small accent dot - only show on hover
  &::before {
    content: '';
    position: absolute;
    top: -4px;
    right: -4px;
    width: 8px;
    height: 8px;
    background: var(--accent-color);
    border-radius: 50%;
    opacity: 0;
    transition: opacity 0.3s;
  }

  &:hover {
    border-color: var(--primary-color);
    transform: translateY(-2px);

    &::before {
      opacity: 1;
    }
  }

  &:active {
    transform: translateY(0);
  }
}

.main-content {
  max-width: 1400px;
  margin: 0 auto;
  padding: 32px 24px;
  min-height: calc(100vh - 64px);

  @media (max-width: 768px) {
    padding: 16px 12px;
  }
}

.page-color-wash {
  position: fixed;
  inset: 0;
  z-index: 900;
  pointer-events: none;
  background: var(--primary-color);
  opacity: 0.03;
  transition: background-color 0.3s ease;
}

.page-container {
  position: relative;
  width: 100%;
  min-height: 400px;
}

// Dark mode - subtle glow only on interaction
.dark-mode {
  .nav-item:hover,
  .theme-toggle:hover,
  .brand-link:hover {
    box-shadow: 0 0 8px color-mix(in srgb, var(--primary-color) 30%, transparent);
  }
}
</style>
