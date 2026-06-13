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
          <router-link to="/" class="brand-link">W</router-link>
        </div>
        <div class="navbar-menu">
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
          <button class="theme-toggle" @click="toggleDark" :title="isDark ? 'Light Mode' : 'Dark Mode'">
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
import { watch, onMounted } from 'vue'
import PageTransition from './components/PageTransition.vue'

const { t } = useI18n()
const { isDark, toggleDark } = useTheme()
const route = useRoute()

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
    text-transform: uppercase;
    letter-spacing: 1.5px;
    padding: 6px 12px;
    border: 2px solid transparent;
    display: inline-block;
    position: relative;
    transition: all 0.3s ease;
    
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

.nav-item {
  padding: 10px 18px;
  color: var(--text-color);
  text-decoration: none;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.5px;
  font-size: 0.9rem;
  border: 2px solid transparent;
  background: transparent;
  position: relative;
  transition: all 0.2s ease;

  @media (max-width: 768px) {
    padding: 8px 12px;
    font-size: 0.8rem;
    letter-spacing: 0.3px;
  }

  // Underline only on hover
  &::after {
    content: '';
    position: absolute;
    bottom: 0;
    left: 50%;
    transform: translateX(-50%);
    width: 0;
    height: 3px;
    background: var(--primary-color);
    transition: width 0.3s ease;
  }

  &:hover {
    transform: translateY(-2px);

    &::after {
      width: 70%;
    }
  }

  // Active state - border only, no background fill
  &.active {
    border-bottom: 3px solid var(--primary-color);

    &::after {
      display: none;
    }
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
