import { mount } from '@vue/test-utils'
import { beforeAll, beforeEach, describe, expect, it } from 'vitest'

import type { CostEstimate } from '@/types/grocery'

import CostEstimateSheet from '../cost-estimate-sheet.vue'
import { polyfillDialog } from './dialog-polyfill'

const ESTIMATE: CostEstimate = {
  totalUsd: 9.68,
  estimatedItemCount: 2,
  items: [
    { itemId: 'a', name: 'Milk', quantity: '1 gallon', estimatedUsd: 4.19, basis: 'Store brand' },
    { itemId: 'b', name: 'Coffee', quantity: null, estimatedUsd: 5.49, basis: null },
    { itemId: 'c', name: 'Birthday card', quantity: null, estimatedUsd: null, basis: 'Not food' },
  ],
  note: 'Prices are national averages.',
  estimatedAt: '2026-11-10T18:00:00.000Z',
}

interface SheetProps {
  estimate?: CostEstimate | null
  loading?: boolean
  error?: string | null
  available?: boolean
}

function mountSheet({
  estimate = null,
  loading = false,
  error = null,
  available = true,
}: SheetProps) {
  return mount(CostEstimateSheet, {
    props: { open: true, estimate, loading, error, available },
    attachTo: document.body,
  })
}

describe('CostEstimateSheet', () => {
  beforeAll(() => {
    polyfillDialog()
  })

  beforeEach(() => {
    document.body.innerHTML = ''
  })

  it('shows the total, then every item with its price', () => {
    const wrapper = mountSheet({ estimate: ESTIMATE })

    expect(wrapper.get('[data-total]').text()).toBe('$9.68')
    expect(wrapper.text()).toContain("for 2 of 3 items; 1 couldn't be priced")
    const rows = wrapper
      .findAll('li')
      .map((row) => row.findAll('p').map((part) => part.text().replace(/\s+/g, ' ')))
    expect(rows).toEqual([
      ['Milk · 1 gallon', 'Store brand', '$4.19'],
      ['Coffee', '$5.49'],
      ['Birthday card', 'Not food', 'No estimate'],
    ])
    expect(wrapper.text()).toContain('Prices are national averages.')
    expect(wrapper.text()).toContain('Average prices across the U.S.')
  })

  it('lists the items in the order the screen shows them', () => {
    const wrapper = mount(CostEstimateSheet, {
      props: { open: true, estimate: ESTIMATE, available: true, order: ['c', 'a'] },
      attachTo: document.body,
    })

    const names = wrapper.findAll('li').map((row) => row.get('p').text().replace(/\s+/g, ' '))
    expect(names).toEqual(['Birthday card', 'Milk · 1 gallon', 'Coffee'])
  })

  it('says how many items the total covers when all are priced', () => {
    const wrapper = mountSheet({
      estimate: {
        ...ESTIMATE,
        estimatedItemCount: 1,
        items: ESTIMATE.items.slice(0, 1),
        note: null,
      },
    })

    expect(wrapper.text()).toContain('for 1 item')
    expect(wrapper.text()).not.toContain("couldn't be priced")
  })

  it('shows AI-written text as text, not markup', () => {
    const wrapper = mountSheet({
      estimate: { ...ESTIMATE, note: '<img src=x onerror=alert(1)>' },
    })

    expect(wrapper.find('img').exists()).toBe(false)
    expect(wrapper.text()).toContain('<img src=x onerror=alert(1)>')
  })

  it('shows progress while the list is being priced', () => {
    const wrapper = mountSheet({ loading: true, estimate: ESTIMATE })

    expect(wrapper.get('[role="status"]').text()).toContain('Pricing the list')
    expect(wrapper.find('[data-total]').exists()).toBe(false)
  })

  it('shows why an estimate failed, and asks again on request', async () => {
    const wrapper = mountSheet({ error: 'The AI is busy right now. Please try again in a minute.' })

    expect(wrapper.get('[role="alert"]').text()).toContain('The AI is busy right now.')
    const retry = wrapper.findAll('button').find((button) => button.text() === 'Try again')
    await retry!.trigger('click')

    expect(wrapper.emitted('estimate')).toHaveLength(1)
  })

  it('explains when the server can’t estimate', () => {
    const wrapper = mountSheet({ available: false, estimate: ESTIMATE })

    expect(wrapper.text()).toContain('it needs a Gemini API key')
    expect(wrapper.find('[data-total]').exists()).toBe(false)
  })
})
