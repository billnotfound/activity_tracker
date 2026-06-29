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
        <div class="navbar-menu" ref="navMenuRef" @mousemove="onNavMouseMove" @mouseleave="onNavMouseLeave">
          <div class="nav-frame" :style="navFrameStyle" :class="{ moving: navMoving }"></div>
          <router-link class="nav-item" to="/" :class="{ active: $route.path === '/' }">
            {{ t('nav.dashboard') }}
          </router-link>
          <router-link class="nav-item" to="/history" :class="{ active: $route.path === '/history' }">
            {{ t('nav.history') }}
          </router-link>
          <router-link class="nav-item" to="/tags" :class="{ active: $route.path === '/tags' }">
            {{ t('nav.tags') }}
          </router-link>
          <router-link class="nav-item" to="/settings" :class="{ active: $route.path === '/settings' }">
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
            <i :class="isDark ? 'pi pi-sun' : 'pi pi-moon'" aria-hidden="true"></i>
          </button>
        </div>
      </div>
    </nav>

    <main class="main-content memphis-background">
      <div class="page-container">
        <PageTransition>
          <router-view :key="$route.path" />
        </PageTransition>
      </div>
    </main>
  </div>
</template>

<script setup>
import { useI18n } from './i18n/index.js'
import { useTheme } from './composables/useTheme.js'
import { useRoute } from 'vue-router'
import { watch, onMounted, nextTick, ref, computed } from 'vue'
import PageTransition from './components/PageTransition.vue'
import timerIconRaw from './ico/timer.svg?raw'
import settingsIconRaw from './ico/settings.svg?raw'

const { t } = useI18n()
const { isDark, toggleDark } = useTheme()
const route = useRoute()

// Brand icon raw SVG content changes based on current route
const brandIconRaw = computed(() => {
  const path = route.path
  if (path === '/settings' || path === '/tags') {
    return settingsIconRaw
  }
  return timerIconRaw
})

// Update favicon based on route and dark mode
function updateFavicon(path) {
  const isSettings = path === '/settings' || path === '/tags'
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
})

// ── Nav sliding frame ──
// Instead of a RAF loop interpolating left/width, set the target directly
// and let CSS transition handle the animation. navMoving is toggled for the
// transition duration so the hover shadow can be suppressed mid-flight.

const navMenuRef = ref(null)
const navFrameStyle = ref({})
const navMoving = ref(false)
let moveTimer = null
let navTargetEl = null

function startNavAnim(el) {
  if (!el) return
  navTargetEl = el
  navMoving.value = true
  navFrameStyle.value = {
    left: el.offsetLeft + 'px',
    width: el.offsetWidth + 'px',
  }
  if (moveTimer) clearTimeout(moveTimer)
  // Match the CSS transition duration (0.28s) + small buffer.
  moveTimer = setTimeout(() => { navMoving.value = false }, 320)
}

function onNavMouseMove(e) {
  const menu = navMenuRef.value
  if (!menu) return
  const items = menu.querySelectorAll('.nav-item')
  let nearest = null, minDist = Infinity
  for (const item of items) {
    const r = item.getBoundingClientRect()
    const cx = r.left + r.width / 2
    const dist = Math.abs(e.clientX - cx)
    if (dist < minDist) { minDist = dist; nearest = item }
  }
  if (nearest && nearest !== navTargetEl) startNavAnim(nearest)
}

function onNavMouseLeave() {
  const menu = navMenuRef.value
  if (!menu) return
  const active = menu.querySelector('.nav-item.active')
  if (active) startNavAnim(active)
}

function initNavFrame() {
  nextTick(() => {
    const menu = navMenuRef.value
    if (!menu) return
    const active = menu.querySelector('.nav-item.active')
    if (active) {
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
    left 0.28s cubic-bezier(0.4, 0, 0.2, 1),
    width 0.28s cubic-bezier(0.4, 0, 0.2, 1),
    box-shadow 0.15s ease-out;

  &.moving {
    box-shadow: 0 0 0 transparent !important;
  }
}

.navbar-menu:hover .nav-frame:not(.moving) {
  border-color: var(--text-color);
  transform: translateY(-2px);
  box-shadow: 4px 4px 0 color-mix(in srgb, var(--primary-color) 80%, transparent);
  transition:
    box-shadow 0.15s ease-out,
    transform 0.12s ease-out,
    border-color 0s 0s;
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

  &:hover {
    transform: translateY(-2px);
  }
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
    box-shadow: 0 0 8px rgba(255, 20, 147, 0.3);
  }
}
</style>
