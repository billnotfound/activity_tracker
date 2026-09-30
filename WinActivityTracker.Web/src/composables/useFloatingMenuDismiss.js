import { onMounted, onUnmounted, watch } from 'vue'

// Shared dismissal behavior for pointer-anchored menus. Keeping this in one
// place makes Esc, outside-click, and distance-based auto-close consistent.
export function useFloatingMenuDismiss({ rootRef, isVisible, onEscape, onClose, distanceFactor = 1 }) {
  let openedAt = 0
  const stopVisibleWatch = watch(isVisible, value => {
    if (value) openedAt = performance.now()
  }, { immediate: true })
  function handleKeydown(event) {
    if (event.key === 'Escape' && isVisible()) onEscape?.()
  }

  function handlePointerDown(event) {
    if (!isVisible() || rootRef.value?.contains(event.target)) return
    onClose()
  }

  function handlePointerMove(event) {
    if (!isVisible() || !rootRef.value) return
    // Ignore the click/mousemove tail that created the menu. ZRender may emit
    // another pointer event at the old tooltip point before Vue finishes
    // positioning the teleported panel.
    if (performance.now() - openedAt < 260) return
    const rect = rootRef.value.getBoundingClientRect()
    const dx = event.clientX < rect.left ? rect.left - event.clientX
      : event.clientX > rect.right ? event.clientX - rect.right : 0
    const dy = event.clientY < rect.top ? rect.top - event.clientY
      : event.clientY > rect.bottom ? event.clientY - rect.bottom : 0
    if (Math.hypot(dx, dy) > Math.max(rect.width, rect.height) * distanceFactor) onClose()
  }

  onMounted(() => {
    window.addEventListener('keydown', handleKeydown)
    document.addEventListener('pointerdown', handlePointerDown, true)
    window.addEventListener('pointermove', handlePointerMove, { passive: true })
  })
  onUnmounted(() => {
    stopVisibleWatch()
    window.removeEventListener('keydown', handleKeydown)
    document.removeEventListener('pointerdown', handlePointerDown, true)
    window.removeEventListener('pointermove', handlePointerMove)
  })
}
