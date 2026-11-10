import { describe, expect, it } from 'vitest'

import { acceptsDrops, emptyStoreGroups, groupByStore } from '../store-groups'

import { makeItem } from './test-items'

const STORES = ['Whole Foods', 'Indian Grocery', 'Teeter/Lion', 'Costco']

const summary = (groups: ReturnType<typeof groupByStore>) =>
  groups.map((group) => [group.label, group.store, group.items.map((item) => item.id)])

describe('groupByStore', () => {
  it('puts items without a store first, then the offered stores in order, leaving out empty groups', () => {
    const items = [
      makeItem({ id: '1', store: 'Costco' }),
      makeItem({ id: '2', store: null }),
      makeItem({ id: '3', store: 'Whole Foods' }),
      makeItem({ id: '4', store: 'Costco' }),
    ]

    expect(summary(groupByStore(items, STORES))).toEqual([
      ['No store', null, ['2']],
      ['Whole Foods', 'Whole Foods', ['3']],
      ['Costco', 'Costco', ['1', '4']],
    ])
  })

  it('matches stores regardless of case, under the offered spelling', () => {
    expect(summary(groupByStore([makeItem({ id: '1', store: 'teeter/lion' })], STORES))).toEqual([
      ['Teeter/Lion', 'Teeter/Lion', ['1']],
    ])
  })

  it('still shows stores that are no longer offered, after the offered ones', () => {
    const items = [
      makeItem({ id: '1', store: 'Trader Joe’s' }),
      makeItem({ id: '2', store: 'Aldi' }),
      makeItem({ id: '3', store: 'Costco' }),
    ]

    expect(groupByStore(items, STORES).map((group) => group.label)).toEqual([
      'Costco',
      'Aldi',
      'Trader Joe’s',
    ])
  })

  it('has no groups for an empty list', () => {
    expect(groupByStore([], STORES)).toEqual([])
  })
})

describe('emptyStoreGroups', () => {
  it('offers "No store" and every offered store without items, in group order', () => {
    const groups = groupByStore(
      [makeItem({ id: 'a', store: 'Costco' }), makeItem({ id: 'b', store: 'Whole Foods' })],
      STORES,
    )

    expect(summary(emptyStoreGroups(groups, STORES))).toEqual([
      ['No store', null, []],
      ['Indian Grocery', 'Indian Grocery', []],
      ['Teeter/Lion', 'Teeter/Lion', []],
    ])
  })

  it('offers nothing when every store has items', () => {
    const groups = groupByStore(
      [null, ...STORES].map((store, index) => makeItem({ id: `${index}`, store })),
      STORES,
    )

    expect(emptyStoreGroups(groups, STORES)).toEqual([])
  })
})

describe('acceptsDrops', () => {
  it('takes items into "No store" and the offered stores, not into stores no longer offered', () => {
    const [noStore, costco, aldi] = groupByStore(
      [
        makeItem({ id: 'a', store: null }),
        makeItem({ id: 'b', store: 'costco' }),
        makeItem({ id: 'c', store: 'Aldi' }),
      ],
      STORES,
    )

    expect([noStore, costco, aldi].map((group) => group && acceptsDrops(group, STORES))).toEqual([
      true,
      true,
      false,
    ])
  })
})
