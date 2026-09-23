<template>
  <svg class="palette-orb" viewBox="0 0 40 40" aria-hidden="true">
    <path d="M20 20 L20 2.5 A17.5 17.5 0 0 1 20 37.5 Z" :fill="safeColors[0]" />
    <path d="M20 20 L20 37.5 A17.5 17.5 0 0 1 2.5 20 Z" :fill="safeColors[1]" />
    <path d="M20 20 L2.5 20 A17.5 17.5 0 0 1 20 2.5 Z" :fill="safeColors[2]" />
    <circle cx="20" cy="20" r="17.5" class="orb-outline" />
  </svg>
</template>

<script setup>
import { computed } from 'vue'

const props = defineProps({
  colors: { type: Array, default: () => ['#000000', '#777777', '#FFFFFF'] },
})

const safeColors = computed(() => [0, 1, 2].map(index =>
  /^#[0-9a-f]{6}$/i.test(String(props.colors[index] || '')) ? props.colors[index] : '#777777'))
</script>

<style scoped>
.palette-orb {
  display: block;
  width: 38px;
  height: 38px;
  flex: 0 0 38px;
  overflow: visible;
}

.orb-outline {
  fill: none;
  stroke: color-mix(in srgb, var(--border-color) 62%, transparent);
  stroke-width: 1;
}
</style>
