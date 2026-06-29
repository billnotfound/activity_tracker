// ============================================================
// Theme Management Composable
// ============================================================
// Reads theme definitions from src/styles/theme.config.js and
// applies them to :root as CSS custom properties at runtime.
//
// User preferences (color scheme, dark mode, page transition,
// auto-color) are persisted to localStorage under `theme-settings`.
//
// To change theme colors, edit theme.config.js — no rebuild needed.
// ============================================================
import { ref, computed, watch } from 'vue'
import {
  THEMES,
  DEFAULT_LIGHT_THEME,
  DEFAULT_DARK_THEME,
} from '../styles/theme.config.js'

const STORAGE_KEY = 'theme-settings'

// ---------- shared reactive state ----------
const isDark = ref(false)
const lightTheme = ref(DEFAULT_LIGHT_THEME)
const darkTheme = ref(DEFAULT_DARK_THEME)
const autoColor = ref(false)
const pageTransition = ref('slide')          // 'slide' | 'geometric'
// User overrides keyed by CSS variable name, e.g. { 'primary-color': '#abcdef' }
const overrides = ref({})

// ---------- load persisted settings ----------
if (typeof window !== 'undefined') {
  const stored = localStorage.getItem(STORAGE_KEY)
  if (stored) {
    try {
      const s = JSON.parse(stored)
      isDark.value = s.isDark ?? false
      lightTheme.value = s.lightTheme ?? DEFAULT_LIGHT_THEME
      darkTheme.value = s.darkTheme ?? DEFAULT_DARK_THEME
      autoColor.value = s.autoColor ?? false
      pageTransition.value = s.pageTransition ?? 'slide'
      overrides.value = s.overrides ?? {}
    } catch (e) {
      console.warn('Failed to parse theme settings:', e)
    }
  }
}

// ---------- derived ----------
const activeThemeId = computed(() =>
  isDark.value ? darkTheme.value : lightTheme.value
)

const activeTheme = computed(() =>
  THEMES[activeThemeId.value] ?? THEMES[DEFAULT_LIGHT_THEME]
)

// ---------- core: write CSS vars to :root ----------
let prevThemeId = null

function applyTheme() {
  if (typeof document === 'undefined') return

  const theme = activeTheme.value
  const root = document.documentElement

  // Swap theme-* class directly instead of scanning classList.
  if (prevThemeId) root.classList.remove(`theme-${prevThemeId}`)
  root.classList.remove('dark-mode')
  root.classList.add(`theme-${theme.id}`)
  if (theme.isDark) root.classList.add('dark-mode')
  prevThemeId = theme.id

  // Apply colors as CSS variables
  for (const [key, value] of Object.entries(theme.colors)) {
    root.style.setProperty(`--${key}`, value)
  }

  // Apply user overrides on top
  for (const [key, value] of Object.entries(overrides.value)) {
    if (value) root.style.setProperty(`--${key}`, value)
  }
}

// ---------- persistence ----------
function saveSettings() {
  if (typeof localStorage === 'undefined') return
  localStorage.setItem(STORAGE_KEY, JSON.stringify({
    isDark: isDark.value,
    lightTheme: lightTheme.value,
    darkTheme: darkTheme.value,
    autoColor: autoColor.value,
    pageTransition: pageTransition.value,
    overrides: overrides.value,
  }))
}

// Re-apply whenever any input changes.
// No deep: true — overrides.value is replaced (not mutated) on every
// setOverride, so a shallow watch catches the change.
watch([isDark, lightTheme, darkTheme, overrides], () => {
  applyTheme()
  saveSettings()
})

watch([autoColor, pageTransition], saveSettings)

export function useTheme() {
  const toggleDark = () => { isDark.value = !isDark.value }

  const setLightTheme = (id) => {
    if (THEMES[id] && !THEMES[id].isDark) lightTheme.value = id
  }

  const setDarkTheme = (id) => {
    if (THEMES[id] && THEMES[id].isDark) darkTheme.value = id
  }

  // Back-compat with old API (single colorScheme that maps to light theme)
  const colorScheme = computed({
    get: () => lightTheme.value,
    set: (v) => setLightTheme(v),
  })
  const setColorScheme = setLightTheme

  const setAutoColor = (v) => { autoColor.value = v }
  const setPageTransition = (v) => { pageTransition.value = v }

  // Override a single CSS variable (used by color picker / auto-color)
  const setOverride = (key, value) => {
    if (value) overrides.value = { ...overrides.value, [key]: value }
    else {
      const next = { ...overrides.value }
      delete next[key]
      overrides.value = next
    }
  }

  const clearOverrides = () => { overrides.value = {} }

  // Apply auto color from a process icon's extracted palette
  const applyAutoColor = async (topProcess) => {
    if (!autoColor.value || !topProcess) return
    try {
      const res = await fetch(`/api/icons/${topProcess}`)
      if (!res.ok) return
      const icon = await res.json()
      const next = { ...overrides.value }
      if (icon.colorPrimary) next['primary-color'] = icon.colorPrimary
      if (icon.colorSecondary) next['secondary-color'] = icon.colorSecondary
      if (icon.colorAccent) next['accent-color'] = icon.colorAccent
      overrides.value = next
    } catch (e) {
      console.warn('Auto color failed:', e)
    }
  }

  return {
    // state
    isDark,
    lightTheme,
    darkTheme,
    colorScheme,           // legacy alias for lightTheme
    activeThemeId,
    activeTheme,
    autoColor,
    pageTransition,
    overrides,
    themes: THEMES,

    // actions
    toggleDark,
    setLightTheme,
    setDarkTheme,
    setColorScheme,        // legacy
    setAutoColor,
    setPageTransition,
    setOverride,
    clearOverrides,
    applyAutoColor,
    applyTheme,
  }
}
