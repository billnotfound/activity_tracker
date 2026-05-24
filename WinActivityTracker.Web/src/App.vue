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
          <router-link class="nav-link" to="/">仪表盘</router-link>
          <router-link class="nav-link" to="/history">历史</router-link>
          <router-link class="nav-link" to="/timeline">时间线</router-link>
          <router-link class="nav-link" to="/settings">设置</router-link>
        </div>
      </div>
    </nav>
    <div class="container mt-3" style="padding-top:56px">
      <router-view />
    </div>
  </div>
</template>
