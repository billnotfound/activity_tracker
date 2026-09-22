export function escapeRegex(value) {
  return String(value || '').replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
}

export function exactTagTitlePattern(page, rawWindowTitle, displayWindowTitle = '') {
  if (page !== 'history') return null
  const title = rawWindowTitle || displayWindowTitle || ''
  return `regex:(?i)^${escapeRegex(title)}$`
}

export function isExactTagTarget(rule, processName, titlePattern) {
  if (String(rule?.process || '').toLocaleLowerCase() !== String(processName || '').toLocaleLowerCase())
    return false
  const actual = String(rule?.titlePattern || '').trim()
  return titlePattern == null ? actual === '' : actual === titlePattern
}
