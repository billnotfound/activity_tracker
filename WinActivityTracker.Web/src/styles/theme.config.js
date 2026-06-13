// ============================================================
// Memphis Theme Configuration
// ============================================================
// This is the SINGLE SOURCE OF TRUTH for all theme colors.
// Edit values here to change the look of the entire app.
// No SCSS recompilation needed — themes are applied at runtime
// via CSS variables.
//
// Each theme is a flat map of CSS variable names (without `--`)
// to color values. The active theme is applied by writing each
// entry to `document.documentElement.style.setProperty`.
//
// To add a new theme:
//   1. Add an entry to THEMES below.
//   2. (Optional) Reference it from `useTheme` / settings UI.
//
// To add a new variable:
//   1. Add the key to every theme map.
//   2. Use it in SCSS via `var(--your-key, fallback)`.
// ============================================================

// 80s Memphis — warm, vibrant but refined for less visual fatigue
export const memphis80s = {
  // Core palette
  'primary-color': '#F4A261',        // Warm orange
  'secondary-color': '#E76F51',      // Coral red
  'accent-color': '#2A9D8F',         // Teal
  'success-color': '#06D6A0',        // Mint green
  'warning-color': '#9B5DE5',        // Purple
  'danger-color': '#F15BB5',         // Pink

  // Surfaces
  'surface-ground': '#FEFEFE',
  'surface-card': '#F8F9FA',
  'surface-100': '#F0F0F0',
  'surface-200': '#E0E0E0',
  'surface-300': '#D0D0D0',
  'surface-400': '#999999',

  // Text & borders
  'text-color': '#2B2D42',           // Dark blue-gray (softer than black)
  'border-color': '#2B2D42',
}

// Modern Memphis — cooler tones, softer contrast
export const memphisModern = {
  'primary-color': '#6B7FD7',        // Soft blue
  'secondary-color': '#DD7596',      // Rose pink
  'accent-color': '#4ECDC4',         // Turquoise
  'success-color': '#95E1D3',
  'warning-color': '#F38181',
  'danger-color': '#AA4465',

  'surface-ground': '#FFFFFF',
  'surface-card': '#F5F5F5',
  'surface-100': '#ECECEC',
  'surface-200': '#D8D8D8',
  'surface-300': '#C4C4C4',
  'surface-400': '#888888',

  'text-color': '#2C3E50',
  'border-color': '#34495E',
}

// Dark Memphis — reduced neon intensity, navy base
export const memphisDark = {
  'primary-color': '#FF6B9D',        // Softer pink
  'secondary-color': '#4ECDC4',      // Teal
  'accent-color': '#FCA311',         // Orange
  'success-color': '#06D6A0',
  'warning-color': '#E76F51',
  'danger-color': '#EF476F',

  'surface-ground': '#14213D',       // Navy blue
  'surface-card': '#1A2332',
  'surface-100': '#243447',
  'surface-200': '#2E3E52',
  'surface-300': '#38495D',
  'surface-400': '#6B7A8F',

  'text-color': '#E5E5E5',
  'border-color': '#4ECDC4',
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
  'memphis-dark': {
    id: 'memphis-dark',
    name: 'Dark Memphis',
    description: 'Navy base with neon accents',
    isDark: true,
    colors: memphisDark,
  },
}

export const DEFAULT_LIGHT_THEME = 'memphis-80s'
export const DEFAULT_DARK_THEME = 'memphis-dark'
