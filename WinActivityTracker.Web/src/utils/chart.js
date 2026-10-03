export function mergePixelIntervals(intervals) {
  if (!intervals.length) return []
  intervals.sort((left, right) => left[0] - right[0])
  const merged = []
  let current = intervals[0]
  for (let index = 1; index < intervals.length; index++) {
    const interval = intervals[index]
    if (interval[0] <= current[1]) {
      if (interval[1] > current[1]) current[1] = interval[1]
    } else {
      merged.push(current)
      current = interval
    }
  }
  merged.push(current)
  return merged
}

export function withAlpha(color, alpha) {
  if (!color) return `rgba(128, 128, 128, ${alpha})`
  const hexMatch = color.trim().match(/^#([0-9a-f]{3}|[0-9a-f]{6})$/i)
  if (hexMatch) {
    const hex = hexMatch[1].length === 3
      ? hexMatch[1].split('').map(character => character + character).join('')
      : hexMatch[1]
    const value = parseInt(hex, 16)
    return `rgba(${(value >> 16) & 255}, ${(value >> 8) & 255}, ${value & 255}, ${alpha})`
  }
  const rgbMatch = color.match(/(\d+)\s*,\s*(\d+)\s*,\s*(\d+)/)
  if (rgbMatch) return `rgba(${rgbMatch[1]}, ${rgbMatch[2]}, ${rgbMatch[3]}, ${alpha})`
  return `rgba(128, 128, 128, ${alpha})`
}
