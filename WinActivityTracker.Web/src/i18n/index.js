import { ref, reactive } from 'vue'

// i18n messages are bundled at build time (Vite JSON imports) instead of
// fetched at runtime, so first paint doesn't block on a /i18n/*.json round-trip
// — important here because DashboardServer starts on-demand and the first
// dashboard open already pays a Kestrel cold-start.
import zhCN from './zh-CN.json'
import enUS from './en-US.json'

const BUNDLED = {
  'zh-CN': zhCN,
  'en-US': enUS,
}

const locale = ref('zh-CN')
export const messages = reactive({})

function detectLanguage() {
  const stored = localStorage.getItem('locale')
  if (stored) return stored
  const lang = navigator.language || navigator.userLanguage || 'zh-CN'
  if (lang.startsWith('zh')) return 'zh-CN'
  return 'en-US'
}

export async function initI18n() {
  locale.value = detectLanguage()
  loadMessages(locale.value)
}

function loadMessages(lang) {
  const data = BUNDLED[lang] || BUNDLED['en-US']
  if (data && typeof data === 'object' && Object.keys(data).length > 0) {
    Object.keys(messages).forEach(k => delete messages[k])
    Object.assign(messages, data)
  }
}

// Re-initialize after HMR to avoid empty messages
if (import.meta.hot) {
  import.meta.hot.accept(async () => {
    await initI18n()
  })
}

// Standalone t() — usable from non-component modules (e.g. utils/time.js)
// without going through useI18n(). Reads the same shared `messages` object.
export function t(key, args = {}) {
  let msg = messages[key]
  if (msg === undefined) return key
  if (typeof args === 'object') {
    for (const [k, v] of Object.entries(args))
      msg = msg.split(`{${k}}`).join(String(v))
  }
  return msg
}

async function setLocale(lang) {
  locale.value = lang
  localStorage.setItem('locale', lang)
  loadMessages(lang)
}

export function useI18n() {
  return { t, setLocale, locale }
}
