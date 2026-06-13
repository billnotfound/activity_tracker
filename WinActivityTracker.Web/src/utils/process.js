/**
 * Normalize process name to ensure consistency
 * Backend stores names with .exe suffix, but historical data might not have it
 *
 * @param {string} processName - The process name to normalize
 * @returns {string} - Normalized process name with .exe suffix
 */
export function normalizeProcessName(processName) {
  if (!processName) return processName

  const trimmed = processName.trim()
  if (trimmed.length === 0) return trimmed

  // If already has .exe suffix (case insensitive), return as-is
  if (trimmed.toLowerCase().endsWith('.exe')) {
    return trimmed
  }

  // Add .exe suffix
  return trimmed + '.exe'
}

/**
 * Group and merge data by normalized process name
 * Useful for aggregating data where process names might be inconsistent
 *
 * @param {Array} items - Array of items with processName field
 * @param {Function} sumFields - Function to sum numeric fields (item, acc) => void
 * @returns {Array} - Merged items with normalized process names
 */
export function mergeByProcessName(items, sumFields) {
  const grouped = {}

  items.forEach(item => {
    const normalizedName = normalizeProcessName(item.processName)

    if (!grouped[normalizedName]) {
      grouped[normalizedName] = {
        ...item,
        processName: normalizedName
      }
    } else {
      // Merge numeric fields
      sumFields(item, grouped[normalizedName])
    }
  })

  return Object.values(grouped)
}
