import { ref, reactive } from 'vue'

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
  await loadMessages(locale.value)
}

async function loadMessages(lang) {
  try {
    const resp = await fetch(`/i18n/${lang}.json`)
    if (!resp.ok) throw new Error(`HTTP ${resp.status}`)
    const data = await resp.json()
    Object.keys(messages).forEach(k => delete messages[k])
    Object.assign(messages, data)
  } catch {
    if (lang !== 'en-US') {
      try {
        const resp = await fetch('/i18n/en-US.json')
        if (resp.ok) {
          const data = await resp.json()
          Object.keys(messages).forEach(k => delete messages[k])
          Object.assign(messages, data)
        }
      } catch { /* silent */ }
    }
  }
}

export function useI18n() {
  function t(key, args = {}) {
    let msg = messages[key]
    if (msg === undefined) return key
    if (typeof args === 'object') {
      for (const [k, v] of Object.entries(args))
        msg = msg.replace(new RegExp(`\\{${k}\\}`, 'g'), String(v))
    }
    return msg
  }

  async function setLocale(lang) {
    locale.value = lang
    localStorage.setItem('locale', lang)
    await loadMessages(lang)
  }

  return { t, setLocale, locale }
}
