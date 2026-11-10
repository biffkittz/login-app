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

  it('reports a press on a move handle as the start of a drag of that row', () => {
    const wrapper = mountList()

    wrapper.get('[aria-label="Move Bread"]').element.dispatchEvent(pointer('pointerdown', 75))

    expect(wrapper.emitted('dragStart')).toEqual([[expect.any(PointerEvent), 1]])
  })

  it('marks where a row dragged from another section would land, above a row or below the last', async () => {
    const wrapper = mountList()
    const markers = () =>
      wrapper.findAll('li').map((row) => {
        const marker = row.find('[data-drop-marker]')
        return marker.exists() ? marker.classes().find((name) => name.includes('-[3px]')) : null
      })

    expect(markers()).toEqual([null, null, null])

    await wrapper.setProps({ dropIndex: 1 })
    expect(markers()).toEqual([null, '-top-[3px]', null])

    await wrapper.setProps({ dropIndex: 3 })
    expect(markers()).toEqual([null, null, '-bottom-[3px]'])
  })

  it('lifts the row being dragged', async () => {
    const wrapper = mountList()

    await wrapper.setProps({ draggingIndex: 2 })

    expect(
      wrapper
        .findAll('li')
        .map((row) => row.classes().includes('shadow-[4px_4px_0_0_var(--color-walnut-800)]')),
    ).toEqual([false, false, true])
  })
})
