<template>
  <div class="time-range-picker">
    <!-- Duration: scrollable value + static i18n unit label -->
    <div class="duration-display" ref="durationEl" :class="{ 'easter-egg': !isValid }">
      <TimeWheel
        v-model="durationNum"
        :items="durationNumOptions"
        frameless
        class="duration-value-wheel"
        @carry="(d) => carryDuration(d)"
      />
      <span class="duration-unit-label">{{ durationUnitLabel }}</span>
    </div>

    <!-- Arrow + wheels row -->
    <div class="picker-row">
      <!-- Start time wheels (left of arrow) -->
      <div class="wheels-row">
        <TimeWheel v-model="startYear"   :items="startYearOptions"   wide :label="t('common.year')"    @carry="(d) => carry('start','year',d)" />
        <TimeWheel v-model="startMonth"  :items="startMonthOptions"       :label="t('common.month')"   @carry="(d) => carry('start','month',d)" />
        <TimeWheel v-model="startDay"    :items="startDayOptions"         :label="t('common.day')"     @carry="(d) => carry('start','day',d)" />
        <TimeWheel v-model="startHour"   :items="startHourOptions"        :label="t('common.hour')"    @carry="(d) => carry('start','hour',d)" />
        <TimeWheel v-model="startMinute" :items="startMinuteOptions"      :label="t('common.minute')"  @carry="(d) => carry('start','minute',d)" />
      </div>

      <!-- Arrow: width follows duration text width, animated.
           Width is placed on the wrapper so flex layout smoothly
           repositions the adjacent wheels during the transition. -->
      <div class="arrow-wrap" :style="{ width: arrowWidth + 'px' }">
        <svg class="arrow-h arrow-svg" width="100%" height="20" :viewBox="`0 0 ${Math.max(48, arrowWidth)} 20`">
          <line :x1="2" y1="10" :x2="Math.max(48, arrowWidth) - 14" y2="10" stroke="currentColor" stroke-width="2.5" stroke-linecap="round"/>
          <polyline :points="`${Math.max(48, arrowWidth) - 14},3 ${Math.max(48, arrowWidth) - 2},10 ${Math.max(48, arrowWidth) - 14},17`" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"/>
        </svg>
        <svg class="arrow-v arrow-svg" :height="arrowWidth" width="20" :viewBox="`0 0 20 ${Math.max(48, arrowWidth)}`">
          <line x1="10" :y1="2" x2="10" :y2="Math.max(48, arrowWidth) - 14" stroke="currentColor" stroke-width="2.5" stroke-linecap="round"/>
          <polyline :points="`3,${Math.max(48, arrowWidth) - 14} 10,${Math.max(48, arrowWidth) - 2} 17,${Math.max(48, arrowWidth) - 14}`" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"/>
        </svg>
      </div>

      <!-- End time wheels (right of arrow) -->
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
import { ref, computed, watch, onMounted, nextTick } from 'vue'
import { useI18n } from '../i18n/index.js'
import TimeWheel from './TimeWheel.vue'

const { t } = useI18n()

const props = defineProps({
  startDate: { type: Date, default: () => new Date(Date.now() - 3 * 60 * 60 * 1000) },
  endDate: { type: Date, default: () => new Date() },
  earliestDate: { type: Date, default: null }
})

const emit = defineEmits(['update:startDate', 'update:endDate', 'change'])

const now = new Date()

const earliestYear = computed(() =>
  props.earliestDate ? props.earliestDate.getFullYear() : now.getFullYear() - 5
)
const earliestMonth = computed(() =>
  props.earliestDate ? props.earliestDate.getMonth() + 1 : 1
)
const earliestDay = computed(() =>
  props.earliestDate ? props.earliestDate.getDate() : 1
)

const startYearOptions = computed(() => range(earliestYear.value, now.getFullYear()))
const endYearOptions = computed(() => range(earliestYear.value, now.getFullYear()))

const startYear = ref(clamp(props.startDate.getFullYear(), earliestYear.value, now.getFullYear()))
const startMonth = ref(props.startDate.getMonth() + 1)
const startDay = ref(props.startDate.getDate())
const startHour = ref(props.startDate.getHours())
const startMinute = ref(props.startDate.getMinutes())

const endYear = ref(clamp(props.endDate.getFullYear(), earliestYear.value, now.getFullYear()))
const endMonth = ref(props.endDate.getMonth() + 1)
const endDay = ref(props.endDate.getDate())
const endHour = ref(props.endDate.getHours())
const endMinute = ref(props.endDate.getMinutes())

const easterEggMsg = ref('')
const isValid = ref(true)

const invalidEggKeys = [
  'history.easterEgg.reverseClock',
  'history.easterEgg.doctorStrange',
  'history.easterEgg.lightSpeed'
]

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

const startDateTime = computed(() => {
  return new Date(startYear.value, startMonth.value - 1, startDay.value, startHour.value, startMinute.value)
})

const endDateTime = computed(() => {
  return new Date(endYear.value, endMonth.value - 1, endDay.value, endHour.value, endMinute.value)
})

// ── Duration wheels ──

const durationNum = ref(1)
const durationUnitWheel = ref('Min')
const durationUnitOptions = ['Min', 'H', 'D']

const unitI18nKeys = {
  Min: 'common.minute',
  H: 'common.hour',
  D: 'common.day'
}

const durationUnitLabel = computed(() => t(unitI18nKeys[durationUnitWheel.value] || 'common.minute'))

// Options capped at natural carry thresholds — 60m→1h, 24h→1d.
// When the user scrolls past the cap, TimeWheel fires @carry;
// carryDuration then converts to the next-larger unit.
const UNIT_CAPS = { Min: 60, H: 24, D: 200 }
const durationNumOptions = computed(() => {
  const earliest = props.earliestDate ? props.earliestDate.getTime() : 0
  const maxMs = now.getTime() - earliest
  if (maxMs <= 0) return [1]
  let max
  switch (durationUnitWheel.value) {
    case 'Min': max = Math.floor(maxMs / 60000); break
    case 'H':   max = Math.floor(maxMs / 3600000); break
    case 'D':   max = Math.floor(maxMs / 86400000); break
  }
  const cap = UNIT_CAPS[durationUnitWheel.value] || 200
  return range(1, Math.min(cap, Math.max(1, max)))
})

watch(durationNumOptions, (opts) => {
  if (!opts.includes(durationNum.value)) {
    carryGuard = true
    durationNum.value = opts[opts.length - 1]
    nextTick(() => { carryGuard = false })
  }
})

watch(durationUnitWheel, () => {
  const opts = durationNumOptions.value
  if (!opts.includes(durationNum.value)) {
    carryGuard = true
    durationNum.value = opts[opts.length - 1]
    nextTick(() => { carryGuard = false })
  }
})

let suppressDateSync = false
let lastCarryDelta = 0
let carryGuard = false

function syncDurationFromDates() {
  const ms = endDateTime.value.getTime() - startDateTime.value.getTime()
  const absMs = Math.max(0, ms)
  const minutes = Math.floor(absMs / 60000)
  const hours = Math.floor(minutes / 60)
  const days = Math.floor(hours / 24)

  carryGuard = true
  if (days > 0) {
    durationUnitWheel.value = 'D'
    durationNum.value = days
  } else if (hours > 0) {
    durationUnitWheel.value = 'H'
    durationNum.value = Math.max(1, hours)
  } else {
    durationUnitWheel.value = 'Min'
    durationNum.value = Math.max(1, minutes)
  }
  nextTick(() => { carryGuard = false })
}

function setStartDate(ts) {
  const earliest = props.earliestDate ? props.earliestDate.getTime() : 0
  const start = new Date(Math.max(earliest, ts))
  const refs = sideRefs.start
  refs.year.value   = start.getFullYear()
  refs.month.value  = start.getMonth() + 1
  refs.day.value    = start.getDate()
  refs.hour.value   = start.getHours()
  refs.minute.value = start.getMinutes()
}

function setEndDate(ts) {
  const end = new Date(ts > now.getTime() ? now.getTime() : ts)
  const refs = sideRefs.end
  refs.year.value   = end.getFullYear()
  refs.month.value  = end.getMonth() + 1
  refs.day.value    = end.getDate()
  refs.hour.value   = end.getHours()
  refs.minute.value = end.getMinutes()
}

function applyDurationToRange() {
  suppressDateSync = true
  nextTick(() => { suppressDateSync = false })

  let ms = durationNum.value
  switch (durationUnitWheel.value) {
    case 'Min': ms *= 60000; break
    case 'H':   ms *= 3600000; break
    case 'D':   ms *= 86400000; break
  }

  const currentStart = startDateTime.value.getTime()
  const currentEnd = endDateTime.value.getTime()
  const currentDuration = Math.max(1, currentEnd - currentStart)
  const earliest = props.earliestDate ? props.earliestDate.getTime() : 0

  const increasing = lastCarryDelta !== 0 ? lastCarryDelta > 0 : ms > currentDuration

  if (increasing) {
    const newEnd = currentStart + ms
    if (newEnd <= now.getTime()) {
      setEndDate(newEnd)
    } else {
      setEndDate(now.getTime())
      setStartDate(now.getTime() - ms)
    }
  } else {
    const newStart = currentEnd - ms
    if (newStart >= earliest) {
      setStartDate(newStart)
    } else {
      setStartDate(earliest)
      const altEnd = earliest + ms
      setEndDate(Math.min(altEnd, now.getTime()))
    }
  }
}

function carryDuration(direction) {
  if (carryGuard) return
  const units = durationUnitOptions
  const idx = units.indexOf(durationUnitWheel.value)

  if (direction > 0 && idx < units.length - 1) {
    // Carry UP to next larger unit
    carryGuard = true
    lastCarryDelta = 1
    const currentVal = durationNum.value
    let converted = currentVal
    if (durationUnitWheel.value === 'Min') converted = Math.floor(converted / 60)
    else if (durationUnitWheel.value === 'H') converted = Math.floor(converted / 24)
    durationUnitWheel.value = units[idx + 1]
    const newOpts = durationNumOptions.value
    durationNum.value = clamp(converted, 1, newOpts[newOpts.length - 1])
    nextTick(() => { carryGuard = false })
  } else if (direction < 0 && idx > 0) {
    // Carry DOWN to next smaller unit
    carryGuard = true
    lastCarryDelta = -1
    const currentVal = durationNum.value
    let converted = currentVal
    if (durationUnitWheel.value === 'D') converted *= 24
    else if (durationUnitWheel.value === 'H') converted *= 60
    durationUnitWheel.value = units[idx - 1]
    const newOpts = durationNumOptions.value
    durationNum.value = clamp(converted, 1, newOpts[newOpts.length - 1])
    nextTick(() => { carryGuard = false })
  }
}

// Auto-carry when the value reaches the boundary via normal scrolling.
// flush: 'sync' is essential — without it, rapid drag fires multiple tryChange()
// calls before the watcher runs, and each call reads stale props.modelValue
// (parent hasn't re-rendered), so they all compute the same next value.
watch(durationNum, (newVal) => {
  if (carryGuard) return
  const opts = durationNumOptions.value
  const units = durationUnitOptions
  const idx = units.indexOf(durationUnitWheel.value)

  if (newVal === opts[opts.length - 1] && idx < units.length - 1) {
    carryDuration(1)
  } else if (newVal === 1 && idx > 0) {
    carryDuration(-1)
  }
}, { flush: 'sync' })

watch([durationNum, durationUnitWheel], () => {
  applyDurationToRange()
  lastCarryDelta = 0
  emitChange()
})

watch([startDateTime, endDateTime], () => {
  if (suppressDateSync) return
  syncDurationFromDates()
})

// ── Context-sensitive option lists for START ──

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
      {
        const d = newDate.getDate()
        newDate.setDate(1)
        newDate.setMonth(newDate.getMonth() + delta)
        const maxD = daysInMonth(newDate.getFullYear(), newDate.getMonth() + 1)
        newDate.setDate(Math.min(d, maxD))
      }
      break
    case 'year':
      {
        const m = newDate.getMonth()
        const d = newDate.getDate()
        newDate.setFullYear(newDate.getFullYear() + delta)
        if (m === 1 && d === 29 && daysInMonth(newDate.getFullYear(), 2) === 28) {
          newDate.setMonth(1)
          newDate.setDate(28)
        }
      }
      break
  }

  const earliest = props.earliestDate || new Date(0)
  if (newDate < earliest) return
  if (newDate > now) return

  if (side === 'start' && newDate >= endDateTime.value) return
  if (side === 'end'   && newDate <= startDateTime.value) return

  const refs = sideRefs[side]
  refs.year.value   = newDate.getFullYear()
  refs.month.value  = newDate.getMonth() + 1
  refs.day.value    = newDate.getDate()
  refs.hour.value   = newDate.getHours()
  refs.minute.value = newDate.getMinutes()
}

watch(startMonthOptions, (opts) => { if (!opts.includes(startMonth.value)) startMonth.value = opts[opts.length - 1] })
watch(startDayOptions, (opts) => { if (!opts.includes(startDay.value)) startDay.value = opts[opts.length - 1] })
watch(startHourOptions, (opts) => { if (!opts.includes(startHour.value)) startHour.value = opts[opts.length - 1] })
watch(startMinuteOptions, (opts) => { if (!opts.includes(startMinute.value)) startMinute.value = opts[opts.length - 1] })
watch(endMonthOptions, (opts) => { if (!opts.includes(endMonth.value)) endMonth.value = opts[opts.length - 1] })
watch(endDayOptions, (opts) => { if (!opts.includes(endDay.value)) endDay.value = opts[opts.length - 1] })
watch(endHourOptions, (opts) => { if (!opts.includes(endHour.value)) endHour.value = opts[opts.length - 1] })
watch(endMinuteOptions, (opts) => { if (!opts.includes(endMinute.value)) endMinute.value = opts[opts.length - 1] })

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

// ── Arrow width follows duration display width ──

const durationEl = ref(null)
const arrowWidth = ref(48)

function updateArrowWidth() {
  if (durationEl.value) {
    arrowWidth.value = Math.max(48, durationEl.value.offsetWidth)
  }
}

watch([durationNum, durationUnitWheel], updateArrowWidth, { flush: 'post' })

onMounted(() => {
  nextTick(updateArrowWidth)
})

// ── Emit ──

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

// ── Initialise duration wheels from props ──

carryGuard = true
const initMs = props.endDate.getTime() - props.startDate.getTime()
const initMin = Math.floor(Math.max(0, initMs) / 60000)
const initHr = Math.floor(initMin / 60)
const initDay = Math.floor(initHr / 24)
if (initDay > 0) {
  durationNum.value = initDay
  durationUnitWheel.value = 'D'
} else if (initHr > 0) {
  durationNum.value = Math.max(1, initHr)
  durationUnitWheel.value = 'H'
} else {
  durationNum.value = Math.max(1, initMin)
  durationUnitWheel.value = 'Min'
}
nextTick(() => { carryGuard = false })
</script>

<style lang="scss" scoped>
.time-range-picker {
  display: flex;
  flex-direction: column;
  align-items: center;
  padding: 16px 20px;
  background: var(--surface-card);
  border: 2px solid var(--surface-200);
}

.duration-display {
  display: flex;
  align-items: baseline;
  gap: 2px;
  color: var(--primary-color);
  margin-bottom: 6px;

  &.easter-egg {
    color: var(--danger-color, #E76F51);
    animation: shake 0.5s ease-in-out;
  }
}

.duration-value-wheel {
  font-size: 2.2rem;
  font-weight: 700;
  line-height: 1;
}

.duration-unit-label {
  font-size: 1rem;
  font-weight: 600;
}

@keyframes shake {
  0%, 100% { transform: translateX(0); }
  20% { transform: translateX(-4px); }
  40% { transform: translateX(4px); }
  60% { transform: translateX(-4px); }
  80% { transform: translateX(4px); }
}

.picker-row {
  display: flex;
  align-items: center;
  gap: 12px;
  justify-content: center;
}

.wheels-row {
  display: flex;
  gap: 4px;
  flex-shrink: 0;
}

.arrow-wrap {
  flex-shrink: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  color: var(--primary-color);
  transition: width 0.28s cubic-bezier(0.4, 0, 0.2, 1);
  will-change: width;

  .arrow-v {
    display: none;
  }
}

.arrow-svg {
  transition: width 0.28s cubic-bezier(0.4, 0, 0.2, 1),
              height 0.28s cubic-bezier(0.4, 0, 0.2, 1);
}

@media (max-width: 900px) {
  .picker-row {
    flex-direction: column;
    gap: 8px;
  }

  .duration-value-wheel {
    font-size: 1.6rem;
  }

  .duration-unit-label {
    font-size: 0.85rem;
  }

  .arrow-wrap {
    .arrow-h { display: none; }
    .arrow-v { display: block; }
  }
}

@media (max-width: 520px) {
  .time-range-picker {
    padding: 10px;
  }

  .wheels-row {
    gap: 2px;
  }
}
</style>
