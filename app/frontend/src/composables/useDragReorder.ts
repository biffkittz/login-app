import { onBeforeUnmount, readonly, ref, type Ref } from 'vue'

import { dropIndex } from '@/utils/list-order'

/** Options for {@link useDragReorder}. */
export interface DragReorderOptions {
  /** The list element. Its rows are its direct children marked with `data-reorder-item`. */
  list: Readonly<Ref<HTMLElement | null>>
  /** Called once when a row is dropped somewhere new. Indexes refer to the list as it was when the drag began. */
  onDrop: (fromIndex: number, toIndex: number) => void
  /** Called when a drag begins, e.g. to hold back realtime updates that would shift rows under the finger. */
  onDragStart?: () => void
  /** Called when a drag ends, dropped or canceled, after {@link DragReorderOptions.onDrop}. */
  onDragEnd?: () => void
  /** How much of the top of the viewport sticky content covers; auto-scrolling starts below it. */
  topInset?: () => number
}

/** The state and handlers returned by {@link useDragReorder}. */
export interface DragReorder {
  /** The index of the row being dragged, or `null`. */
  draggingIndex: Readonly<Ref<number | null>>
  /** Starts dragging row `index`; bind to the drag handle's `pointerdown`. */
  start: (event: PointerEvent, index: number) => void
  /** Cancels the current drag, leaving the order as it was. */
  cancel: () => void
}

const ROW_SELECTOR = ':scope > [data-reorder-item]'
// Within this distance of the top or bottom edge, the page scrolls by itself, faster the closer the finger is.
const AUTO_SCROLL_ZONE_PX = 72
const AUTO_SCROLL_MAX_SPEED_PX = 14

interface DragState {
  pointerId: number
  handle: HTMLElement
  rows: HTMLElement[]
  centers: number[]
  from: number
  target: number
  /** The pointer's starting point in page coordinates, so scrolling during the drag is accounted for. */
  startPageY: number
  clientY: number
  /** How far the other rows move aside: the dragged row's height. */
  shift: number
  frame: number
}

/**
 * Lets the user reorder a list by dragging a handle, with a finger or a mouse.
 *
 * Built on Pointer Events rather than HTML drag and drop, which mobile browsers don't support for this. Only the
 * dragged row and the rows it passes move, by CSS transforms, so nothing is laid out again until the drop: smooth even
 * on slow phones. The handle needs `touch-action: none` so that dragging it doesn't scroll the page; the page scrolls
 * by itself when the row nears the top or bottom of the screen.
 *
 * Dragging is never the only way to reorder (WCAG 2.5.7): the list also offers buttons and arrow keys.
 */
export function useDragReorder(options: DragReorderOptions): DragReorder {
  const draggingIndex = ref<number | null>(null)
  let drag: DragState | null = null

  const reducedMotion = (): boolean =>
    typeof window.matchMedia === 'function' &&
    window.matchMedia('(prefers-reduced-motion: reduce)').matches

  function start(event: PointerEvent, index: number): void {
    if (
      drag !== null ||
      !event.isPrimary ||
      (event.pointerType === 'mouse' && event.button !== 0)
    ) {
      return
    }

    const rows = Array.from(options.list.value?.querySelectorAll<HTMLElement>(ROW_SELECTOR) ?? [])
    const row = rows[index]
    if (row === undefined || !(event.currentTarget instanceof HTMLElement)) {
      return
    }

    // Stops text selection and, with touch, the page's own scrolling.
    event.preventDefault()
    const handle = event.currentTarget
    handle.setPointerCapture?.(event.pointerId)

    const scrollY = window.scrollY
    const rects = rows.map((element) => element.getBoundingClientRect())
    drag = {
      pointerId: event.pointerId,
      handle,
      rows,
      centers: rects.map((rect) => rect.top + scrollY + rect.height / 2),
      from: index,
      target: index,
      startPageY: event.clientY + scrollY,
      clientY: event.clientY,
      shift: rects[index]?.height ?? 0,
      frame: 0,
    }

    const transition = reducedMotion() ? '' : 'transform 150ms ease'
    rows.forEach((element, i) => {
      if (i !== index) {
        element.style.transition = transition
      }
    })

    handle.addEventListener('pointermove', onPointerMove)
    handle.addEventListener('pointerup', onPointerUp)
    handle.addEventListener('pointercancel', onPointerCancel)
    draggingIndex.value = index
    options.onDragStart?.()
  }

  function render(): void {
    const state = drag
    if (state === null) {
      return
    }

    const offset = state.clientY + window.scrollY - state.startPageY
    const dragged = state.rows[state.from]
    if (dragged !== undefined) {
      dragged.style.transform = `translateY(${offset}px)`
    }

    const target = dropIndex(state.centers, state.from, (state.centers[state.from] ?? 0) + offset)
    if (target === state.target) {
      return
    }
    state.target = target
    state.rows.forEach((element, i) => {
      if (i === state.from) {
        return
      }
      // Rows the dragged one has passed step aside, up or down, to show where it would land.
      const shift =
        state.from < i && i <= target
          ? -state.shift
          : target <= i && i < state.from
            ? state.shift
            : 0
      element.style.transform = shift === 0 ? '' : `translateY(${shift}px)`
    })
  }

  function autoScrollSpeed(clientY: number): number {
    const top = (options.topInset?.() ?? 0) + AUTO_SCROLL_ZONE_PX
    const bottom = window.innerHeight - AUTO_SCROLL_ZONE_PX
    if (clientY < top) {
      return -AUTO_SCROLL_MAX_SPEED_PX * Math.min((top - clientY) / AUTO_SCROLL_ZONE_PX, 1)
    }
    if (clientY > bottom) {
      return AUTO_SCROLL_MAX_SPEED_PX * Math.min((clientY - bottom) / AUTO_SCROLL_ZONE_PX, 1)
    }
    return 0
  }

  function autoScroll(): void {
    if (drag === null) {
      return
    }
    drag.frame = 0
    const speed = autoScrollSpeed(drag.clientY)
    if (speed !== 0) {
      window.scrollBy(0, speed)
      render()
      drag.frame = requestAnimationFrame(autoScroll)
    }
  }

  function onPointerMove(event: PointerEvent): void {
    if (drag === null || event.pointerId !== drag.pointerId) {
      return
    }
    event.preventDefault()
    drag.clientY = event.clientY
    render()
    if (drag.frame === 0) {
      drag.frame = requestAnimationFrame(autoScroll)
    }
  }

  function finish(commit: boolean): void {
    const ended = drag
    if (ended === null) {
      return
    }
    drag = null

    cancelAnimationFrame(ended.frame)
    ended.handle.removeEventListener('pointermove', onPointerMove)
    ended.handle.removeEventListener('pointerup', onPointerUp)
    ended.handle.removeEventListener('pointercancel', onPointerCancel)
    if (ended.handle.hasPointerCapture?.(ended.pointerId)) {
      ended.handle.releasePointerCapture(ended.pointerId)
    }

    // Clearing the transition together with the transform puts every row back without animating; the drop then
    // re-renders the list in its new order before the browser paints, so nothing visibly jumps.
    for (const element of ended.rows) {
      element.style.transition = ''
      element.style.transform = ''
    }
    draggingIndex.value = null

    if (commit && ended.target !== ended.from) {
      options.onDrop(ended.from, ended.target)
    }
    options.onDragEnd?.()
  }

  function onPointerUp(event: PointerEvent): void {
    if (drag !== null && event.pointerId === drag.pointerId) {
      finish(true)
    }
  }

  function onPointerCancel(event: PointerEvent): void {
    if (drag !== null && event.pointerId === drag.pointerId) {
      finish(false)
    }
  }

  onBeforeUnmount(() => finish(false))

  return { draggingIndex: readonly(draggingIndex), start, cancel: () => finish(false) }
}
