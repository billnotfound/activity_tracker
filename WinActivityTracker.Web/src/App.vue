<!--
  Root Vue component — Bootstrap 5 navbar + router-view outlet.
  Fixed-top navbar requires padding-top on the body (56px default Bootstrap navbar height).
-->
<template>
  <div>
    <nav class="navbar navbar-expand-lg navbar-dark bg-dark fixed-top">
      <div class="container">
        <router-link class="navbar-brand" to="/">WinActivityTracker</router-link>
        <div class="navbar-nav">
          <router-link class="nav-link" to="/">{{ t('nav.dashboard') }}</router-link>
          <router-link class="nav-link" to="/history">{{ t('nav.history') }}</router-link>
          <router-link class="nav-link" to="/tags">{{ t('nav.tags') }}</router-link>
          <router-link class="nav-link" to="/timeline">{{ t('nav.timeline') }}</router-link>
          <router-link class="nav-link" to="/settings">{{ t('nav.settings') }}</router-link>
        </div>
      </div>
    </nav>
    <div class="container mt-3" style="padding-top:56px">
      <router-view />
    </div>
  </div>
</template>

<script setup>
import { useI18n } from './i18n/index.js'
const { t } = useI18n()
</script>
