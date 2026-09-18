// Theme Management Composable
// Reads theme definitions from src/styles/theme.config.js and applies them to
// :root as CSS custom properties at runtime.
//
// User preferences (color scheme, dark mode, page transition, auto-color) are
// persisted to localStorage under `theme-settings`.
//
// To change theme colors, edit theme.config.js — no rebuild needed.
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
const customThemes = ref({ light: null, dark: null })

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
      customThemes.value = normalizeCustomThemes(s.customThemes)
      const legacyPalette = normalizePalette(s.customPalette)
      if (!customThemes.value.light && legacyPalette) {
        customThemes.value = { ...customThemes.value, light: { name: 'Custom', colors: legacyPalette } }
      }
    } catch (e) {
      console.warn('Failed to parse theme settings:', e)
    }
  }
}

// ---------- derived ----------
const activeThemeId = computed(() =>
  isDark.value ? darkTheme.value : lightTheme.value
)

const availableThemes = computed(() => {
  const result = { ...THEMES }
  if (customThemes.value.light) result['custom-light'] = customThemeDefinition('light', customThemes.value.light)
  if (customThemes.value.dark) result['custom-dark'] = customThemeDefinition('dark', customThemes.value.dark)
  return result
})

const activeTheme = computed(() =>
  availableThemes.value[activeThemeId.value] ?? THEMES[DEFAULT_LIGHT_THEME]
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
    customThemes: customThemes.value,
  }))
}

// Re-apply whenever any input changes.
// No deep: true — overrides.value is replaced (not mutated) on every
// setOverride, so a shallow watch catches the change.
watch([isDark, lightTheme, darkTheme, overrides, customThemes], () => {
  applyTheme()
  saveSettings()
})

watch([autoColor, pageTransition], saveSettings)

function normalizePalette(colors) {
  if (!Array.isArray(colors) || colors.length !== 3) return null
  const normalized = colors.map(color => String(color || '').trim().toUpperCase())
  return normalized.every(color => /^#[0-9A-F]{6}$/.test(color)) ? normalized : null
}

function paletteOverrides(colors) {
  return {
    'primary-color': colors[0],
    'secondary-color': colors[1],
    'accent-color': colors[2],
    'success-color': colors[0],
    'warning-color': colors[2],
    'danger-color': colors[1],
  }
}

function normalizeCustomTheme(value) {
  const name = String(value?.name || '').trim().slice(0, 40)
  const colors = normalizePalette(value?.colors)
  return name && colors ? { name, colors } : null
}

function normalizeCustomThemes(value) {
  return {
    light: normalizeCustomTheme(value?.light),
    dark: normalizeCustomTheme(value?.dark),
  }
}

function customThemeDefinition(mode, saved) {
  const isDarkTheme = mode === 'dark'
  const neutral = isDarkTheme
    ? {
        'surface-ground': '#000000', 'surface-card': '#101010',
        'surface-100': '#181818', 'surface-200': '#2B2B2B',
        'surface-300': '#424242', 'surface-400': '#A0A0A0',
        'text-color': '#FFFFFF', 'border-color': '#666666',
      }
    : {
        'surface-ground': '#FFFFFF', 'surface-card': '#FFFFFF',
        'surface-100': '#F5F5F5', 'surface-200': '#E5E5E5',
        'surface-300': '#CCCCCC', 'surface-400': '#777777',
        'text-color': '#000000', 'border-color': '#000000',
      }
  return {
    id: `custom-${mode}`,
    name: saved.name,
    description: '',
    isDark: isDarkTheme,
    colors: { ...paletteOverrides(saved.colors), ...neutral },
  }
}

function colorMetrics(value) {
  const match = /^#([0-9a-f]{6})$/i.exec(String(value || '').trim())
  if (!match) return null
  const number = Number.parseInt(match[1], 16)
  const r = ((number >> 16) & 255) / 255
  const g = ((number >> 8) & 255) / 255
  const b = (number & 255) / 255
  const max = Math.max(r, g, b)
  const min = Math.min(r, g, b)
  const delta = max - min
  let hue = 0
  if (delta) {
    if (max === r) hue = 60 * (((g - b) / delta) % 6)
    else if (max === g) hue = 60 * ((b - r) / delta + 2)
    else hue = 60 * ((r - g) / delta + 4)
  }
  if (hue < 0) hue += 360
  const lightness = (max + min) / 2
  const saturation = delta ? delta / (1 - Math.abs(2 * lightness - 1)) : 0
  return { r: r * 255, g: g * 255, b: b * 255, hue, saturation, lightness }
}

function hasDistinctColors(colors) {
  const metrics = colors.map(colorMetrics)
  if (metrics.some(value => !value)) return false
  for (let left = 0; left < metrics.length; left++) {
    for (let right = left + 1; right < metrics.length; right++) {
      const a = metrics[left]
      const b = metrics[right]
      const rgbDistance = Math.hypot(a.r - b.r, a.g - b.g, a.b - b.b)
      const hueDistance = Math.min(Math.abs(a.hue - b.hue), 360 - Math.abs(a.hue - b.hue))
      const lightnessDistance = Math.abs(a.lightness - b.lightness)
      if (rgbDistance < 72) return false
      if (a.saturation > 0.14 && b.saturation > 0.14 && hueDistance < 22 && lightnessDistance < 0.2) return false
      if (a.saturation <= 0.14 && b.saturation <= 0.14 && lightnessDistance < 0.22) return false
    }
  }
  return true
}

export function useTheme() {
  const toggleDark = () => { isDark.value = !isDark.value }

  const setLightTheme = (id) => {
    const selected = availableThemes.value[id]
    if (selected && !selected.isDark) lightTheme.value = id
  }

  const setDarkTheme = (id) => {
    const selected = availableThemes.value[id]
    if (selected?.isDark) darkTheme.value = id
  }

  // Back-compat with old API (single colorScheme that maps to light theme)
  const colorScheme = computed({
    get: () => lightTheme.value,
    set: (v) => setLightTheme(v),
  })
  const setColorScheme = setLightTheme

  const setAutoColor = (v) => {
    autoColor.value = v
    if (!v) overrides.value = {}
  }
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

  const saveCustomTheme = (mode, name, colors) => {
    if (mode !== 'light' && mode !== 'dark') return false
    const normalized = normalizeCustomTheme({ name, colors })
    if (!normalized) return false
    autoColor.value = false
    overrides.value = {}
    customThemes.value = { ...customThemes.value, [mode]: normalized }
    if (mode === 'dark') {
      darkTheme.value = 'custom-dark'
      isDark.value = true
    } else {
      lightTheme.value = 'custom-light'
      isDark.value = false
    }
    return true
  }

  // Pick the first distinct three-color palette from at most the five most-used
  // processes. Near-identical hues or RGB positions are skipped; if none of the
  // first five works, remove overrides and return to the selected theme.
  const applyAutoColor = async (candidates) => {
    if (!autoColor.value) return false
    let palettes = Array.isArray(candidates) ? candidates : []

    // Backwards compatibility for callers that pass one process name.
    if (!palettes.length && typeof candidates === 'string' && candidates) {
      try {
        const response = await fetch(`/api/icons/${encodeURIComponent(candidates)}`)
        if (response.ok) palettes = [await response.json()]
      } catch (error) {
        console.warn('Auto color failed:', error)
      }
    }

    for (const palette of palettes.slice(0, 5)) {
      const colors = [palette.colorPrimary, palette.colorSecondary, palette.colorAccent]
      if (!hasDistinctColors(colors)) continue
      const next = paletteOverrides(colors)
      if (JSON.stringify(next) !== JSON.stringify(overrides.value)) overrides.value = next
      return true
    }

    if (Object.keys(overrides.value).length) overrides.value = {}
    return false
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
    customThemes,
    themes: availableThemes,

    // actions
    toggleDark,
    setLightTheme,
    setDarkTheme,
    setColorScheme,        // legacy
    setAutoColor,
    setPageTransition,
    setOverride,
    clearOverrides,
    saveCustomTheme,
    applyAutoColor,
    applyTheme,
  }
}
