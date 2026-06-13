// Vue Router with HTML5 history mode (no hash in URLs).
// Three routes, each lazy-loading a view component.
//
// Navigation:
//   /           Dashboard   — today's charts, media history
//   /history    History     — date-range query, aggregated data table, visual timeline
//   /tags       Tags        — tag rules editor
//   /settings   Settings    — backend configuration, DB maintenance, theme settings, third-party licenses
//
// history: createWebHistory() means the .NET Web server MUST have
// MapFallbackToFile("index.html") configured for SPA routing to work in production.
import { createRouter, createWebHistory } from 'vue-router'
import Dashboard from './views/Dashboard.vue'
import History from './views/History.vue'
import Settings from './views/Settings.vue'
import Tags from './views/Tags.vue'

export default createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/', component: Dashboard },
    { path: '/history', component: History },
    { path: '/tags', component: Tags },
    { path: '/settings', component: Settings },
  ]
})
