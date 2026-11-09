import type { GroceryItem } from '@/types/grocery'

/**
 * The gap between neighbors at either end of the list. Mirrors `ItemPositions.Spacing` in the API, so a position the
 * app guesses matches the one the server will choose.
 */
export const POSITION_SPACING = 1024

/**
 * Sorts items into list order: by position, then by ID, which is how the server breaks ties.
 *
 * @param items - The items, in any order. Not modified.
 * @returns A new, sorted array.
 */
export function sortByPosition(items: Iterable<GroceryItem>): GroceryItem[] {
  return [...items].sort(
    (a, b) => a.position - b.position || (a.id < b.id ? -1 : a.id > b.id ? 1 : 0),
  )
}

/**
 * A position between two neighbors, as the server computes it (fractional indexing): the midpoint, or one
 * {@link POSITION_SPACING} beyond an end of the list.
 *
 * This is only the app's guess for showing a change straight away. When the gap is too small to split, the server
 * renumbers the list, and its answer replaces the guess.
 *
 * @param before - The position of the item that will come first, or `null` at the top.
 * @param after - The position of the item that will come next, or `null` at the bottom.
 */
export function positionBetween(before: number | null, after: number | null): number {
  if (before === null && after === null) {
    return 0
  }
  if (before === null) {
    return (after as number) - POSITION_SPACING
  }
  if (after === null) {
    return before + POSITION_SPACING
  }
  return before + (after - before) / 2
}

/**
 * Where a dragged row would land: its index in the reordered list.
 *
 * The row passes a neighbor once its center crosses the neighbor's center, which is how native lists on phones
 * behave.
 *
 * @param centers - The vertical center of every row before the drag started, in list order.
 * @param from - The index of the dragged row.
 * @param draggedCenter - The dragged row's center now.
 */
export function dropIndex(centers: readonly number[], from: number, draggedCenter: number): number {
  let index = from
  for (let i = from - 1; i >= 0 && draggedCenter < (centers[i] ?? -Infinity); i--) {
    index = i
  }
  for (let i = from + 1; i < centers.length && draggedCenter > (centers[i] ?? Infinity); i++) {
    index = i
  }
  return index
}

/**
 * Turns a move within one group of the list (a store's section) into the index the store's `moveItem` expects: the
 * item's index in the whole list once moved. The item lands directly after the row it was dropped below, or directly
 * before the row it was dropped above, wherever those rows are in the whole list.
 *
 * @param list - The whole list, in order.
 * @param group - The group's items, in order (a subsequence of `list`).
 * @param from - The item's index in the group.
 * @param to - The index in the group it should end up at.
 * @returns The index in the whole list, or `null` when nothing moves.
 */
export function listIndexForGroupMove(
  list: readonly GroceryItem[],
  group: readonly GroceryItem[],
  from: number,
  to: number,
): number | null {
  const item = group[from]
  const anchor = group[to]
  if (item === undefined || anchor === undefined || from === to) {
    return null
  }
  const anchorIndex = list
    .filter((other) => other.id !== item.id)
    .findIndex((other) => other.id === anchor.id)
  return to > from ? anchorIndex + 1 : anchorIndex
}
