<template>
  <div class="time-range-picker">
    <!-- Duration display (left) — always shows actual duration, red when invalid -->
    <div class="duration-display" :class="{ 'easter-egg': !isValid }">
      <div class="duration-value">{{ signedDurationText }}</div>
      <div class="duration-label">{{ durationUnit }}</div>
    </div>

    <!-- Time wheels (right) -->
    <div class="time-wheels">
      <!-- Start time -->
      <div class="wheel-group">
        <div class="wheel-label">{{ t('history.startTime') }}</div>
        <div class="wheels-row">
          <TimeWheel v-model="startYear" :items="startYearOptions" :suffix="t('common.year')" />
          <TimeWheel v-model="startMonth" :items="startMonthOptions" :suffix="t('common.month')" />
          <TimeWheel v-model="startDay" :items="startDayOptions" :suffix="t('common.day')" />
          <TimeWheel v-model="startHour" :items="startHourOptions" :suffix="t('common.hour')" />
          <TimeWheel v-model="startMinute" :items="startMinuteOptions" :suffix="t('common.minute')" />
        </div>
      </div>

      <!-- End time -->
      <div class="wheel-group">
        <div class="wheel-label">{{ t('history.endTime') }}</div>
        <div class="wheels-row">
          <TimeWheel v-model="endYear" :items="endYearOptions" :suffix="t('common.year')" />
          <TimeWheel v-model="endMonth" :items="endMonthOptions" :suffix="t('common.month')" />
          <TimeWheel v-model="endDay" :items="endDayOptions" :suffix="t('common.day')" />
          <TimeWheel v-model="endHour" :items="endHourOptions" :suffix="t('common.hour')" />
          <TimeWheel v-model="endMinute" :items="endMinuteOptions" :suffix="t('common.minute')" />
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
    default: () => new Date(Date.now() - 3 * 60 * 60 * 1000)
  },
  endDate: {
    type: Date,
    default: () => new Date()
  },
  earliestDate: {
    type: Date,
    default: null
  }
})

const emit = defineEmits(['update:startDate', 'update:endDate', 'change'])

const now = new Date()

// Earliest bounds (from DB oldest record, or fallback to 5 years ago)
const earliestYear = computed(() =>
  props.earliestDate ? props.earliestDate.getFullYear() : now.getFullYear() - 5
)
const earliestMonth = computed(() =>
  props.earliestDate ? props.earliestDate.getMonth() + 1 : 1
)
const earliestDay = computed(() =>
  props.earliestDate ? props.earliestDate.getDate() : 1
)

// Dynamic year options
const startYearOptions = computed(() =>
  range(earliestYear.value, now.getFullYear())
)
const endYearOptions = computed(() =>
  range(earliestYear.value, now.getFullYear())
)

// Start time refs
const startYear = ref(clamp(props.startDate.getFullYear(), earliestYear.value, now.getFullYear()))
const startMonth = ref(props.startDate.getMonth() + 1)
const startDay = ref(props.startDate.getDate())
const startHour = ref(props.startDate.getHours())
const startMinute = ref(props.startDate.getMinutes())

// End time refs
const endYear = ref(clamp(props.endDate.getFullYear(), earliestYear.value, now.getFullYear()))
const endMonth = ref(props.endDate.getMonth() + 1)
const endDay = ref(props.endDate.getDate())
const endHour = ref(props.endDate.getHours())
const endMinute = ref(props.endDate.getMinutes())

// Easter egg state
const easterEggMsg = ref('')
const isValid = ref(true)

// Invalid-range easter eggs (start >= end, out of bounds)
const invalidEggKeys = [
  'history.easterEgg.reverseClock',
  'history.easterEgg.doctorStrange',
  'history.easterEgg.lightSpeed'
]

// Zero-duration easter eggs (start == end)
const zeroEggKeys = [
  'history.easterEgg.zeroDraw',
  'history.easterEgg.zeroLog',
  'history.easterEgg.zeroFine',
  'history.easterEgg.zeroHard',
  'history.easterEgg.zeroInteresting',
  'history.easterEgg.zeroWhat',
  'history.easterEgg.zeroQuestion'
]

function pickEasterEgg(durationMs) {
  const keys = durationMs === 0 ? zeroEggKeys : invalidEggKeys
  const key = keys[Math.floor(Math.random() * keys.length)]
  easterEggMsg.value = t(key)
}

// Days in month helper
function daysInMonth(year, month) {
  return new Date(year, month, 0).getDate()
}

function range(from, to) {
  const arr = []
  for (let i = from; i <= to; i++) arr.push(i)
  return arr
}

function clamp(val, min, max) {
  return Math.max(min, Math.min(max, val))
}

// Computed dates
const startDateTime = computed(() => {
  return new Date(startYear.value, startMonth.value - 1, startDay.value, startHour.value, startMinute.value)
})

const endDateTime = computed(() => {
  return new Date(endYear.value, endMonth.value - 1, endDay.value, endHour.value, endMinute.value)
})

// Context-sensitive month/day/hour/minute options for start
const startMonthOptions = computed(() => {
  const min = startYear.value === earliestYear.value ? earliestMonth.value : 1
  const max = startYear.value === now.getFullYear() ? now.getMonth() + 1 : 12
  return range(min, max)
})

const startDayOptions = computed(() => {
  const maxDay = daysInMonth(startYear.value, startMonth.value)
  let min = 1
  let max = maxDay
  if (startYear.value === earliestYear.value && startMonth.value === earliestMonth.value) {
    min = earliestDay.value
  }
  if (startYear.value === now.getFullYear() && startMonth.value === now.getMonth() + 1) {
    max = Math.min(maxDay, now.getDate())
  }
  return range(min, max)
})

const startHourOptions = computed(() => {
  const max = (startYear.value === now.getFullYear() &&
    startMonth.value === now.getMonth() + 1 &&
    startDay.value === now.getDate()) ? now.getHours() : 23
  return range(0, max)
})

const startMinuteOptions = computed(() => {
  const max = (startYear.value === now.getFullYear() &&
    startMonth.value === now.getMonth() + 1 &&
    startDay.value === now.getDate() &&
    startHour.value === now.getHours()) ? now.getMinutes() : 59
  return range(0, max)
})

// Context-sensitive month/day/hour/minute options for end
const endMonthOptions = computed(() => {
  const min = endYear.value === earliestYear.value ? earliestMonth.value : 1
  const max = endYear.value === now.getFullYear() ? now.getMonth() + 1 : 12
  return range(min, max)
})

const endDayOptions = computed(() => {
  const maxDay = daysInMonth(endYear.value, endMonth.value)
  let min = 1
  let max = maxDay
  if (endYear.value === earliestYear.value && endMonth.value === earliestMonth.value) {
    min = earliestDay.value
  }
  if (endYear.value === now.getFullYear() && endMonth.value === now.getMonth() + 1) {
    max = Math.min(maxDay, now.getDate())
  }
  return range(min, max)
})

const endHourOptions = computed(() => {
  const max = (endYear.value === now.getFullYear() &&
    endMonth.value === now.getMonth() + 1 &&
    endDay.value === now.getDate()) ? now.getHours() : 23
  return range(0, max)
})

const endMinuteOptions = computed(() => {
  const max = (endYear.value === now.getFullYear() &&
    endMonth.value === now.getMonth() + 1 &&
    endDay.value === now.getDate() &&
    endHour.value === now.getHours()) ? now.getMinutes() : 59
  return range(0, max)
})

// Clamp wheel values when options change
watch(startMonthOptions, (opts) => {
  if (!opts.includes(startMonth.value)) startMonth.value = opts[opts.length - 1]
})
watch(startDayOptions, (opts) => {
  if (!opts.includes(startDay.value)) startDay.value = opts[opts.length - 1]
})
watch(startHourOptions, (opts) => {
  if (!opts.includes(startHour.value)) startHour.value = opts[opts.length - 1]
})
watch(startMinuteOptions, (opts) => {
  if (!opts.includes(startMinute.value)) startMinute.value = opts[opts.length - 1]
})
watch(endMonthOptions, (opts) => {
  if (!opts.includes(endMonth.value)) endMonth.value = opts[opts.length - 1]
})
watch(endDayOptions, (opts) => {
  if (!opts.includes(endDay.value)) endDay.value = opts[opts.length - 1]
})
watch(endHourOptions, (opts) => {
  if (!opts.includes(endHour.value)) endHour.value = opts[opts.length - 1]
})
watch(endMinuteOptions, (opts) => {
  if (!opts.includes(endMinute.value)) endMinute.value = opts[opts.length - 1]
})

// Validation — picks easter egg on valid→invalid transition
function validate() {
  const start = startDateTime.value
  const end = endDateTime.value
  const wasValid = isValid.value

  if (start >= end) {
    isValid.value = false
    if (wasValid) pickEasterEgg(end - start)
    return false
  }
  if (start < (props.earliestDate || new Date(0))) {
    isValid.value = false
    if (wasValid) pickEasterEgg(end - start)
    return false
  }
  if (end > now) {
    isValid.value = false
    if (wasValid) pickEasterEgg(end - start)
    return false
  }
  isValid.value = true
  easterEggMsg.value = ''
  return true
}

// Duration calculation — allows negative values
const durationMs = computed(() => {
  return endDateTime.value - startDateTime.value
})

const signedDurationText = computed(() => {
  const ms = durationMs.value
  const sign = ms < 0 ? '-' : ''
  const absMs = Math.abs(ms)

  const minutes = Math.floor(absMs / 1000 / 60)
  const hours = Math.floor(minutes / 60)
  const days = Math.floor(hours / 24)

  if (days > 0) return sign + days.toString()
  else if (hours > 0) return sign + hours.toString()
  else return sign + minutes.toString()
})

const durationUnit = computed(() => {
  const absMs = Math.abs(durationMs.value)

  const minutes = Math.floor(absMs / 1000 / 60)
  const hours = Math.floor(minutes / 60)
  const days = Math.floor(hours / 24)

  if (days > 0) return t('common.day')
  else if (hours > 0) return t('common.hour')
  else return t('common.minute')
})

// Always emit — include validity info so parent can decide
function emitChange() {
  emit('update:startDate', startDateTime.value)
  emit('update:endDate', endDateTime.value)
  validate()
  emit('change', {
    start: startDateTime.value,
    end: endDateTime.value,
    valid: isValid.value,
    easterEgg: easterEggMsg.value
  })
}

watch([startYear, startMonth, startDay, startHour, startMinute], emitChange)
watch([endYear, endMonth, endDay, endHour, endMinute], emitChange)
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
  justify-content: center;
  padding: 16px 24px;
  background: var(--primary-color);
  color: white;
  border: 2px solid var(--primary-color);
  min-width: 120px;
  min-height: 100px;
  transition: background 0.3s, border-color 0.3s;

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

  &.easter-egg {
    background: var(--danger-color, #E76F51);
    border-color: var(--danger-color, #E76F51);
    animation: shake 0.5s ease-in-out;
  }
}

@keyframes shake {
  0%, 100% { transform: translateX(0); }
  20% { transform: translateX(-4px); }
  40% { transform: translateX(4px); }
  60% { transform: translateX(-4px); }
  80% { transform: translateX(4px); }
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
