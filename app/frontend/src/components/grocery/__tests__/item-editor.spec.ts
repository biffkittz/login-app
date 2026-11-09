import { mount, type VueWrapper } from '@vue/test-utils'
import { beforeAll, beforeEach, describe, expect, it } from 'vitest'

import type { GroceryItem } from '@/types/grocery'
import { makeItem } from '@/utils/__tests__/test-items'

import ItemEditor from '../item-editor.vue'

import { polyfillDialog } from './dialog-polyfill'

function mountEditor(item: GroceryItem | null, props: Record<string, unknown> = {}) {
  return mount(ItemEditor, {
    props: {
      open: true,
      item,
      index: 1,
      count: 3,
      groupLabel: 'Costco',
      stores: ['Whole Foods', 'Indian Grocery', 'Teeter/Lion', 'Costco'],
      currentUserId: 'me',
      ...props,
    },
    attachTo: document.body,
  })
}

function field(wrapper: VueWrapper, label: string) {
  const id = wrapper
    .findAll('label')
    .find((element) => element.text() === label)
    ?.attributes('for')
  return wrapper.get(`[id="${id}"]`)
}

describe('ItemEditor', () => {
  beforeAll(polyfillDialog)

  beforeEach(() => {
    document.body.innerHTML = ''
  })

  it('opens with the item’s details', () => {
    const wrapper = mountEditor(makeItem({ id: '1', name: 'Milk', brand: 'Organic Valley' }))

    expect(wrapper.get('dialog').attributes('open')).toBeDefined()
    expect((field(wrapper, 'Item').element as HTMLInputElement).value).toBe('Milk')
    expect((field(wrapper, 'Brand').element as HTMLInputElement).value).toBe('Organic Valley')
    expect(wrapper.findAll('select option').map((option) => option.text())).toEqual([
      'No store',
      'Whole Foods',
      'Indian Grocery',
      'Teeter/Lion',
      'Costco',
    ])
  })

  it('saves only the fields that were changed', async () => {
    const wrapper = mountEditor(makeItem({ id: '1', name: 'Milk', brand: 'Organic Valley' }))

    await field(wrapper, 'Quantity').setValue(' 2 gallons ')
    await field(wrapper, 'Brand').setValue('')
    await wrapper.get('form').trigger('submit')

    expect(wrapper.emitted('save')).toEqual([[{ quantity: '2 gallons', brand: '' }]])
  })

  it('just closes when nothing was changed', async () => {
    const wrapper = mountEditor(makeItem({ id: '1', name: 'Milk' }))

    await wrapper.get('form').trigger('submit')

    expect(wrapper.emitted('save')).toBeUndefined()
    expect(wrapper.emitted('update:open')).toEqual([[false]])
  })

  it('saves a store picked from the dropdown, or no store', async () => {
    const wrapper = mountEditor(makeItem({ id: '1', name: 'Paneer', store: 'Costco' }))
    const store = field(wrapper, 'Store')

    expect((store.element as HTMLSelectElement).value).toBe('Costco')
    await store.setValue('Indian Grocery')
    await wrapper.get('form').trigger('submit')
    await store.setValue('')
    await wrapper.get('form').trigger('submit')

    expect(wrapper.emitted('save')).toEqual([[{ store: 'Indian Grocery' }], [{ store: '' }]])
  })

  it('selects an offered store whatever its case, without counting that as a change', async () => {
    const wrapper = mountEditor(makeItem({ id: '1', name: 'Rice', store: 'costco' }))

    expect((field(wrapper, 'Store').element as HTMLSelectElement).value).toBe('Costco')
    await wrapper.get('form').trigger('submit')

    expect(wrapper.emitted('save')).toBeUndefined()
  })

  it('keeps showing a store that is no longer offered, so saving other changes keeps it', async () => {
    const wrapper = mountEditor(makeItem({ id: '1', name: 'Stroopwafels', store: 'Aldi' }))

    expect((field(wrapper, 'Store').element as HTMLSelectElement).value).toBe('Aldi')
    expect(wrapper.findAll('select option').at(-1)?.text()).toBe('Aldi (no longer offered)')
    await field(wrapper, 'Quantity').setValue('2')
    await wrapper.get('form').trigger('submit')

    expect(wrapper.emitted('save')).toEqual([[{ quantity: '2' }]])
  })

  it('requires a name', async () => {
    const wrapper = mountEditor(makeItem({ id: '1', name: 'Milk' }))

    await field(wrapper, 'Item').setValue('  ')
    await wrapper.get('form').trigger('submit')

    expect(wrapper.text()).toContain('Enter what to buy.')
    expect(wrapper.emitted('save')).toBeUndefined()
  })

  it('shows who added and last changed the item', () => {
    const wrapper = mountEditor(
      makeItem({
        id: '1',
        createdAt: '2020-01-02T10:00:00.000Z',
        lastModifiedAt: '2020-01-03T10:00:00.000Z',
        lastModifiedById: 'grace',
        lastModifiedByName: 'Grace',
      }),
    )

    expect(wrapper.text()).toMatch(/Added .+ Last changed .+ by Grace/)
  })

  it('points out a change someone else made while it was open', async () => {
    const wrapper = mountEditor(makeItem({ id: '1', lastModifiedAt: '2026-11-09T10:00:00.000Z' }))

    await wrapper.setProps({
      item: makeItem({
        id: '1',
        version: 2,
        lastModifiedAt: '2026-11-09T10:05:00.000Z',
        lastModifiedById: 'grace',
        lastModifiedByName: 'Grace',
      }),
    })

    expect(wrapper.get('[role="status"]').text()).toContain(
      'Grace changed this item while you had it open',
    )
  })

  it('explains that the item is gone when someone else removes it', async () => {
    const wrapper = mountEditor(makeItem({ id: '1' }))

    await wrapper.setProps({ item: null })

    expect(wrapper.text()).toContain('Someone has just removed this item')
    expect(wrapper.find('button[type="submit"]').exists()).toBe(false)
  })

  it('offers move buttons that stop at either end of the list', async () => {
    const wrapper = mountEditor(makeItem({ id: '1' }), { index: 0, count: 2 })
    const [up, down] = ['Move up', 'Move down'].map((label) =>
      wrapper.findAll('button').find((button) => button.text().includes(label)),
    )

    expect(up?.attributes('disabled')).toBeDefined()
    await down?.trigger('click')

    expect(wrapper.emitted('moveBy')).toEqual([[1]])
    expect(wrapper.text().replace(/\s+/g, ' ')).toContain('Position 1 of 2 in Costco')
  })
})
