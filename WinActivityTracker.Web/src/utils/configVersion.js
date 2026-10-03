export const CONFIG_LAST_WRITE_HEADER = 'X-WTA-Last-Write'

export function configWriteHeaders(lastWrite) {
  const headers = { 'Content-Type': 'application/json' }
  if (lastWrite) headers[CONFIG_LAST_WRITE_HEADER] = lastWrite
  return headers
}

export async function ensureConfigWriteSucceeded(response) {
  if (response.ok) return response
  const payload = await response.json().catch(() => ({}))
  const error = new Error(payload.error || `API ${response.status}`)
  error.status = response.status
  error.lastWrite = payload.lastWrite || null
  throw error
}
