// DB timestamps are UTC but may lack 'Z' suffix (EF Core strips DateTimeKind).
// Append 'Z' so JS parses as UTC, then format in local time.
export function parseUtcTs(ts) {
  if (!ts) return null
  return new Date(ts.endsWith('Z') ? ts : ts + 'Z')
}

export function toLocalTime(ts) {
  const d = parseUtcTs(ts)
  return d ? d.toLocaleTimeString() : '-'
}

export function toLocalString(ts) {
  const d = parseUtcTs(ts)
  return d ? d.toLocaleString() : '-'
}

// Human-readable duration formatting (Chinese units)
export function fmtDuration(s) {
  if (s < 60) return `${s.toFixed(0)}秒`
  if (s < 3600) return `${(s / 60).toFixed(1)}分钟`
  return `${(s / 3600).toFixed(1)}小时`
}

export function fmtShortDur(s) {
  if (s < 60) return s + 's'
  if (s < 3600) return Math.round(s / 60) + 'm'
  return (s / 3600).toFixed(1) + 'h'
}
