<template>
  <svg class="palette-orb" viewBox="0 0 40 40" aria-hidden="true">
    <circle cx="20" cy="20" r="18.25" class="orb-outline" />
    <g class="orb-sectors" transform="rotate(-90 20 20)">
      <circle cx="20" cy="20" r="13.5" pathLength="100" :stroke="safeColors[0]" stroke-dasharray="50 50" stroke-dashoffset="0" />
      <circle cx="20" cy="20" r="13.5" pathLength="100" :stroke="safeColors[1]" stroke-dasharray="25 75" stroke-dashoffset="-50" />
      <circle cx="20" cy="20" r="13.5" pathLength="100" :stroke="safeColors[2]" stroke-dasharray="25 75" stroke-dashoffset="-75" />
    </g>
    <circle cx="20" cy="20" r="8" class="orb-center" />
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
  fill: var(--surface-card);
  stroke: color-mix(in srgb, var(--border-color) 62%, transparent);
  stroke-width: 1.5;
}

.orb-sectors circle {
  fill: none;
  stroke-width: 10;
  stroke-linecap: butt;
}

.orb-center {
  fill: var(--surface-card);
  stroke: color-mix(in srgb, var(--border-color) 45%, transparent);
  stroke-width: 1;
}
</style>
