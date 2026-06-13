<template>
  <div class="memphis-card" :class="variantClass">
    <div class="card-content">
      <slot></slot>
    </div>
  </div>
</template>

<script setup>
import { computed } from 'vue'

const props = defineProps({
  variant: {
    type: String,
    default: 'primary'
  },
  showDeco: {
    type: Boolean,
    default: true
  }
})

const variantClass = computed(() => {
  const classes = []
  if (props.variant !== 'primary') {
    classes.push(`variant-${props.variant}`)
  }
  if (!props.showDeco) {
    classes.push('no-deco')
  }
  return classes.join(' ')
})
</script>
