export function mergeTimePeriods(periods) {
  const sorted = periods
    .filter(period => period.end > period.start)
    .map(period => ({ ...period }))
    .sort((a, b) => a.start - b.start || a.end - b.end)
  const merged = []
  for (const period of sorted) {
    const last = merged[merged.length - 1]
    if (last && period.start <= last.end) last.end = Math.max(last.end, period.end)
    else merged.push(period)
  }
  return merged
}

export function subtractTimePeriods(start, end, exclusions) {
  const result = []
  if (end <= start) return result
  let cursor = start
  for (const exclusion of exclusions) {
    if (exclusion.end <= cursor) continue
    if (exclusion.start >= end) break
    if (exclusion.start > cursor) result.push([cursor, Math.min(exclusion.start, end)])
    cursor = Math.max(cursor, exclusion.end)
    if (cursor >= end) break
  }
  if (cursor < end) result.push([cursor, end])
  return result
}
