// Vue Router with HTML5 history mode (no hash in URLs).
// Four routes, each lazy-loading a view component via dynamic import()
// so the initial bundle only contains the currently-visited view.
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

export default createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/', component: () => import('./views/Dashboard.vue') },
    { path: '/history', component: () => import('./views/History.vue') },
    { path: '/tags', component: () => import('./views/Tags.vue') },
    { path: '/settings', component: () => import('./views/Settings.vue') },
  ]
})
