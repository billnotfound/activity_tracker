import { computed, onBeforeUnmount, reactive } from 'vue'

const FALLBACK_COLORS = {
  light: ['#C87773', '#77A67B', '#B7E6F0'],
  dark: ['#FEB4C1', '#5A6CBC', '#3B1E3D'],
}

export function useSettingsTheme(theme) {
  const isDark = computed(() => theme.isDark.value)
  const autoColor = computed(() => theme.autoColor.value)
  const lightTheme = computed(() => theme.lightTheme.value)
  const darkTheme = computed(() => theme.darkTheme.value)
  const pageTransition = computed(() => theme.pageTransition.value)
  const customColorKeys = ['primary', 'secondary', 'accent']
  const customModes = ['light', 'dark']
  const openCustomPalettes = reactive({ light: false, dark: false })
  const customDrafts = reactive({
    light: {
      name: theme.customThemes.value.light?.name || '',
      colors: [...(theme.customThemes.value.light?.colors || FALLBACK_COLORS.light)],
    },
    dark: {
      name: theme.customThemes.value.dark?.name || '',
      colors: [...(theme.customThemes.value.dark?.colors || FALLBACK_COLORS.dark)],
    },
  })
  const customSaved = reactive({ light: false, dark: false })
  const customPaletteTimers = { light: null, dark: null }
  const languages = [
    { code: 'zh-CN', name: '中文' },
    { code: 'en-US', name: 'English' },
  ]
  const lightThemes = computed(() => Object.values(theme.themes.value).filter(item => !item.isDark))
  const darkThemes = computed(() => Object.values(theme.themes.value).filter(item => item.isDark))

  function selectTheme(selected) {
    theme.setAutoColor(false)
    theme.clearOverrides()
    if (selected.isDark) {
      theme.setDarkTheme(selected.id)
      if (!isDark.value) theme.toggleDark()
    } else {
      theme.setLightTheme(selected.id)
      if (isDark.value) theme.toggleDark()
    }
  }

  function themePreviewColors(selected) {
    const active = selected.id === (isDark.value ? darkTheme.value : lightTheme.value)
    const colors = active ? { ...selected.colors, ...theme.overrides.value } : selected.colors
    return [colors['primary-color'], colors['secondary-color'], colors['accent-color']]
  }

  function customDraftValid(mode) {
    const draft = customDrafts[mode]
    return Boolean(draft.name.trim())
      && draft.colors.length === 3
      && draft.colors.every(color => /^#[0-9a-f]{6}$/i.test(color))
  }

  function customPreview(mode) {
    const colors = customDrafts[mode].colors
    return colors.every(color => /^#[0-9a-f]{6}$/i.test(color)) ? colors : FALLBACK_COLORS[mode]
  }

  function saveCustomScheme(mode) {
    if (!customDraftValid(mode)) return
    const draft = customDrafts[mode]
    const colors = draft.colors.map(color => color.toUpperCase())
    if (!theme.saveCustomTheme(mode, draft.name, colors)) return
    draft.colors = colors
    customSaved[mode] = true
    clearTimeout(customPaletteTimers[mode])
    customPaletteTimers[mode] = setTimeout(() => { customSaved[mode] = false }, 1400)
  }

  onBeforeUnmount(() => {
    clearTimeout(customPaletteTimers.light)
    clearTimeout(customPaletteTimers.dark)
  })

  return {
    isDark,
    autoColor,
    lightTheme,
    darkTheme,
    pageTransition,
    customColorKeys,
    customModes,
    openCustomPalettes,
    customDrafts,
    customSaved,
    languages,
    lightThemes,
    darkThemes,
    selectTheme,
    themePreviewColors,
    customDraftValid,
    customPreview,
    saveCustomScheme,
  }
}
