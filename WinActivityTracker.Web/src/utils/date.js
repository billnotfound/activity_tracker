// DB timestamps are UTC but may lack a Z suffix because EF Core strips DateTimeKind.
export function parseUtcTs(timestamp) {
  if (!timestamp) return null
  return new Date(timestamp.endsWith('Z') ? timestamp : `${timestamp}Z`)
}

// Local date string "YYYY-MM-DD" for date inputs and date-only API parameters.
export function toLocalDateString(date) {
  date = date || new Date()
  const pad = value => String(value).padStart(2, '0')
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`
}

// Local datetime string "YYYY-MM-DDTHH:mm" for datetime-local inputs.
export function toLocalDatetimeString(date) {
  date = date || new Date()
  const pad = value => String(value).padStart(2, '0')
  return `${toLocalDateString(date)}T${pad(date.getHours())}:${pad(date.getMinutes())}`
}

export function addLocalDays(date, days) {
  const result = new Date(date)
  result.setDate(result.getDate() + days)
  return result
}

export function addLocalMonthsClamped(date, months) {
  const result = new Date(date)
  const day = result.getDate()
  result.setDate(1)
  result.setMonth(result.getMonth() + months)
  const maxDay = new Date(result.getFullYear(), result.getMonth() + 1, 0).getDate()
  result.setDate(Math.min(day, maxDay))
  return result
}

export function addLocalYearsClamped(date, years) {
  const result = new Date(date)
  const month = result.getMonth()
  const day = result.getDate()
  result.setDate(1)
  result.setFullYear(result.getFullYear() + years)
  result.setMonth(month)
  const maxDay = new Date(result.getFullYear(), month + 1, 0).getDate()
  result.setDate(Math.min(day, maxDay))
  return result
}
