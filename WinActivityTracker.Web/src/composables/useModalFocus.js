import { watch, nextTick, onBeforeUnmount } from 'vue'

export function useModalFocus(root, visible, close) {
  let previous
  const focusable = () => Array.from(root.value?.querySelectorAll('button:not(:disabled), input:not(:disabled), select:not(:disabled), a[href], [tabindex="0"]') || []).filter(e => e.getClientRects().length)
  function keyboard(e) {
    if (!visible()) return
    if (e.key === 'Escape') { e.preventDefault(); close(); return }
    if (e.key !== 'Tab') return
    const list = focusable()
    if (!list.length) { e.preventDefault(); root.value?.focus(); return }
    const first = list[0], last = list[list.length-1]
    if (e.shiftKey && (document.activeElement === first || !root.value?.contains(document.activeElement))) { e.preventDefault(); last.focus() }
    else if (!e.shiftKey && (document.activeElement === last || !root.value?.contains(document.activeElement))) { e.preventDefault(); first.focus() }
  }
  watch(visible, async open => {
    if (open) {
      previous = document.activeElement
      await nextTick()
      if (!visible()) return
      ;(focusable()[0] || root.value)?.focus()
      document.addEventListener('keydown', keyboard)
    } else {
      document.removeEventListener('keydown', keyboard)
      if (previous?.isConnected) previous.focus()
    }
  }, { immediate: true })
  onBeforeUnmount(() => document.removeEventListener('keydown', keyboard))
}
