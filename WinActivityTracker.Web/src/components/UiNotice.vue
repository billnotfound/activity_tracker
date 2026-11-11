<template>
  <div class="ui-notice" :class="tone" :role="tone === 'error' ? 'alert' : 'status'"><slot /></div>
</template>
<script setup>
defineProps({ tone: { type: String, default: 'info' } })
</script>
