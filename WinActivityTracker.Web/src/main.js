// Vue application entry point.
//
// API base: always uses relative URLs (''). In development, Vite's proxy
// (vite.config.js) forwards /api → backend. In production, the SPA is served
// from the same Kestrel origin, so /api resolves to the correct port automatically.
// The apiBase is provided to all components via Vue's provide/inject system.
// Components access it with: const apiBase = inject('apiBase')
import { createApp } from 'vue'
import App from './App.vue'
import router from './router.js'
import { initI18n } from './i18n/index.js'

// PrimeVue imports
import PrimeVue from 'primevue/config'
import 'primeicons/primeicons.css'

// Memphis styles
import './styles/index.scss'

// Theme management
import { useTheme } from './composables/useTheme.js'

const apiBase = ''

async function boot() {
  await initI18n()

  // Apply theme BEFORE mounting so the first paint already has correct colors
  const { applyTheme } = useTheme()
  applyTheme()

  const app = createApp(App)

  app.use(router)
  app.use(PrimeVue, {
    ripple: true,
    unstyled: false
  })
  app.provide('apiBase', apiBase)

  app.mount('#app')
}
boot()
