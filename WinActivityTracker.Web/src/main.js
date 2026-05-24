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

const apiBase = window.location.hostname === 'localhost'
  ? ''  // Vite proxy handles /api -> :5200 in dev
  : `http://${window.location.hostname}:5200`

createApp(App).use(router).provide('apiBase', apiBase).mount('#app')
