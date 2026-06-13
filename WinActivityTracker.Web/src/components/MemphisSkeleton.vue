<template>
  <div class="memphis-skeleton">
    <div class="shape shape-triangle"></div>
    <div class="shape shape-circle"></div>
    <div class="skeleton-content">
      <div class="shape shape-rect" v-for="n in lines" :key="n"></div>
    </div>
  </div>
</template>

<script setup>
defineProps({
  lines: {
    type: Number,
    default: 3
  }
})
</script>
