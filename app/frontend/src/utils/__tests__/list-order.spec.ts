import { describe, expect, it } from 'vitest'

import {
  dropIndex,
  listIndexForGroupMove,
  listIndexForGroupTransfer,
  POSITION_SPACING,
  positionBetween,
  sortByPosition,
} from '../list-order'

import { makeItem } from './test-items'

describe('sortByPosition', () => {
  it('orders by position, breaking ties by ID like the server', () => {
    const items = [
      makeItem({ id: 'c', position: 2 }),
      makeItem({ id: 'b', position: 1 }),
      makeItem({ id: 'a', position: 2 }),
    ]

    expect(sortByPosition(items).map((item) => item.id)).toEqual(['b', 'a', 'c'])
    expect(items.map((item) => item.id)).toEqual(['c', 'b', 'a'])
  })
})

describe('positionBetween', () => {
  it('starts an empty list at zero', () => {
    expect(positionBetween(null, null)).toBe(0)
  })

  it('steps out by the spacing at either end', () => {
    expect(positionBetween(null, 10)).toBe(10 - POSITION_SPACING)
    expect(positionBetween(10, null)).toBe(10 + POSITION_SPACING)
  })

  it('takes the midpoint between neighbors', () => {
    expect(positionBetween(1, 2)).toBe(1.5)
  })
})

describe('dropIndex', () => {
  // Four rows, 50px tall: centers at 25, 75, 125, 175.
  const centers = [25, 75, 125, 175]

  it('stays put until the dragged row passes a neighbor’s center', () => {
    expect(dropIndex(centers, 1, 75)).toBe(1)
    expect(dropIndex(centers, 1, 120)).toBe(1)
  })

  it('moves down past every center it has crossed', () => {
    expect(dropIndex(centers, 0, 130)).toBe(2)
    expect(dropIndex(centers, 0, 999)).toBe(3)
  })

  it('moves up past every center it has crossed', () => {
    expect(dropIndex(centers, 3, 70)).toBe(1)
    expect(dropIndex(centers, 3, -999)).toBe(0)
  })
})

describe('listIndexForGroupMove', () => {
  // The whole list interleaves two stores: C1 W1 C2 W2 C3.
  const list = ['c1', 'w1', 'c2', 'w2', 'c3'].map((id, position) => makeItem({ id, position }))
  const costco = list.filter((item) => item.id.startsWith('c'))

  it('places an item moved down directly after the row it passed', () => {
    // c1 to the bottom of its group: after c3. Without c1 the list is w1 c2 w2 c3, so it goes to index 4.
    expect(listIndexForGroupMove(list, costco, 0, 2)).toBe(4)
  })

  it('places an item moved up directly before the row it passed', () => {
    // c3 to the top of its group: before c1, at index 0.
    expect(listIndexForGroupMove(list, costco, 2, 0)).toBe(0)
    // c3 up one: before c2. Without c3 the list is c1 w1 c2 w2, so it goes to index 2.
    expect(listIndexForGroupMove(list, costco, 2, 1)).toBe(2)
  })

  it('reports that nothing moves', () => {
    expect(listIndexForGroupMove(list, costco, 1, 1)).toBeNull()
    expect(listIndexForGroupMove(list, costco, 0, 5)).toBeNull()
  })
})

describe('listIndexForGroupTransfer', () => {
  // The whole list interleaves two stores: C1 W1 C2 W2 C3.
  const list = ['c1', 'w1', 'c2', 'w2', 'c3'].map((id, position) => makeItem({ id, position }))
  const wholeFoods = list.filter((item) => item.id.startsWith('w'))

  it('places an item dropped above a row of the other group directly before that row', () => {
    // c1 above w1: without c1 the list is w1 c2 w2 c3, so it goes to index 0.
    expect(listIndexForGroupTransfer(list, 'c1', wholeFoods, 0)).toBe(0)
    // c3 above w2: without c3 the list is c1 w1 c2 w2, so it goes to index 3.
    expect(listIndexForGroupTransfer(list, 'c3', wholeFoods, 1)).toBe(3)
  })

  it('places an item dropped below the other group’s last row directly after it', () => {
    // c1 below w2: without c1 the list is w1 c2 w2 c3, so it goes to index 3.
    expect(listIndexForGroupTransfer(list, 'c1', wholeFoods, 2)).toBe(3)
  })

  it('keeps the item’s place in the whole list when dropped into an empty group', () => {
    expect(listIndexForGroupTransfer(list, 'c2', [], 0)).toBe(2)
  })

  it('reports an item that is no longer on the list', () => {
    expect(listIndexForGroupTransfer(list, 'gone', wholeFoods, 0)).toBeNull()
  })
})
