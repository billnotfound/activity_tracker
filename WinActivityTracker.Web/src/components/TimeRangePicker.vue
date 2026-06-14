<template>
  <div class="time-range-picker">
    <!-- Duration display -->
    <div class="duration-display" :class="{ 'easter-egg': !isValid }">
      <div class="duration-value">{{ signedDurationText }}</div>
      <div class="duration-label">{{ durationUnit }}</div>
    </div>

    <!-- Arrow + time wheels -->
    <div class="time-area">
      <!-- Start time wheels -->
      <div class="wheels-row">
        <TimeWheel v-model="startYear"   :items="startYearOptions"   wide :label="t('common.year')"    @carry="(d) => carry('start','year',d)" />
        <TimeWheel v-model="startMonth"  :items="startMonthOptions"       :label="t('common.month')"   @carry="(d) => carry('start','month',d)" />
        <TimeWheel v-model="startDay"    :items="startDayOptions"         :label="t('common.day')"     @carry="(d) => carry('start','day',d)" />
        <TimeWheel v-model="startHour"   :items="startHourOptions"        :label="t('common.hour')"    @carry="(d) => carry('start','hour',d)" />
        <TimeWheel v-model="startMinute" :items="startMinuteOptions"      :label="t('common.minute')"  @carry="(d) => carry('start','minute',d)" />
      </div>

      <!-- Arrow: horizontal by default, vertical on narrow -->
      <div class="arrow-wrap">
        <svg class="arrow-h" viewBox="0 0 48 20" width="48" height="20">
          <line x1="0" y1="10" x2="38" y2="10" stroke-width="2.5" stroke="currentColor" />
          <polygon points="34,3 46,10 34,17" fill="currentColor" />
        </svg>
        <svg class="arrow-v" viewBox="0 0 20 48" width="20" height="48">
          <line x1="10" y1="0" x2="10" y2="38" stroke-width="2.5" stroke="currentColor" />
          <polygon points="3,34 10,46 17,34" fill="currentColor" />
        </svg>
      </div>

      <!-- End time wheels -->
      <div class="wheels-row">
        <TimeWheel v-model="endYear"   :items="endYearOptions"   wide :label="t('common.year')"    @carry="(d) => carry('end','year',d)" />
        <TimeWheel v-model="endMonth"  :items="endMonthOptions"       :label="t('common.month')"   @carry="(d) => carry('end','month',d)" />
        <TimeWheel v-model="endDay"    :items="endDayOptions"         :label="t('common.day')"     @carry="(d) => carry('end','day',d)" />
        <TimeWheel v-model="endHour"   :items="endHourOptions"        :label="t('common.hour')"    @carry="(d) => carry('end','hour',d)" />
        <TimeWheel v-model="endMinute" :items="endMinuteOptions"      :label="t('common.minute')"  @carry="(d) => carry('end','minute',d)" />
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

// Earliest bounds
const earliestYear = computed(() =>
  props.earliestDate ? props.earliestDate.getFullYear() : now.getFullYear() - 5
)
const earliestMonth = computed(() =>
  props.earliestDate ? props.earliestDate.getMonth() + 1 : 1
)
const earliestDay = computed(() =>
  props.earliestDate ? props.earliestDate.getDate() : 1
)

// Year options
const startYearOptions = computed(() => range(earliestYear.value, now.getFullYear()))
const endYearOptions = computed(() => range(earliestYear.value, now.getFullYear()))

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

// Invalid-range easter eggs
const invalidEggKeys = [
  'history.easterEgg.reverseClock',
  'history.easterEgg.doctorStrange',
  'history.easterEgg.lightSpeed'
]

// Zero-duration easter eggs
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

// Context-sensitive option lists for START
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

// Context-sensitive option lists for END
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

// ── Auto-carry / borrow ──

const sideRefs = {
  start: { year: startYear, month: startMonth, day: startDay, hour: startHour, minute: startMinute },
  end:   { year: endYear,   month: endMonth,   day: endDay,   hour: endHour,   minute: endMinute }
}

function carry(side, unit, delta) {
  const date = side === 'start' ? startDateTime.value : endDateTime.value
  const newDate = new Date(date)

  switch (unit) {
    case 'minute': newDate.setMinutes(newDate.getMinutes() + delta); break
    case 'hour':   newDate.setHours(newDate.getHours() + delta); break
    case 'day':    newDate.setDate(newDate.getDate() + delta); break
    case 'month':
      // Avoid JS date auto-correction (Jan 31 → Feb → Mar 3).
      // Pin to day 1 before month shift, then restore day clamped to new month.
      {
        const d = newDate.getDate()
        newDate.setDate(1)
        newDate.setMonth(newDate.getMonth() + delta)
        const maxD = daysInMonth(newDate.getFullYear(), newDate.getMonth() + 1)
        newDate.setDate(Math.min(d, maxD))
      }
      break
    case 'year':
      // Same issue: Feb 29 in leap year → next year Feb 29 → Mar 1.
      {
        const m = newDate.getMonth()
        const d = newDate.getDate()
        newDate.setFullYear(newDate.getFullYear() + delta)
        // If we were on Feb 29 of a leap year, restore to Feb 28 in non-leap year
        if (m === 1 && d === 29 && daysInMonth(newDate.getFullYear(), 2) === 28) {
          newDate.setMonth(1)
          newDate.setDate(28)
        }
      }
      break
  }

  // Validate against bounds
  const earliest = props.earliestDate || new Date(0)
  if (newDate < earliest) return
  if (newDate > now) return

  if (side === 'start' && newDate >= endDateTime.value) return
  if (side === 'end'   && newDate <= startDateTime.value) return

  // Valid — update all refs
  const refs = sideRefs[side]
  refs.year.value   = newDate.getFullYear()
  refs.month.value  = newDate.getMonth() + 1
  refs.day.value    = newDate.getDate()
  refs.hour.value   = newDate.getHours()
  refs.minute.value = newDate.getMinutes()
}

// Clamp wheel values when options change
watch(startMonthOptions, (opts) => { if (!opts.includes(startMonth.value)) startMonth.value = opts[opts.length - 1] })
watch(startDayOptions, (opts) => { if (!opts.includes(startDay.value)) startDay.value = opts[opts.length - 1] })
watch(startHourOptions, (opts) => { if (!opts.includes(startHour.value)) startHour.value = opts[opts.length - 1] })
watch(startMinuteOptions, (opts) => { if (!opts.includes(startMinute.value)) startMinute.value = opts[opts.length - 1] })
watch(endMonthOptions, (opts) => { if (!opts.includes(endMonth.value)) endMonth.value = opts[opts.length - 1] })
watch(endDayOptions, (opts) => { if (!opts.includes(endDay.value)) endDay.value = opts[opts.length - 1] })
watch(endHourOptions, (opts) => { if (!opts.includes(endHour.value)) endHour.value = opts[opts.length - 1] })
watch(endMinuteOptions, (opts) => { if (!opts.includes(endMinute.value)) endMinute.value = opts[opts.length - 1] })

// Validation
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

const durationMs = computed(() => endDateTime.value - startDateTime.value)

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
  gap: 20px;
  padding: 16px 20px;
  background: var(--surface-card);
  border: 2px solid var(--surface-200);
  min-height: 86px;
  contain: layout style;
}

// ── Duration display ──

.duration-display {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 12px 20px;
  background: var(--primary-color);
  color: white;
  min-width: 100px;
  min-height: 90px;
  flex-shrink: 0;
  contain: layout style;

  .duration-value {
    font-size: 2.2rem;
    font-weight: 700;
    line-height: 1;
  }

  .duration-label {
    font-size: 0.85rem;
    font-weight: 600;
    text-transform: uppercase;
    letter-spacing: 1px;
    margin-top: 6px;
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

// ── Time area (wheels + arrow) ──

.time-area {
  flex: 1;
  display: flex;
  align-items: center;
  gap: 10px;
  contain: layout style;
}

.wheels-row {
  display: flex;
  gap: 4px;
  flex-shrink: 0;
  contain: layout style;
}

// ── Arrow ──

.arrow-wrap {
  flex-shrink: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  color: var(--primary-color);

  .arrow-v {
    display: none;
  }
}

// ── Responsive: stack vertically ──

@media (max-width: 900px) {
  .time-range-picker {
    flex-direction: column;
    min-height: auto;
  }

  .duration-display {
    width: 100%;
    min-height: 70px;
    flex-direction: row;
    gap: 12px;

    .duration-value { font-size: 1.6rem; }
    .duration-label { margin-top: 0; }
  }

  .time-area {
    flex-direction: column;
  }

  .arrow-wrap {
    .arrow-h { display: none; }
    .arrow-v { display: block; }
  }
}

@media (max-width: 520px) {
  .time-range-picker {
    padding: 10px;
    gap: 10px;
  }

  .wheels-row {
    gap: 2px;
  }

  .time-area {
    gap: 6px;
  }
}
</style>
