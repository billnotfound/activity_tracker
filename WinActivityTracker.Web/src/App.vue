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
            <img :src="brandIconPath" alt="Logo" class="brand-icon" />
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
          <button class="theme-toggle" @click="toggleDark" :title="isDark ? t('app.lightMode') : t('app.darkMode')">
            <i :class="isDark ? 'pi pi-sun' : 'pi pi-moon'"></i>
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
import timerIcon from './icon/timer.svg'
import settingsIcon from './icon/settings.svg'

const { t } = useI18n()
const { isDark, toggleDark } = useTheme()
const route = useRoute()

// Brand icon changes based on current route
const brandIconPath = computed(() => {
  const path = route.path
  if (path === '/settings' || path === '/tags') {
    return settingsIcon
  }
  return timerIcon
})

// Update favicon based on route
function updateFavicon(path) {
  const isSettings = path === '/settings' || path === '/tags'
  const iconPath = isSettings ? '/settings.ico' : '/timer.ico'

  // Update main favicon
  const favicon = document.getElementById('favicon')
  if (favicon) {
    favicon.href = iconPath + '?v=' + Date.now() // Add timestamp to force refresh
  }

  // Update shortcut icon
  const shortcutIcon = document.getElementById('shortcut-icon')
  if (shortcutIcon) {
    shortcutIcon.href = iconPath + '?v=' + Date.now()
  }
}

// Dynamic favicon based on route
watch(() => route.path, (newPath) => {
  updateFavicon(newPath)
})

// Update on mount to ensure initial state is correct
onMounted(() => {
  updateFavicon(route.path)
  initNavFrame()
})

// ── Nav sliding frame ──

const navMenuRef = ref(null)
const navFrameStyle = ref({})
const navMoving = ref(false)
let navRafId = null
let navTargetEl = null

function startNavAnim(el) {
  navTargetEl = el || navTargetEl
  if (!navTargetEl) return
  navMoving.value = true
  if (navRafId) return

  const tick = () => {
    if (!navTargetEl || !navMenuRef.value) {
      navRafId = null
      navMoving.value = false
      return
    }
    const curLeft = parseFloat(navFrameStyle.value.left) || 0
    const curWid = parseFloat(navFrameStyle.value.width) || 0
    const tgtLeft = navTargetEl.offsetLeft
    const tgtWid = navTargetEl.offsetWidth
    const dx = tgtLeft - curLeft
    const dw = tgtWid - curWid

    if (Math.abs(dx) < 0.5 && Math.abs(dw) < 0.5) {
      navFrameStyle.value = { left: tgtLeft + 'px', width: tgtWid + 'px' }
      navRafId = null
      navMoving.value = false
      return
    }

    navFrameStyle.value = {
      left: (curLeft + dx * 0.2) + 'px',
      width: (curWid + dw * 0.2) + 'px'
    }
    navRafId = requestAnimationFrame(tick)
  }
  navRafId = requestAnimationFrame(tick)
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
        width: active.offsetWidth + 'px'
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
  transition: box-shadow 0.15s ease-out;

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
  text-transform: uppercase;
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
