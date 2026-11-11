import { nextTick, onBeforeUnmount, watch } from 'vue'

const EDGE = 10
const GAP = 16
const MENU_GAP = 9
const clamp = (value, min, max) => Math.min(Math.max(value, min), Math.max(min, max))

function menuBounds() {
  const menu = document.querySelector('.context-layer:not(.context-menu-leave-active) .context-anchor, .media-menu-layer:not(.media-menu-leave-active) .media-menu-anchor')
  return menu?.getBoundingClientRect()
}

function nearestFreePosition(cursor, width, height, obstacle) {
  const viewport = { left: EDGE, top: EDGE, right: window.innerWidth - EDGE, bottom: window.innerHeight - EDGE }
  const { x, y } = cursor
  const preferred = [
    [x + GAP, y - height / 2], [x - width - GAP, y - height / 2],
    [x - width / 2, y + GAP], [x - width / 2, y - height - GAP],
  ]
  const regions = obstacle ? [
    { ...viewport, right: Math.min(viewport.right, obstacle.left - MENU_GAP) },
    { ...viewport, left: Math.max(viewport.left, obstacle.right + MENU_GAP) },
    { ...viewport, bottom: Math.min(viewport.bottom, obstacle.top - MENU_GAP) },
    { ...viewport, top: Math.max(viewport.top, obstacle.bottom + MENU_GAP) },
  ] : [viewport]
  let best
  let bestScore = Infinity
  for (const region of regions) {
    if (region.right - region.left < width || region.bottom - region.top < height) continue
    for (const [index, [px, py]] of preferred.entries()) {
      const left = clamp(px, region.left, region.right - width)
      const top = clamp(py, region.top, region.bottom - height)
      const dx = Math.max(left - x, x - (left + width), 0)
      const dy = Math.max(top - y, y - (top + height), 0)
      const gap = Math.hypot(dx, dy)
      const score = Math.abs(gap - GAP) + (gap < 6 ? 100 : 0) + Math.hypot(left - px, top - py) * .03 + index * .05
      if (score < bestScore) { bestScore = score; best = [left, top] }
    }
  }
  // On a very small viewport a full-width menu can leave no region large
  // enough. Keep the tooltip readable in the viewport on its own layer.
  return best || [clamp(x + GAP, viewport.left, viewport.right - width), clamp(y - height / 2, viewport.top, viewport.bottom - height)]
}

export function chartTooltip(host) {
  return {
    className: 'wta-chart-tooltip',
    appendTo: () => document.body,
    transitionDuration: 0.4,
    position(point, _params, dom, _rect, size) {
      const chart = host.value?.getBoundingClientRect()
      if (!chart) return point
      const cursor = { x: chart.left + point[0], y: chart.top + point[1] }
      dom.dataset.cursorX = cursor.x
      dom.dataset.cursorY = cursor.y
      const [left, top] = nearestFreePosition(cursor, ...size.contentSize, menuBounds())
      return [left - chart.left, top - chart.top]
    },
  }
}

export function useChartTooltipAvoidance(isMenuOpen) {
  let frame
  let resize
  let mutations
  let disposed = false
  function schedule() {
    if (disposed) return
    cancelAnimationFrame(frame)
    frame = requestAnimationFrame(reposition)
  }
  function reposition() {
    const menu = menuBounds()
    if (!menu) return
    for (const tip of document.querySelectorAll('.wta-chart-tooltip')) {
      if (tip.style.visibility === 'hidden' || !tip.offsetWidth) continue
      const cursor = { x: Number(tip.dataset.cursorX), y: Number(tip.dataset.cursorY) }
      if (!Number.isFinite(cursor.x) || !Number.isFinite(cursor.y)) continue
      const [left, top] = nearestFreePosition(cursor, tip.offsetWidth, tip.offsetHeight, menu)
      const transform = `translate3d(${left + window.scrollX}px, ${top + window.scrollY}px, 0px)`
      if (tip.style.left !== '0px') tip.style.left = '0px'
      if (tip.style.top !== '0px') tip.style.top = '0px'
      if (tip.style.transform !== transform) tip.style.transform = transform
    }
  }
  function stop() {
    cancelAnimationFrame(frame)
    resize?.disconnect()
    mutations?.disconnect()
    window.removeEventListener('scroll', reposition, true)
    window.removeEventListener('resize', reposition)
  }
  watch(isMenuOpen, async open => {
    stop()
    if (!open) return
    await nextTick()
    if (disposed || !isMenuOpen()) return
    schedule()
    resize = new ResizeObserver(schedule)
    document.querySelectorAll('.context-anchor, .media-menu-anchor').forEach(menu => resize.observe(menu))
    mutations = new MutationObserver(schedule)
    document.querySelectorAll('.wta-chart-tooltip').forEach(tip => {
      resize.observe(tip)
      mutations.observe(tip, { attributes: true, attributeFilter: ['style'] })
    })
    window.addEventListener('scroll', reposition, true)
    window.addEventListener('resize', reposition)
  })
  onBeforeUnmount(() => { disposed = true; stop() })
}
