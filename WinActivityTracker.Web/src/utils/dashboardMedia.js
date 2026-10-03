import { parseUtcTs } from './date.js'

export function buildDisplayMedia(media, { now = Date.now(), formatDuration, formatAnomaly }) {
  const normalized = media.map(item => {
    const start = parseUtcTs(item.startTime)?.getTime() || now
    const end = item.endTime ? (parseUtcTs(item.endTime)?.getTime() || now) : now
    const rawSeconds = Math.round((end - start) / 1000)
    const recordCount = Math.max(1, Number(item.recordCount) || 1)
    const reportedAnomalyCount = Math.max(0, Number(item.anomalousRecordCount) || 0)
    const isAnomalous = item.isAnomalous === true || rawSeconds < 0 || reportedAnomalyCount > 0
    const durationSeconds = Math.max(1, rawSeconds)
    return {
      ...item,
      recordCount,
      anomalousRecordCount: isAnomalous ? Math.max(1, reportedAnomalyCount) : 0,
      isAnomalous,
      durationSec: durationSeconds,
      durationFmt: isAnomalous ? formatAnomaly(recordCount) : formatDuration(durationSeconds),
    }
  }).filter(item => item.playbackStatus !== 'SystemSleep')

  if (!normalized.length) return []
  const merged = []
  let current = { ...normalized[0] }
  for (let index = 1; index < normalized.length; index++) {
    const item = normalized[index]
    if (current.appName === item.appName
      && current.title === item.title
      && current.artist === item.artist
      && current.playbackStatus === item.playbackStatus) {
      current.durationSec += item.durationSec
      current.recordCount += item.recordCount
      current.anomalousRecordCount += item.anomalousRecordCount
      current.isAnomalous = current.isAnomalous || item.isAnomalous
      current.durationFmt = current.isAnomalous
        ? formatAnomaly(current.recordCount)
        : formatDuration(current.durationSec)
      if (item.endTime) current.endTime = item.endTime
    } else {
      merged.push(current)
      current = { ...item }
    }
  }
  merged.push(current)
  return merged
}

export function buildMediaRing(media, fromDate, toDate, successColor, isToday) {
  const periodStart = new Date(`${fromDate}T00:00:00`).getTime()
  const periodEnd = isToday
    ? Math.min(Date.now(), new Date(`${toDate}T23:59:59`).getTime())
    : new Date(`${toDate}T23:59:59`).getTime()

  const filtered = media
    .filter(item => item.playbackStatus === 'Playing' && !item.isAnomalous)
    .map(item => {
      const start = parseUtcTs(item.startTime)?.getTime()
      const end = item.endTime ? parseUtcTs(item.endTime)?.getTime() : Date.now()
      if (!Number.isFinite(start) || !Number.isFinite(end)) return null
      return { ...item, _start: Math.max(start, periodStart), _end: Math.min(end, periodEnd) }
    })
    .filter(Boolean)
    .filter(item => item._end > periodStart && item._start < periodEnd && item._end - item._start >= 1000)
    .sort((left, right) => left._start - right._start)

  if (!filtered.length) return []
  const segments = []
  let cursor = periodStart
  for (const item of filtered) {
    const segmentStart = Math.max(item._start, cursor)
    if (item._end <= segmentStart) continue
    if (segmentStart > cursor) {
      segments.push({
        value: (segmentStart - cursor) / 1000,
        name: '',
        itemStyle: { color: 'transparent', borderWidth: 0 },
        tooltip: { show: false },
        _type: 'gap',
      })
    }
    segments.push({
      value: Math.max(1, (item._end - segmentStart) / 1000),
      name: item.title,
      itemStyle: { color: successColor, borderWidth: 0 },
      _type: 'media',
      _media: { title: item.title, artist: item.artist, appName: item.appName },
    })
    cursor = Math.max(cursor, item._end)
  }
  if (periodEnd > cursor) {
    segments.push({
      value: (periodEnd - cursor) / 1000,
      name: '',
      itemStyle: { color: 'transparent', borderWidth: 0 },
      tooltip: { show: false },
      _type: 'gap',
    })
  }
  return segments
}
