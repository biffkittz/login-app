import { describe, expect, it } from 'vitest'

import { describeEditor, formatRelativeTime, formatUsd } from '../grocery-format'

const NOW = Date.parse('2026-11-09T12:00:00.000Z')

describe('formatRelativeTime', () => {
  it.each([
    ['2026-11-09T11:59:30.000Z', 'just now'],
    ['2026-11-09T11:55:00.000Z', '5 minutes ago'],
    ['2026-11-09T09:00:00.000Z', '3 hours ago'],
    ['2026-11-08T12:00:00.000Z', 'yesterday'],
    ['2026-11-05T12:00:00.000Z', '4 days ago'],
    ['2026-10-20T12:00:00.000Z', 'Oct 20'],
    ['2025-10-20T12:00:00.000Z', 'Oct 20, 2025'],
  ])('describes %s as "%s"', (instant, expected) => {
    expect(formatRelativeTime(instant, NOW, 'en-US')).toBe(expected)
  })

  it('treats timestamps from a clock running ahead as just now', () => {
    expect(formatRelativeTime('2026-11-09T12:00:10.000Z', NOW, 'en-US')).toBe('just now')
  })

  it('returns nothing for a malformed timestamp', () => {
    expect(formatRelativeTime('not a date', NOW, 'en-US')).toBe('')
  })
})

describe('describeEditor', () => {
  it('names the reader "you"', () => {
    expect(describeEditor({ lastModifiedById: 'me', lastModifiedByName: 'Ada' }, 'me')).toBe('you')
  })

  it('names someone else by their display name', () => {
    expect(describeEditor({ lastModifiedById: 'other', lastModifiedByName: 'Grace' }, 'me')).toBe(
      'Grace',
    )
  })

  it('copes with a deleted account', () => {
    expect(describeEditor({ lastModifiedById: null, lastModifiedByName: null }, 'me')).toBe(
      'a former member',
    )
  })
})

describe('formatUsd', () => {
  it.each([
    [4.5, '$4.50'],
    [0, '$0.00'],
    [1234, '$1,234.00'],
    [19.999, '$20.00'],
  ])('formats %s as %s', (amount, expected) => {
    expect(formatUsd(amount)).toBe(expected)
  })
})
