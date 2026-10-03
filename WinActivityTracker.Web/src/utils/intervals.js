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
  // Exclusions are produced by mergeTimePeriods, so both starts and ends are
  // monotonic. Skip every period ending before this interval in O(log n).
  let low = 0
  let high = exclusions.length
  while (low < high) {
    const middle = (low + high) >> 1
    if (exclusions[middle].end <= start) low = middle + 1
    else high = middle
  }
  let cursor = start
  for (let index = low; index < exclusions.length; index++) {
    const exclusion = exclusions[index]
    if (exclusion.start >= end) break
    if (exclusion.start > cursor) result.push([cursor, Math.min(exclusion.start, end)])
    cursor = Math.max(cursor, exclusion.end)
    if (cursor >= end) break
  }
  if (cursor < end) result.push([cursor, end])
  return result
}

export function inferIdleGaps(periods, {
  minimumGapMs,
  rangeStart = Number.NEGATIVE_INFINITY,
  rangeEnd = Number.POSITIVE_INFINITY,
}) {
  const sorted = periods
    .filter(period => Number.isFinite(period.start) && Number.isFinite(period.end) && period.end > period.start)
    .map(period => ({ start: period.start, end: period.end }))
    .sort((left, right) => left.start - right.start || left.end - right.end)
  if (sorted.length < 2) return []

  const gaps = []
  let activeEnd = sorted[0].end
  for (let index = 1; index < sorted.length; index++) {
    const period = sorted[index]
    if (period.start - activeEnd >= minimumGapMs) {
      const start = Math.max(activeEnd, rangeStart)
      const end = Math.min(period.start, rangeEnd)
      if (end > start) gaps.push({ start, end })
    }
    activeEnd = Math.max(activeEnd, period.end)
  }
  return gaps
}
