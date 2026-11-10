import { nextTick, onBeforeUnmount, readonly, ref, shallowRef, type Ref } from 'vue'

import { dropIndex } from '@/utils/list-order'

/** A place in one of the groups: a row's index, or where a row would be inserted. */
export interface GroupPlace {
  /** The group's key (its `data-reorder-group` value). */
  group: string
  index: number
}

/** A finished drag that put a row somewhere new. Indexes refer to the groups as they were when the drag began. */
export interface ReorderDrop {
  from: GroupPlace
  /**
   * Where it goes. In the row's own group, the index it should end up at once moved; in another group, where among
   * that group's rows it was dropped: 0 above the first, the number of rows below the last.
   */
  to: GroupPlace
}

/** Options for {@link useDragReorder}. */
export interface DragReorderOptions {
  /**
   * Holds the groups: elements marked `data-reorder-group="<key>"`, in display order, each with its rows marked
   * `data-reorder-item`, in order. A group may have no rows: an empty drop zone.
   */
  container: Readonly<Ref<HTMLElement | null>>
  /** Called once when a row is dropped somewhere new. */
  onDrop: (drop: ReorderDrop) => void
  /** Whether a row of group `from` may be dropped into group `to` (another group); all may by default. */
  canDrop?: (from: string, to: string) => boolean
  /**
   * Called when a drag begins, e.g. to hold back realtime updates that would shift rows under the finger. Groups it
   * shows (drop zones for empty groups, say) are found after the next render; they must appear below the others,
   * so that nothing already measured moves.
   */
  onDragStart?: () => void
  /** Called when a drag ends, dropped or canceled, after {@link DragReorderOptions.onDrop}. */
  onDragEnd?: () => void
  /** How much of the top of the viewport sticky content covers; auto-scrolling starts below it. */
  topInset?: () => number
}

/** The state and handlers returned by {@link useDragReorder}. */
export interface DragReorder {
  /** The row being dragged, or `null`. */
  dragging: Readonly<Ref<GroupPlace | null>>
  /**
   * Where the row would land in another group, which shows it with a marker: `null` while it is over its own group
   * (whose rows step aside instead) or over a group that doesn't take it.
   */
  dropTarget: Readonly<Ref<GroupPlace | null>>
  /** Starts dragging row `index` of group `group`; bind to the drag handle's `pointerdown`. */
  start: (event: PointerEvent, group: string, index: number) => void
  /** Cancels the current drag, leaving everything as it was. */
  cancel: () => void
}

const GROUP_SELECTOR = '[data-reorder-group]'
const ROW_SELECTOR = '[data-reorder-item]'
// Within this distance of the top or bottom edge, the page scrolls by itself, faster the closer the finger is.
const AUTO_SCROLL_ZONE_PX = 72
const AUTO_SCROLL_MAX_SPEED_PX = 14

/** A group as measured in page coordinates, so scrolling during the drag doesn't change it. */
interface MeasuredGroup {
  key: string
  top: number
  /** The vertical centers of its rows. */
  centers: number[]
}

interface DragState {
  pointerId: number
  handle: HTMLElement
  group: string
  /** The dragged row's group's rows, and their centers before the drag started. */
  rows: HTMLElement[]
  centers: number[]
  from: number
  /** Where the row would land now, or `null` over a group that doesn't take it. */
  target: GroupPlace | null
  groups: MeasuredGroup[]
  /** The pointer's starting point in page coordinates, so scrolling during the drag is accounted for. */
  startPageY: number
  clientY: number
  /** How far the other rows move aside: the dragged row's height. */
  shift: number
  frame: number
}

function groupKey(element: HTMLElement): string {
  return element.dataset.reorderGroup ?? ''
}

function rowsOf(group: Element): HTMLElement[] {
  return Array.from(group.querySelectorAll<HTMLElement>(ROW_SELECTOR))
}

function centerOf(rect: DOMRect, scrollY: number): number {
  return rect.top + scrollY + rect.height / 2
}

/**
 * Lets the user reorder a list that is split into groups (the stores' sections) by dragging a handle, with a finger
 * or a mouse: within the row's own group, or into another group.
 *
 * Built on Pointer Events rather than HTML drag and drop, which mobile browsers don't support for this. Only the
 * dragged row and the rows it passes move, by CSS transforms, so nothing is laid out again until the drop: smooth even
 * on slow phones. Over another group, nothing there moves; {@link DragReorder.dropTarget} says where to show a marker
 * instead, because rows stepping aside there would spill out of that group's card. The handle needs
 * `touch-action: none` so that dragging it doesn't scroll the page; the page scrolls by itself when the row nears the
 * top or bottom of the screen.
 *
 * Dragging is never the only way to reorder (WCAG 2.5.7): the list also offers buttons, arrow keys, and a store
 * field in each item's details.
 */
export function useDragReorder(options: DragReorderOptions): DragReorder {
  const dragging = ref<GroupPlace | null>(null)
  const dropTarget = shallowRef<GroupPlace | null>(null)
  let drag: DragState | null = null

  const reducedMotion = (): boolean =>
    typeof window.matchMedia === 'function' &&
    window.matchMedia('(prefers-reduced-motion: reduce)').matches

  // Rows of the dragged row's own group may be moving, so only other groups' rows are measured here; the own group's
  // were measured when the drag began.
  function measureGroups(state: DragState): void {
    const scrollY = window.scrollY
    const elements = Array.from(
      options.container.value?.querySelectorAll<HTMLElement>(GROUP_SELECTOR) ?? [],
    )
    state.groups = elements.map((element) => {
      const key = groupKey(element)
      return {
        key,
        top: element.getBoundingClientRect().top + scrollY,
        centers:
          key === state.group
            ? state.centers
            : rowsOf(element).map((row) => centerOf(row.getBoundingClientRect(), scrollY)),
      }
    })
  }

  function start(event: PointerEvent, group: string, index: number): void {
    if (
      drag !== null ||
      !event.isPrimary ||
      (event.pointerType === 'mouse' && event.button !== 0)
    ) {
      return
    }

    const groupElement = Array.from(
      options.container.value?.querySelectorAll<HTMLElement>(GROUP_SELECTOR) ?? [],
    ).find((element) => groupKey(element) === group)
    const rows = groupElement === undefined ? [] : rowsOf(groupElement)
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
    const state: DragState = {
      pointerId: event.pointerId,
      handle,
      group,
      rows,
      centers: rects.map((rect) => centerOf(rect, scrollY)),
      from: index,
      target: { group, index },
      groups: [],
      startPageY: event.clientY + scrollY,
      clientY: event.clientY,
      shift: rects[index]?.height ?? 0,
      frame: 0,
    }
    drag = state
    measureGroups(state)

    const transition = reducedMotion() ? '' : 'transform 150ms ease'
    rows.forEach((element, i) => {
      if (i !== index) {
        element.style.transition = transition
      }
    })

    handle.addEventListener('pointermove', onPointerMove)
    handle.addEventListener('pointerup', onPointerUp)
    handle.addEventListener('pointercancel', onPointerCancel)
    dragging.value = { group, index }
    options.onDragStart?.()
    // Pick up the groups that starting the drag has shown.
    void nextTick(() => {
      if (drag === state) {
        measureGroups(state)
        render()
      }
    })
  }

  // The group under the dragged row's center: the last one that starts above it, so the gap between two groups
  // belongs to the upper one and a group's heading to its own group.
  function groupAt(state: DragState, y: number): MeasuredGroup | undefined {
    let found = state.groups[0]
    for (const group of state.groups) {
      if (group.top <= y) {
        found = group
      }
    }
    return found
  }

  function placeAt(state: DragState, y: number): GroupPlace | null {
    const group = groupAt(state, y)
    if (group === undefined) {
      return null
    }
    if (group.key === state.group) {
      return { group: group.key, index: dropIndex(state.centers, state.from, y) }
    }
    if (options.canDrop !== undefined && !options.canDrop(state.group, group.key)) {
      return null
    }
    // Lands above the first row whose center it hasn't reached.
    return { group: group.key, index: group.centers.filter((center) => center < y).length }
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

    const target = placeAt(state, (state.centers[state.from] ?? 0) + offset)
    if (target?.group === state.target?.group && target?.index === state.target?.index) {
      return
    }
    state.target = target

    // Rows the dragged one has passed in its own group step aside, up or down, to show where it would land. Once it
    // leaves the group, they go back: its slot stays open until the drop.
    const within = target?.group === state.group ? target.index : state.from
    state.rows.forEach((element, i) => {
      if (i === state.from) {
        return
      }
      const shift =
        state.from < i && i <= within
          ? -state.shift
          : within <= i && i < state.from
            ? state.shift
            : 0
      element.style.transform = shift === 0 ? '' : `translateY(${shift}px)`
    })
    dropTarget.value = target !== null && target.group !== state.group ? target : null
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
    dragging.value = null
    dropTarget.value = null

    const target = ended.target
    if (
      commit &&
      target !== null &&
      (target.group !== ended.group || target.index !== ended.from)
    ) {
      options.onDrop({ from: { group: ended.group, index: ended.from }, to: target })
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

  return {
    dragging: readonly(dragging),
    dropTarget: readonly(dropTarget),
    start,
    cancel: () => finish(false),
  }
}
