import { mount } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { nextTick } from 'vue'

import { makeItem } from '@/utils/__tests__/test-items'
import { groupByStore, type StoreGroup } from '@/utils/store-groups'

import GroceryStoreGroups from '../grocery-store-groups.vue'

const STORES = ['Whole Foods', 'Indian Grocery', 'Teeter/Lion', 'Costco']

// No store: Bread. Whole Foods: Walnuts, Wasabi. Costco: Coffee. Aldi (no longer offered): Apfelstrudel.
const items = [
  makeItem({ id: 'b', name: 'Bread', position: 0 }),
  makeItem({ id: 'w1', name: 'Walnuts', position: 1, store: 'Whole Foods' }),
  makeItem({ id: 'c1', name: 'Coffee', position: 2, store: 'Costco' }),
  makeItem({ id: 'w2', name: 'Wasabi', position: 3, store: 'Whole Foods' }),
  makeItem({ id: 'a1', name: 'Apfelstrudel', position: 4, store: 'Aldi' }),
]
const groups = groupByStore(items, STORES)
const group = (key: string): StoreGroup => {
  const found = groups.find((candidate) => candidate.key === key)
  if (found === undefined) {
    throw new Error(`No group ${key}`)
  }
  return found
}

// jsdom doesn't lay anything out. Stack the groups as a browser would, from the page as it is at the moment: a
// 30px heading, 50px per row (or for an empty drop zone), and 20px between groups.
function fakeLayout(element: Element): DOMRect {
  let y = 0
  for (const groupElement of document.querySelectorAll('[data-reorder-group]')) {
    const top = y
    y += 30
    const rows = groupElement.querySelectorAll('[data-reorder-item]')
    for (const row of rows) {
      if (row === element) {
        return new DOMRect(0, y, 300, 50)
      }
      y += 50
    }
    if (rows.length === 0) {
      y += 50
    }
    if (groupElement === element) {
      return new DOMRect(0, top, 300, y - top)
    }
    y += 20
  }
  return new DOMRect(0, 0, 0, 0)
}

const centerOf = (element: Element): number => {
  const rect = fakeLayout(element)
  return rect.top + rect.height / 2
}

function pointer(type: string, clientY: number): PointerEvent {
  return new PointerEvent(type, {
    bubbles: true,
    cancelable: true,
    clientY,
    pointerId: 1,
    isPrimary: true,
    button: 0,
    pointerType: 'touch',
  })
}

function mountGroups() {
  return mount(GroceryStoreGroups, {
    props: { groups, stores: STORES, checkedIds: new Set<string>() },
    attachTo: document.body,
  })
}

type Wrapper = ReturnType<typeof mountGroups>

const row = (wrapper: Wrapper, id: string): Element => wrapper.get(`[data-item-id="${id}"]`).element

const groupElement = (wrapper: Wrapper, key: string): Element =>
  wrapper.get(`[data-reorder-group="${key}"]`).element

/** Presses the handle of item `id` at its center, and returns where that was. */
async function grab(wrapper: Wrapper, id: string): Promise<{ handle: Element; startY: number }> {
  const handle = wrapper.get(`[data-item-id="${id}"] [data-reorder-handle]`).element
  const startY = centerOf(row(wrapper, id))
  handle.dispatchEvent(pointer('pointerdown', startY))
  // The drop zones for empty stores render, and are measured, on the next tick.
  await nextTick()
  await nextTick()
  return { handle, startY }
}

/** Moves the grabbed row so that its center is at `y`. */
async function dragTo(handle: Element, y: number): Promise<void> {
  handle.dispatchEvent(pointer('pointermove', y))
  await nextTick()
}

describe('GroceryStoreGroups', () => {
  beforeEach(() => {
    document.body.innerHTML = ''
    vi.spyOn(Element.prototype, 'getBoundingClientRect').mockImplementation(function (
      this: Element,
    ) {
      return fakeLayout(this)
    })
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('shows each store’s section with its heading and items', () => {
    const wrapper = mountGroups()

    const sections = wrapper.findAll('section')
    expect(sections.map((section) => section.get('h2').text())).toEqual([
      expect.stringContaining('No store'),
      expect.stringContaining('Whole Foods'),
      expect.stringContaining('Costco'),
      expect.stringContaining('Aldi'),
    ])
    expect(sections[1]?.get('ul').attributes('aria-label')).toBe('Whole Foods items')
    expect(sections[1]?.findAll('li').map((item) => item.attributes('data-item-id'))).toEqual([
      'w1',
      'w2',
    ])
  })

  it('reorders an item dragged within its own section, as before', async () => {
    const wrapper = mountGroups()
    const { handle } = await grab(wrapper, 'w1')

    await dragTo(handle, centerOf(row(wrapper, 'w2')) + 10)
    expect(wrapper.find('[data-drop-marker]').exists()).toBe(false)
    expect(wrapper.find('[data-moving-to]').exists()).toBe(false)
    handle.dispatchEvent(pointer('pointerup', 0))

    expect(wrapper.emitted('move')).toEqual([
      [items[1], group('Whole Foods'), group('Whole Foods'), 1],
    ])
    expect(wrapper.emitted('dragStart')).toHaveLength(1)
    expect(wrapper.emitted('dragEnd')).toHaveLength(1)
  })

  it('moves an item dragged into another store’s section there, marking the spot while dragging', async () => {
    const wrapper = mountGroups()
    const { handle } = await grab(wrapper, 'c1')

    // Between Walnuts and Wasabi: past the center of the first, short of the second's.
    await dragTo(handle, centerOf(row(wrapper, 'w1')) + 10)

    const wholeFoods = wrapper.findAll('section')[1]
    expect(wholeFoods?.find('[data-drop-target]').exists()).toBe(true)
    expect(
      wholeFoods?.findAll('li').map((item) => item.find('[data-drop-marker]').exists()),
    ).toEqual([false, true])
    // It covers that spot, so it says where it is going.
    expect(wrapper.get(`[data-item-id="c1"] [data-moving-to]`).text()).toBe('Whole Foods')
    // The dragged row follows the finger; the rows there don't step aside, which would push them out of their card.
    expect((row(wrapper, 'c1') as HTMLElement).style.transform).toContain('translateY(')
    expect(
      wholeFoods?.findAll('li').map((item) => (item.element as HTMLElement).style.transform),
    ).toEqual(['', ''])

    handle.dispatchEvent(pointer('pointerup', 0))
    await nextTick()

    expect(wrapper.emitted('move')).toEqual([[items[2], group('Costco'), group('Whole Foods'), 1]])
    expect(wrapper.find('[data-drop-marker]').exists()).toBe(false)
    expect(wrapper.find('[data-drop-target]').exists()).toBe(false)
    expect(wrapper.find('[data-moving-to]').exists()).toBe(false)
  })

  it('counts a drop on a section’s heading as a drop at the top of that section', async () => {
    const wrapper = mountGroups()
    const { handle } = await grab(wrapper, 'b')

    await dragTo(handle, fakeLayout(groupElement(wrapper, 'Whole Foods')).top + 5)
    handle.dispatchEvent(pointer('pointerup', 0))

    expect(wrapper.emitted('move')).toEqual([[items[0], group(''), group('Whole Foods'), 0]])
  })

  it('offers stores without items as drop zones while dragging, and moves the item into one', async () => {
    const wrapper = mountGroups()
    expect(wrapper.find('[data-reorder-group="Indian Grocery"]').exists()).toBe(false)

    const { handle } = await grab(wrapper, 'b')
    const zones = wrapper.findAll('[data-reorder-group]').slice(4)
    expect(zones.map((zone) => zone.text())).toEqual(['Indian Grocery', 'Teeter/Lion'])

    await dragTo(handle, centerOf(groupElement(wrapper, 'Indian Grocery')))
    expect(
      wrapper.get('[data-reorder-group="Indian Grocery"]').attributes('data-drop-target'),
    ).toBe('true')
    handle.dispatchEvent(pointer('pointerup', 0))
    await nextTick()

    expect(wrapper.emitted('move')).toEqual([
      [
        items[0],
        group(''),
        { key: 'Indian Grocery', store: 'Indian Grocery', label: 'Indian Grocery', items: [] },
        0,
      ],
    ])
    expect(wrapper.find('[data-reorder-group="Indian Grocery"]').exists()).toBe(false)
  })

  it('doesn’t take drops into a store that is no longer offered', async () => {
    const wrapper = mountGroups()
    const { handle } = await grab(wrapper, 'c1')

    await dragTo(handle, centerOf(row(wrapper, 'a1')))
    expect(wrapper.find('[data-drop-marker]').exists()).toBe(false)
    expect(wrapper.find('[data-drop-target]').exists()).toBe(false)
    handle.dispatchEvent(pointer('pointerup', 0))

    expect(wrapper.emitted('move')).toBeUndefined()
    expect(wrapper.emitted('dragEnd')).toHaveLength(1)
  })

  it('leaves everything alone when a drag is canceled', async () => {
    const wrapper = mountGroups()
    const { handle } = await grab(wrapper, 'c1')

    await dragTo(handle, centerOf(row(wrapper, 'w1')))
    handle.dispatchEvent(pointer('pointercancel', 0))
    await nextTick()

    expect(wrapper.emitted('move')).toBeUndefined()
    expect(wrapper.emitted('dragEnd')).toHaveLength(1)
    expect(wrapper.find('[data-drop-marker]').exists()).toBe(false)
    expect(
      wrapper.findAll('li').every((item) => (item.element as HTMLElement).style.transform === ''),
    ).toBe(true)
  })

  it('passes on keyboard moves within a section', async () => {
    const wrapper = mountGroups()

    await wrapper.get('[aria-label="Move Walnuts"]').trigger('keydown', { key: 'ArrowDown' })

    expect(wrapper.emitted('move')).toEqual([
      [items[1], group('Whole Foods'), group('Whole Foods'), 1],
    ])
  })
})
