import { mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it } from 'vitest'

import { makeItem } from '@/utils/__tests__/test-items'

import GroceryItemList from '../grocery-item-list.vue'

const items = [
  makeItem({ id: 'a', name: 'Apples', quantity: '6', store: 'Aldi' }),
  makeItem({ id: 'b', name: 'Bread', brand: 'Dave’s', notes: 'Sliced' }),
  makeItem({ id: 'c', name: 'Cheese' }),
]

function mountList() {
  return mount(GroceryItemList, {
    props: { items, checkedIds: new Set<string>(), label: 'Costco items' },
    attachTo: document.body,
  })
}

// jsdom doesn't lay anything out, so give every row a 50px slot, as a browser would.
function layOutRows(rows: HTMLElement[]): void {
  rows.forEach((row, index) => {
    row.getBoundingClientRect = () => new DOMRect(0, index * 50, 300, 50)
  })
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

describe('GroceryItemList', () => {
  beforeEach(() => {
    document.body.innerHTML = ''
  })

  it('shows an item’s picture left of its name, as decoration that loads when needed', () => {
    const wrapper = mount(GroceryItemList, {
      props: {
        items: [makeItem({ id: 'a', name: 'Apples', imageUrl: '/api/grocery-list/images/1' })],
        checkedIds: new Set<string>(),
        label: 'Costco items',
      },
    })

    const button = wrapper.get('li button:nth-of-type(2)')
    const picture = button.get('[data-item-picture]')
    const image = picture.get('img')
    expect(button.element.firstElementChild).toBe(picture.element)
    expect(picture.attributes('aria-hidden')).toBe('true')
    expect(image.attributes()).toMatchObject({
      src: '/api/grocery-list/images/1',
      alt: '',
      width: '40',
      height: '40',
      loading: 'lazy',
    })
    expect(button.text()).toBe('Apples')
  })

  it('shows a placeholder while the picture is being drawn, and leaves no gap for items without one', () => {
    const wrapper = mount(GroceryItemList, {
      props: {
        items: [
          makeItem({ id: 'a', name: 'Apples', imagePending: true }),
          makeItem({ id: 'b', name: 'Bread' }),
        ],
        checkedIds: new Set<string>(),
        label: 'Costco items',
      },
    })

    const [waiting, without] = wrapper.findAll('li')
    expect(waiting?.find('[data-picture-pending]').exists()).toBe(true)
    expect(waiting?.find('img').exists()).toBe(false)
    expect(without?.find('[data-item-picture]').exists()).toBe(false)
  })

  it('gives items without a picture a plain tile when others have one, so the names line up', () => {
    const wrapper = mount(GroceryItemList, {
      props: {
        items: [makeItem({ id: 'b', name: 'Bread' })],
        checkedIds: new Set<string>(),
        label: 'Costco items',
        pictureColumn: true,
      },
    })

    const tile = wrapper.get('li button:nth-of-type(2) [data-item-picture]')
    expect(tile.find('[data-picture-none]').exists()).toBe(true)
    expect(tile.find('img').exists()).toBe(false)
  })

  it('shows the plain tile instead of a broken image when a picture fails to load', async () => {
    const wrapper = mount(GroceryItemList, {
      props: {
        items: [makeItem({ id: 'a', name: 'Apples', imageUrl: '/api/grocery-list/images/1' })],
        checkedIds: new Set<string>(),
        label: 'Costco items',
      },
    })

    await wrapper.get('img').trigger('error')

    expect(wrapper.find('[data-picture-none]').exists()).toBe(true)
    expect(wrapper.find('img').exists()).toBe(false)
  })

  it('shows each item with its details and labeled controls', () => {
    const wrapper = mountList()
    const rows = wrapper.findAll('li')

    expect(rows.map((row) => row.find('button:nth-of-type(2)').text())).toEqual([
      expect.stringContaining('Apples'),
      expect.stringContaining('Bread'),
      expect.stringContaining('Cheese'),
    ])
    expect(rows[0]?.text()).toContain('6')
    // The store isn't repeated on rows: the list's group heading names it.
    expect(rows[0]?.text()).not.toContain('Aldi')
    expect(rows[1]?.text()).toContain('Dave’s')
    expect(rows[1]?.text()).toContain('Sliced')
    expect(rows[0]?.find('[aria-label="Check off Apples"]').exists()).toBe(true)
    expect(rows[0]?.get('[aria-label="Move Apples"]').attributes('aria-describedby')).toBeTruthy()
  })

  it('reports checking off and opening an item', async () => {
    const wrapper = mountList()

    await wrapper.get('[aria-label="Check off Bread"]').trigger('click')
    await wrapper.findAll('li')[2]?.find('button:nth-of-type(2)').trigger('click')

    expect(wrapper.emitted('check')).toEqual([[items[1]]])
    expect(wrapper.emitted('edit')).toEqual([[items[2]]])
  })

  it('moves an item with the arrow keys on its handle', async () => {
    const wrapper = mountList()

    await wrapper.get('[aria-label="Move Bread"]').trigger('keydown', { key: 'ArrowDown' })
    await wrapper.get('[aria-label="Move Apples"]').trigger('keydown', { key: 'ArrowUp' })

    expect(wrapper.emitted('move')).toEqual([[items[1], 2]])
  })

  it('moves an item dragged by its handle past its neighbors', () => {
    const wrapper = mountList()
    layOutRows(wrapper.findAll('li').map((row) => row.element as HTMLElement))
    const handle = wrapper.get('[aria-label="Move Apples"]').element

    handle.dispatchEvent(pointer('pointerdown', 25))
    handle.dispatchEvent(pointer('pointermove', 90))
    handle.dispatchEvent(pointer('pointermove', 140))
    handle.dispatchEvent(pointer('pointerup', 140))

    expect(wrapper.emitted('dragStart')).toHaveLength(1)
    expect(wrapper.emitted('move')).toEqual([[items[0], 2]])
    expect(wrapper.emitted('dragEnd')).toHaveLength(1)
    // Every row is back in its slot; the parent re-renders the new order.
    expect(
      wrapper.findAll('li').every((row) => (row.element as HTMLElement).style.transform === ''),
    ).toBe(true)
  })

  it('leaves the order alone when a drag is canceled', () => {
    const wrapper = mountList()
    layOutRows(wrapper.findAll('li').map((row) => row.element as HTMLElement))
    const handle = wrapper.get('[aria-label="Move Apples"]').element

    handle.dispatchEvent(pointer('pointerdown', 25))
    handle.dispatchEvent(pointer('pointermove', 140))
    handle.dispatchEvent(pointer('pointercancel', 140))

    expect(wrapper.emitted('move')).toBeUndefined()
    expect(wrapper.emitted('dragEnd')).toHaveLength(1)
  })
})
