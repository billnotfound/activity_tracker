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
  const period = ref('today')
  const isToday = computed(() => period.value === 'today')
  const pickDate = ref(toLocalDateString())
  const earliestDate = ref(null)
  const today = new Date()
  const wheelYear = ref(today.getFullYear())
  const wheelMonth = ref(today.getMonth() + 1)
  const wheelDay = ref(today.getDate())

  const yearOptions = computed(() => {
    const from = earliestDate.value ? earliestDate.value.getFullYear() : new Date().getFullYear() - 5
    return numberRange(from, new Date().getFullYear())
  })

  const monthOptions = computed(() => {
    let min = 1
    let max = 12
    if (earliestDate.value && wheelYear.value === earliestDate.value.getFullYear()) {
      min = earliestDate.value.getMonth() + 1
    }
    if (wheelYear.value === new Date().getFullYear()) max = new Date().getMonth() + 1
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
    if (wheelYear.value === new Date().getFullYear()
      && wheelMonth.value === new Date().getMonth() + 1) {
      max = Math.min(maxDay, new Date().getDate())
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
    const todayEnd = new Date()
    todayEnd.setHours(23, 59, 59, 999)
    if (+next > +todayEnd) return

    wheelYear.value = next.getFullYear()
    wheelMonth.value = next.getMonth() + 1
    wheelDay.value = next.getDate()
  }

  function resetDateToToday() {
    const now = new Date()
    wheelYear.value = now.getFullYear()
    wheelMonth.value = now.getMonth() + 1
    wheelDay.value = now.getDate()
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
  const frameStyle = ref({})
  const frameReady = ref(false)

  function updateFrame() {
    nextTick(() => {
      const element = periodButtonsRef.value
      if (!element) return
      const active = element.querySelector('.period-btn.active')
      if (!active) return
      frameStyle.value = {
        width: `${active.offsetWidth}px`,
        left: `${active.offsetLeft}px`,
      }
      frameReady.value = true
    })
  }

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

  return {
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
    periodButtonsRef,
    frameStyle,
    frameReady,
    updateFrame,
    periodRange,
  }
}
