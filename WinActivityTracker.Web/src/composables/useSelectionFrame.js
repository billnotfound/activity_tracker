import { ref, onMounted, onBeforeUnmount, nextTick, getCurrentInstance } from 'vue'

// Routes, tabs and period buttons keep their semantics but share geometry.
export function useSelectionFrame(host, itemSelector, activeSelector) {
  const style = ref({ opacity: 0 })
  let target = null
  let observer, mutations, frame
  function measure() {
    const root = host.value
    const item = target?.isConnected ? target : root?.querySelector(activeSelector)
    if (!root || !item) return
    const a = root.getBoundingClientRect(), b = item.getBoundingClientRect()
    style.value = { opacity: 1, left: `${b.left-a.left+root.scrollLeft}px`, top: `${b.top-a.top+root.scrollTop}px`, width: `${b.width}px`, height: `${b.height}px` }
  }
  function schedule() {
    if (typeof requestAnimationFrame === 'undefined') return
    cancelAnimationFrame(frame); frame = requestAnimationFrame(measure)
  }
  function select(element) { target = element || null; nextTick(schedule) }
  function reset() { select(null) }
  function hover(event) {
    if (event.pointerType === 'touch') return
    const item = event.target.closest(itemSelector)
    select(item && host.value?.contains(item) ? item : null)
  }
  if (getCurrentInstance()) onMounted(() => {
    observer = new ResizeObserver(schedule)
    const observe = () => {
      observer.disconnect()
      if (!host.value) return
      observer.observe(host.value)
      host.value.querySelectorAll(itemSelector).forEach(e => observer.observe(e))
      schedule()
    }
    mutations = new MutationObserver(observe)
    if (host.value) mutations.observe(host.value, { subtree: true, childList: true, characterData: true })
    observe()
    window.addEventListener('resize', schedule)
  })
  if (getCurrentInstance()) onBeforeUnmount(() => { observer?.disconnect(); mutations?.disconnect(); cancelAnimationFrame(frame); window.removeEventListener('resize', schedule) })
  return { style, select, reset, hover }
}
