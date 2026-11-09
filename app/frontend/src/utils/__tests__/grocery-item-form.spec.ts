import { describe, expect, it } from 'vitest'

import { changedDetails, toItemDetails } from '../grocery-item-form'

import { makeItem } from './test-items'

describe('toItemDetails', () => {
  it('turns unset fields into empty strings for the form', () => {
    expect(toItemDetails(makeItem({ id: '1', name: 'Milk', quantity: '2' }))).toEqual({
      name: 'Milk',
      quantity: '2',
      brand: '',
      store: '',
      notes: '',
    })
  })
})

describe('changedDetails', () => {
  const original = { name: 'Milk', quantity: '2', brand: 'Organic Valley', store: '', notes: '' }

  it('returns only the fields that changed, trimmed', () => {
    expect(changedDetails(original, { ...original, quantity: ' 3 ', store: 'Costco' })).toEqual({
      quantity: '3',
      store: 'Costco',
    })
  })

  it('sends an empty string to clear a field', () => {
    expect(changedDetails(original, { ...original, brand: '  ' })).toEqual({ brand: '' })
  })

  it('ignores edits that only add surrounding spaces', () => {
    expect(changedDetails(original, { ...original, name: ' Milk ' })).toEqual({})
  })
})
