export const DURATION_UNIT_ORDER = ['Min', 'H', 'D']
export const DURATION_UNIT_CAPS = { Min: 180, H: 72, D: 200 }
export const DURATION_UNIT_MINS = { Min: 1, H: 3, D: 3 }

export function durationWheelValue(totalMinutes) {
  const minutes = Math.max(1, Math.round(Number(totalMinutes) || 0))
  if (minutes >= 72 * 60) {
    return { value: Math.floor(minutes / (24 * 60)), unit: 'D' }
  }
  if (minutes >= 180) {
    return { value: Math.floor(minutes / 60), unit: 'H' }
  }
  return { value: minutes, unit: 'Min' }
}

export function previousDurationValue(unit) {
  if (unit === 'D') return { value: DURATION_UNIT_CAPS.H - 1, unit: 'H' }
  if (unit === 'H') return { value: DURATION_UNIT_CAPS.Min - 1, unit: 'Min' }
  return null
}
