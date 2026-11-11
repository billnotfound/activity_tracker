import { useActivityDay } from './useActivityDay.js'
import { activityDayLabel, activityBounds } from '../utils/activityDay.js'
import { useSelectionFrame } from './useSelectionFrame.js'
import { computed, nextTick, ref, watch } from 'vue'
import {
  addLocalDays,
  addLocalMonthsClamped,
  addLocalYearsClamped,
  toLocalDateString,
} from '../utils/date.js'

function numberRange(from, to) {
  const values = []
  for (let value = from; value <= to; value++) values.push(value)
  return values
}

export function useDashboardPeriod({ onDateChange }) {
  const { effectiveDayStart } = useActivityDay()
  const currentCalendar = () => new Date(`${activityDayLabel(new Date(), effectiveDayStart.value)}T00:00:00`)
  const period = ref('today')
  const isToday = computed(() => period.value === 'today')
  const pickDate = ref(toLocalDateString(currentCalendar()))
  const earliestDate = ref(null)
  const today = currentCalendar()
  const calendarNow = ref(today)
  const wheelYear = ref(today.getFullYear())
  const wheelMonth = ref(today.getMonth() + 1)
  const wheelDay = ref(today.getDate())

  const yearOptions = computed(() => {
    const from = earliestDate.value ? earliestDate.value.getFullYear() : calendarNow.value.getFullYear() - 5
    return numberRange(from, calendarNow.value.getFullYear())
  })

  const monthOptions = computed(() => {
    let min = 1
    let max = 12
    if (earliestDate.value && wheelYear.value === earliestDate.value.getFullYear()) {
      min = earliestDate.value.getMonth() + 1
    }
    if (wheelYear.value === calendarNow.value.getFullYear()) max = calendarNow.value.getMonth() + 1
    return numberRange(min, max)
  })

  const dayOptions = computed(() => {
    const maxDay = new Date(wheelYear.value, wheelMonth.value, 0).getDate()
    let min = 1
    let max = maxDay
    if (earliestDate.value
      && wheelYear.value === earliestDate.value.getFullYear()
      && wheelMonth.value === earliestDate.value.getMonth() + 1) {
      min = earliestDate.value.getDate()
    }
    if (wheelYear.value === calendarNow.value.getFullYear()
      && wheelMonth.value === calendarNow.value.getMonth() + 1) {
      max = Math.min(maxDay, calendarNow.value.getDate())
    }
    return numberRange(min, max)
  })

  function carryDate(unit, delta) {
    const current = new Date(wheelYear.value, wheelMonth.value - 1, wheelDay.value)
    const next = new Date(current)

    if (unit === 'month') {
      const day = next.getDate()
      next.setDate(1)
      next.setMonth(next.getMonth() + delta)
      const maxDay = new Date(next.getFullYear(), next.getMonth() + 1, 0).getDate()
      next.setDate(Math.min(day, maxDay))
    } else if (unit === 'day') {
      next.setDate(next.getDate() + delta)
    } else {
      return
    }

    if (earliestDate.value) {
      const earliest = new Date(earliestDate.value)
      earliest.setHours(0, 0, 0, 0)
      if (+next < +earliest) return
    }
    const todayEnd = currentCalendar()
    todayEnd.setHours(23, 59, 59, 999)
    if (+next > +todayEnd) return

    wheelYear.value = next.getFullYear()
    wheelMonth.value = next.getMonth() + 1
    wheelDay.value = next.getDate()
  }

  function resetDateToToday() {
    const now = currentCalendar()
    calendarNow.value = now
    wheelYear.value = now.getFullYear()
    wheelMonth.value = now.getMonth() + 1
    wheelDay.value = now.getDate()
  }

  // Only a single-day view that followed the previous calendar date follows
  // midnight. Explicit historical dates and longer ranges keep their endpoint.
  function refreshCalendar() {
    const previousToday = toLocalDateString(calendarNow.value)
    const now = currentCalendar()
    const nextToday = toLocalDateString(now)
    const followToday = period.value === 'today' && pickDate.value === previousToday
    calendarNow.value = now
    if (followToday && previousToday !== nextToday) {
      resetDateToToday()
      return true // The date watcher will perform the load.
    }
    return false
  }

  watch(yearOptions, options => {
    if (!options.includes(wheelYear.value)) {
      wheelYear.value = wheelYear.value < options[0] ? options[0] : options[options.length - 1]
    }
  })
  watch(monthOptions, options => {
    if (!options.includes(wheelMonth.value)) {
      wheelMonth.value = wheelMonth.value < options[0] ? options[0] : options[options.length - 1]
    }
  })
  watch([wheelYear, wheelMonth], () => {
    const options = dayOptions.value
    if (!options.includes(wheelDay.value)) wheelDay.value = options[options.length - 1]
  })
  watch([wheelYear, wheelMonth, wheelDay], () => {
    pickDate.value = `${wheelYear.value}-${String(wheelMonth.value).padStart(2, '0')}-${String(wheelDay.value).padStart(2, '0')}`
    onDateChange()
  })

  const periodButtonsRef = ref(null)
  const { style: frameStyle, reset: updateFrame } = useSelectionFrame(periodButtonsRef, '.period-btn', '.period-btn.active')
  const frameReady = ref(true)

  watch(period, updateFrame)

  function periodRange() {
    const [year, month, day] = pickDate.value.split('-').map(Number)
    const endDate = new Date(year, month - 1, day)
    const to = toLocalDateString(endDate)
    if (period.value === 'today') return [pickDate.value, pickDate.value]

    let startDate
    switch (period.value) {
      case 'week':
        // API ranges include both date endpoints, so six days back means
        // exactly seven calendar days including the selected end date.
        startDate = addLocalDays(endDate, -6)
        break
      case 'month':
        startDate = addLocalDays(addLocalMonthsClamped(endDate, -1), 1)
        break
      case 'halfYear':
        startDate = addLocalDays(addLocalMonthsClamped(endDate, -6), 1)
        break
      case 'year':
        startDate = addLocalDays(addLocalYearsClamped(endDate, -1), 1)
        break
      default:
        return [pickDate.value, pickDate.value]
    }
    return [toLocalDateString(startDate), to]
  }

  function periodBounds() {
    const [from, to] = periodRange()
    return activityBounds(from, to, effectiveDayStart.value)
  }
  watch(effectiveDayStart, () => { refreshCalendar(); onDateChange() })
  return {
    periodBounds,
    period,
    isToday,
    pickDate,
    earliestDate,
    wheelYear,
    wheelMonth,
    wheelDay,
    yearOptions,
    monthOptions,
    dayOptions,
    carryDate,
    resetDateToToday,
    refreshCalendar,
    periodButtonsRef,
    frameStyle,
    frameReady,
    updateFrame,
    periodRange,
  }
}
