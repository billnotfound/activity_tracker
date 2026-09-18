// Memphis Theme Configuration
// This is the single source of truth for all theme colors. Edit values here to
// change the look of the entire app. No SCSS recompilation needed — themes are
// applied at runtime via CSS variables.
//
// Each theme is a flat map of CSS variable names (without `--`) to color
// values. The active theme is applied by writing each entry to
// `document.documentElement.style.setProperty`.
//
// To add a new theme:
//   1. Add an entry to THEMES below.
//   2. (Optional) Reference it from `useTheme` / settings UI.
//
// To add a new variable:
//   1. Add the key to every theme map.
//   2. Use it in SCSS via `var(--your-key, fallback)`.

// 80s Memphis — warm palette tuned to reduce visual fatigue
export const memphis80s = {
  // Core palette
  'primary-color': '#F4A261',        // Warm orange
  'secondary-color': '#E76F51',      // Coral red
  'accent-color': '#2A9D8F',         // Teal
  'success-color': '#2A9D8F',
  'warning-color': '#F4A261',
  'danger-color': '#E76F51',

  // Surfaces
  'surface-ground': '#FFFFFF',
  'surface-card': '#FFFFFF',
  'surface-100': '#F5F5F5',
  'surface-200': '#E5E5E5',
  'surface-300': '#CCCCCC',
  'surface-400': '#777777',

  // Text & borders
  'text-color': '#000000',
  'border-color': '#000000',
}

// Modern Memphis — cooler tones, softer contrast
export const memphisModern = {
  'primary-color': '#6B7FD7',        // Soft blue
  'secondary-color': '#DD7596',      // Rose pink
  'accent-color': '#4ECDC4',         // Turquoise
  'success-color': '#4ECDC4',
  'warning-color': '#6B7FD7',
  'danger-color': '#DD7596',

  'surface-ground': '#FFFFFF',
  'surface-card': '#FFFFFF',
  'surface-100': '#F5F5F5',
  'surface-200': '#E5E5E5',
  'surface-300': '#CCCCCC',
  'surface-400': '#777777',

  'text-color': '#000000',
  'border-color': '#000000',
}

// Prism — the three-color combination shown by the circular preview, promoted
// to a full theme. Accent and warning intentionally share the warm quarter so
// the interface uses the same blue / rose / amber relationship as the orb.
export const prismLight = {
  'primary-color': '#6B7FD7',
  'secondary-color': '#DD7596',
  'accent-color': '#F4A261',
  'success-color': '#6B7FD7',
  'warning-color': '#F4A261',
  'danger-color': '#DD7596',

  'surface-ground': '#FFFFFF',
  'surface-card': '#FFFFFF',
  'surface-100': '#F5F5F5',
  'surface-200': '#E5E5E5',
  'surface-300': '#CCCCCC',
  'surface-400': '#777777',

  'text-color': '#000000',
  'border-color': '#000000',
}

// Garden Mist — airy blue with leaf green and muted coral
export const gardenMist = {
  'primary-color': '#C87773',
  'secondary-color': '#77A67B',
  'accent-color': '#B7E6F0',
  'success-color': '#77A67B',
  'warning-color': '#B7E6F0',
  'danger-color': '#C87773',

  'surface-ground': '#FFFFFF',
  'surface-card': '#FFFFFF',
  'surface-100': '#F5F5F5',
  'surface-200': '#E5E5E5',
  'surface-300': '#CCCCCC',
  'surface-400': '#777777',

  'text-color': '#000000',
  'border-color': '#000000',
}

// Graphite — muted charcoal, slate blue and restrained red
export const memphisDark = {
  'primary-color': '#7895B2',
  'secondary-color': '#9AA4AE',
  'accent-color': '#657D96',
  'success-color': '#657D96',
  'warning-color': '#7895B2',
  'danger-color': '#9AA4AE',

  'surface-ground': '#000000',
  'surface-card': '#101010',
  'surface-100': '#181818',
  'surface-200': '#2B2B2B',
  'surface-300': '#424242',
  'surface-400': '#A0A0A0',

  'text-color': '#FFFFFF',
  'border-color': '#666666',
}

// GitHub Dark — familiar neutral surfaces with calm blue and red accents
export const githubDark = {
  'primary-color': '#58A6FF',
  'secondary-color': '#8B949E',
  'accent-color': '#79C0FF',
  'success-color': '#79C0FF',
  'warning-color': '#58A6FF',
  'danger-color': '#8B949E',

  'surface-ground': '#000000',
  'surface-card': '#101010',
  'surface-100': '#181818',
  'surface-200': '#2B2B2B',
  'surface-300': '#424242',
  'surface-400': '#A0A0A0',

  'text-color': '#FFFFFF',
  'border-color': '#666666',
}

// Kaf — deep aubergine, powder pink and dusky periwinkle
export const kafDark = {
  'primary-color': '#FEB4C1',
  'secondary-color': '#5A6CBC',
  'accent-color': '#3B1E3D',
  'success-color': '#5A6CBC',
  'warning-color': '#FEB4C1',
  'danger-color': '#3B1E3D',

  'surface-ground': '#000000',
  'surface-card': '#101010',
  'surface-100': '#181818',
  'surface-200': '#2B2B2B',
  'surface-300': '#424242',
  'surface-400': '#A0A0A0',

  'text-color': '#FFFFFF',
  'border-color': '#666666',
}

// All built-in themes, keyed by id.
// Add custom themes here or load them at runtime via useTheme().
export const THEMES = {
  'memphis-80s': {
    id: 'memphis-80s',
    name: '80s Memphis',
    description: 'Warm orange and teal, classic 80s vibe',
    isDark: false,
    colors: memphis80s,
  },
  'memphis-modern': {
    id: 'memphis-modern',
    name: 'Modern Memphis',
    description: 'Cool blues and turquoise, modern feel',
    isDark: false,
    colors: memphisModern,
  },
  'prism-light': {
    id: 'prism-light',
    name: 'Prism',
    description: 'The blue, rose and amber preview palette',
    isDark: false,
    colors: prismLight,
  },
  'garden-mist': {
    id: 'garden-mist',
    name: 'Garden Mist',
    description: 'Airy blue, leaf green and muted coral',
    isDark: false,
    colors: gardenMist,
  },
  'memphis-dark': {
    id: 'memphis-dark',
    name: 'Graphite',
    description: 'Muted charcoal, slate blue and soft red',
    isDark: true,
    colors: memphisDark,
  },
  'github-dark': {
    id: 'github-dark',
    name: 'GitHub Dark',
    description: 'Neutral charcoal with familiar blue accents',
    isDark: true,
    colors: githubDark,
  },
  'kaf-dark': {
    id: 'kaf-dark',
    name: 'Kaf',
    description: 'Aubergine, powder pink and periwinkle',
    isDark: true,
    colors: kafDark,
  },
}

export const DEFAULT_LIGHT_THEME = 'memphis-80s'
export const DEFAULT_DARK_THEME = 'memphis-dark'
