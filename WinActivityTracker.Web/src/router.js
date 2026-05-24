// Vue Router with HTML5 history mode (no hash in URLs).
// Four routes, each lazy-loading a view component.
//
// Navigation:
//   /           Dashboard   — today's charts, media history
//   /history    History     — date-range query, aggregated data table
//   /timeline   Timeline    — focus change log, live visible windows
//   /settings   Settings    — backend configuration, DB maintenance
//
// history: createWebHistory() means the .NET Web server MUST have
// MapFallbackToFile("index.html") configured for SPA routing to work in production.
import { createRouter, createWebHistory } from 'vue-router'
import Dashboard from './views/Dashboard.vue'
import History from './views/History.vue'
import Timeline from './views/Timeline.vue'
import Settings from './views/Settings.vue'

export default createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/', component: Dashboard },
    { path: '/history', component: History },
    { path: '/timeline', component: Timeline },
    { path: '/settings', component: Settings },
  ]
})
