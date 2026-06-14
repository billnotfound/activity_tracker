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
  let data = null

  // Try requested locale
  try {
    const resp = await fetch(`/i18n/${lang}.json`)
    if (resp.ok) {
      data = await resp.json()
      console.log(`[i18n] Loaded ${lang}: ${Object.keys(data).length} keys, ${resp.headers.get('content-type') || 'no content-type'}`)
    } else {
      console.warn(`[i18n] HTTP ${resp.status} for ${lang}, content-type: ${resp.headers.get('content-type')}`)
    }
  } catch (e) {
    console.warn(`[i18n] Failed to load ${lang}:`, e.message || e)
  }

  // Fallback to en-US
  if (!data && lang !== 'en-US') {
    try {
      const resp = await fetch('/i18n/en-US.json')
      if (resp.ok) {
        data = await resp.json()
        console.log(`[i18n] Loaded en-US fallback: ${Object.keys(data).length} keys`)
      } else {
        console.warn(`[i18n] en-US fallback HTTP ${resp.status}`)
      }
    } catch (e2) {
      console.warn('[i18n] en-US fallback failed:', e2.message || e2)
    }
  }

  // Only mutate messages when we have valid data — never leave it empty
  if (data && typeof data === 'object' && Object.keys(data).length > 0) {
    Object.keys(messages).forEach(k => delete messages[k])
    Object.assign(messages, data)
    console.log(`[i18n] messages now has ${Object.keys(messages).length} keys`)
  } else if (!data) {
    console.warn(`[i18n] No messages loaded for ${lang}, keeping existing (${Object.keys(messages).length} keys)`)
  }
}

// Re-initialize after HMR to avoid empty messages
if (import.meta.hot) {
  import.meta.hot.accept(() => {
    initI18n()
  })
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
