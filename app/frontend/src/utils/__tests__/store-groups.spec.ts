import { describe, expect, it } from 'vitest'

import { groupByStore } from '../store-groups'

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
