import { toLocalDateString, addLocalDays } from './date.js'

export function normalizeDayStart(value) {
  const n = Number(value)
  return Number.isFinite(n) ? Math.max(0, Math.min(1439, Math.trunc(n))) : 240
}

// Calendar arithmetic keeps the selected local boundary through DST changes.
export function activityDayStart(now = new Date(), minutes = 0) {
  const start = new Date(now)
  const offset = normalizeDayStart(minutes)
  start.setHours(Math.floor(offset / 60), offset % 60, 0, 0)
  if (start > now) start.setDate(start.getDate() - 1)
  return start
}

export function activityDayLabel(now = new Date(), minutes = 0) {
  return toLocalDateString(activityDayStart(now, minutes))
}

export function activityBounds(fromDate, toDate, minutes = 0) {
  const from = new Date(`${fromDate}T00:00:00`)
  const to = addLocalDays(new Date(`${toDate}T00:00:00`), 1)
  const offset = normalizeDayStart(minutes)
  for (const d of [from, to]) d.setHours(Math.floor(offset/60), offset%60, 0, 0)
  return [from, to]
}

export function summaryQuery(from, to) {
  return new URLSearchParams({ from: from.toISOString(), to: to.toISOString() }).toString()
}
