import { messages } from '../i18n/index.js'

function m(key, args = {}) {
  let msg = messages[key]
  if (msg === undefined) return key
  for (const [k, v] of Object.entries(args))
    msg = msg.replace(new RegExp(`\\{${k}\\}`, 'g'), String(v))
  return msg
}

// DB timestamps are UTC but may lack 'Z' suffix (EF Core strips DateTimeKind).
// Append 'Z' so JS parses as UTC, then format in local time.
export function parseUtcTs(ts) {
  if (!ts) return null
  return new Date(ts.endsWith('Z') ? ts : ts + 'Z')
}

// RFC 3339 timestamp — always appends 'Z' for API interoperability.
// Unlike toISOString(), this guarantees correct UTC parsing even when
// the backend returns dates without timezone info.
export function toUtcIso(ts) {
  if (!ts) return '?'
  const d = parseUtcTs(ts)
  return d ? d.toLocaleString() : '?'
}

// Local date string "YYYY-MM-DD" for date inputs and date-only API params.
// Avoids toISOString() which shifts dates across midnight for non-UTC timezones.
export function toLocalDateString(d) {
  d = d || new Date()
  const pad = (n) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`
}

// Local datetime string "YYYY-MM-DDTHH:mm" for datetime-local inputs.
export function toLocalDatetimeString(d) {
  d = d || new Date()
  const pad = (n) => String(n).padStart(2, '0')
  return `${toLocalDateString(d)}T${pad(d.getHours())}:${pad(d.getMinutes())}`
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
  if (s < 60) return m('time.seconds', { n: s.toFixed(0) })
  if (s < 3600) return m('time.minutes', { n: (s / 60).toFixed(1) })
  return m('time.hours', { n: (s / 3600).toFixed(1) })
}

export function fmtShortDur(s) {
  const strip = v => v.replace(/\.0+$/, '')
  if (s < 60) return strip(s.toFixed(0)) + 's'
  if (s < 3600) return strip((s / 60).toFixed(1)) + 'm'
  if (s < 86400) return strip((s / 3600).toFixed(1)) + 'h'
  return strip((s / 86400).toFixed(1)) + 'd'
}
