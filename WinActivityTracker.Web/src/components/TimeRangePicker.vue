<template>
  <div class="time-range-picker">
    <!-- Duration display (left) -->
    <div class="duration-display">
      <div class="duration-value">{{ durationText }}</div>
      <div class="duration-label">{{ durationUnit }}</div>
    </div>

    <!-- Time wheels (right) -->
    <div class="time-wheels">
      <!-- Start time -->
      <div class="wheel-group">
        <div class="wheel-label">{{ t('history.startTime') }}</div>
        <div class="wheels-row">
          <TimeWheel v-model="startYear" :items="years" :suffix="t('common.year')" />
          <TimeWheel v-model="startMonth" :items="months" :suffix="t('common.month')" />
          <TimeWheel v-model="startDay" :items="days" :suffix="t('common.day')" />
          <TimeWheel v-model="startHour" :items="hours" :suffix="t('common.hour')" />
          <TimeWheel v-model="startMinute" :items="minutes" :suffix="t('common.minute')" />
        </div>
      </div>

      <!-- End time -->
      <div class="wheel-group">
        <div class="wheel-label">{{ t('history.endTime') }}</div>
        <div class="wheels-row">
          <TimeWheel v-model="endYear" :items="years" :suffix="t('common.year')" />
          <TimeWheel v-model="endMonth" :items="months" :suffix="t('common.month')" />
          <TimeWheel v-model="endDay" :items="days" :suffix="t('common.day')" />
          <TimeWheel v-model="endHour" :items="hours" :suffix="t('common.hour')" />
          <TimeWheel v-model="endMinute" :items="minutes" :suffix="t('common.minute')" />
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, computed, watch } from 'vue'
import { useI18n } from '../i18n/index.js'
import TimeWheel from './TimeWheel.vue'

const { t } = useI18n()

const props = defineProps({
  startDate: {
    type: Date,
    default: () => new Date(Date.now() - 3 * 60 * 60 * 1000) // 3 hours ago
  },
  endDate: {
    type: Date,
    default: () => new Date()
  }
})

const emit = defineEmits(['update:startDate', 'update:endDate', 'change'])

// Generate options
const years = Array.from({ length: 10 }, (_, i) => new Date().getFullYear() - 5 + i)
const months = Array.from({ length: 12 }, (_, i) => i + 1)
const days = Array.from({ length: 31 }, (_, i) => i + 1)
const hours = Array.from({ length: 24 }, (_, i) => i)
const minutes = Array.from({ length: 60 }, (_, i) => i)

// Start time
const startYear = ref(props.startDate.getFullYear())
const startMonth = ref(props.startDate.getMonth() + 1)
const startDay = ref(props.startDate.getDate())
const startHour = ref(props.startDate.getHours())
const startMinute = ref(props.startDate.getMinutes())

// End time
const endYear = ref(props.endDate.getFullYear())
const endMonth = ref(props.endDate.getMonth() + 1)
const endDay = ref(props.endDate.getDate())
const endHour = ref(props.endDate.getHours())
const endMinute = ref(props.endDate.getMinutes())

// Computed dates
const startDateTime = computed(() => {
  return new Date(startYear.value, startMonth.value - 1, startDay.value, startHour.value, startMinute.value)
})

const endDateTime = computed(() => {
  return new Date(endYear.value, endMonth.value - 1, endDay.value, endHour.value, endMinute.value)
})

// Duration calculation
const durationMs = computed(() => {
  return endDateTime.value - startDateTime.value
})

const durationText = computed(() => {
  const ms = durationMs.value
  if (ms < 0) return '0'

  const minutes = Math.floor(ms / 1000 / 60)
  const hours = Math.floor(minutes / 60)
  const days = Math.floor(hours / 24)

  if (days > 0) {
    return days.toString()
  } else if (hours > 0) {
    return hours.toString()
  } else {
    return minutes.toString()
  }
})

const durationUnit = computed(() => {
  const ms = durationMs.value
  if (ms < 0) return t('common.minute')

  const minutes = Math.floor(ms / 1000 / 60)
  const hours = Math.floor(minutes / 60)
  const days = Math.floor(hours / 24)

  if (days > 0) {
    return t('common.day')
  } else if (hours > 0) {
    return t('common.hour')
  } else {
    return t('common.minute')
  }
})

// Watch for changes and emit
watch([startYear, startMonth, startDay, startHour, startMinute], () => {
  emit('update:startDate', startDateTime.value)
  emit('change', { start: startDateTime.value, end: endDateTime.value })
})

watch([endYear, endMonth, endDay, endHour, endMinute], () => {
  emit('update:endDate', endDateTime.value)
  emit('change', { start: startDateTime.value, end: endDateTime.value })
})
</script>

<style lang="scss" scoped>
.time-range-picker {
  display: flex;
  align-items: center;
  gap: 24px;
  padding: 20px;
  background: var(--surface-card);
  border: 2px solid var(--surface-200);
}

.duration-display {
  display: flex;
  flex-direction: column;
  align-items: center;
  padding: 16px 24px;
  background: var(--primary-color);
  color: white;
  border: 2px solid var(--primary-color);
  min-width: 120px;

  .duration-value {
    font-size: 2.5rem;
    font-weight: 700;
    line-height: 1;
  }

  .duration-label {
    font-size: 1rem;
    font-weight: 600;
    text-transform: uppercase;
    letter-spacing: 1px;
    margin-top: 8px;
  }
}

.time-wheels {
  flex: 1;
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.wheel-group {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.wheel-label {
  font-size: 0.9rem;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.5px;
  color: var(--text-color);
}

.wheels-row {
  display: flex;
  gap: 8px;
}

@media (max-width: 768px) {
  .time-range-picker {
    flex-direction: column;
  }

  .duration-display {
    width: 100%;
  }

  .wheels-row {
    flex-wrap: wrap;
  }
}
</style>
