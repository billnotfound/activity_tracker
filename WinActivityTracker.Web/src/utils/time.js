import { t } from '../i18n/index.js'
import { parseUtcTs, toLocalDateString } from './date.js'

export { parseUtcTs, toLocalDateString, toLocalDatetimeString } from './date.js'

// Format a DB timestamp as a localized display string ('?' when missing).
export function toUtcIso(ts) {
  if (!ts) return '?'
  const d = parseUtcTs(ts)
  return d ? d.toLocaleString() : '?'
}

export function toLocalTime(ts) {
  const d = parseUtcTs(ts)
  return d ? d.toLocaleTimeString() : '-'
}

export function toLocalString(ts) {
  const d = parseUtcTs(ts)
  return d ? d.toLocaleString() : '-'
}

// Human-readable duration formatting (localized units)
export function fmtDuration(s) {
  if (s < 60) return t('time.seconds', { n: s.toFixed(0) })
  if (s < 3600) return t('time.minutes', { n: (s / 60).toFixed(1) })
  return t('time.hours', { n: (s / 3600).toFixed(1) })
}

export function fmtShortDur(s) {
  const strip = v => v.replace(/\.0+$/, '')
  if (s < 60) return strip(s.toFixed(0)) + 's'
  if (s < 3600) return strip((s / 60).toFixed(1)) + 'm'
  if (s < 86400) return strip((s / 3600).toFixed(1)) + 'h'
  return strip((s / 86400).toFixed(1)) + 'd'
}
