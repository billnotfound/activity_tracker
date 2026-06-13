// Vue application entry point.
//
// API base URL logic:
//   - localhost dev: '' (Vite proxy in vite.config.js handles /api → :5200)
//   - production: 'http://<hostname>:5200' (direct connection to the backend)
//
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

const apiBase = window.location.hostname === 'localhost'
  ? ''  // Vite proxy handles /api -> :5200 in dev
  : `http://${window.location.hostname}:5200`

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
