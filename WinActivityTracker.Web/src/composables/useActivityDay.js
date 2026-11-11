import { computed, readonly, ref } from 'vue'
import { normalizeDayStart } from '../utils/activityDay.js'

const nightOwl = ref(false)
const dayStartMinutes = ref(240)
const loaded = ref(false)
const saving = ref(false)
const error = ref('')
let revision = 0
const effectiveDayStart = computed(() => nightOwl.value ? dayStartMinutes.value : 0)
const dayStartTime = computed(() => {
  const hours = Math.floor(dayStartMinutes.value / 60)
  const minutes = dayStartMinutes.value % 60
  return `${String(hours).padStart(2, '0')}:${String(minutes).padStart(2, '0')}`
})

function applyActivityDaySettings(settings) {
  nightOwl.value = settings.nightOwlMode === true
  dayStartMinutes.value = normalizeDayStart(settings.dayStartMinutes ?? 240)
  loaded.value = true
  error.value = ''
}

export function initializeActivityDay(settings) {
  if (!loaded.value) applyActivityDaySettings(settings)
}

export async function loadActivityDay() {
  if (saving.value) return
  const current = ++revision
  try {
    const response = await fetch('/api/settings', { signal: AbortSignal.timeout(3000) })
    if (!response.ok) throw new Error(`HTTP ${response.status}`)
    const settings = await response.json()
    if (current === revision) applyActivityDaySettings(settings)
  } catch (e) {
    if (current === revision) error.value = e.message
  }
}

async function saveActivityDay(values) {
  if (!loaded.value || saving.value) return
  revision++
  saving.value = true
  error.value = ''
  try {
    // Send only these fields so unsaved tracking/database forms stay untouched.
    const response = await fetch('/api/settings', {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(values),
    })
    if (!response.ok) throw new Error(`HTTP ${response.status}`)
    const settings = await response.json()
    applyActivityDaySettings(settings)
  } catch (e) {
    error.value = e.message
  } finally {
    saving.value = false
  }
}

export function useActivityDay() {
  return {
    nightOwl: readonly(nightOwl),
    effectiveDayStart, dayStartTime,
    activityDayLoaded: readonly(loaded), activityDaySaving: readonly(saving),
    activityDayError: readonly(error),
    saveActivityDay, loadActivityDay,
  }
}
