import { mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it } from 'vitest'

import AddItemForm from '../add-item-form.vue'

describe('AddItemForm', () => {
  beforeEach(() => {
    document.body.innerHTML = ''
  })

  it('adds the trimmed name, then clears the field and keeps it focused for the next item', async () => {
    const wrapper = mount(AddItemForm, { attachTo: document.body })
    const input = wrapper.get('input')

    await input.setValue('  Milk ')
    await wrapper.get('form').trigger('submit')

    expect(wrapper.emitted('add')).toEqual([['Milk']])
    expect((input.element as HTMLInputElement).value).toBe('')
    expect(document.activeElement).toBe(input.element)
  })

  it('ignores a blank name', async () => {
    const wrapper = mount(AddItemForm)

    await wrapper.get('input').setValue('   ')
    await wrapper.get('form').trigger('submit')

    expect(wrapper.emitted('add')).toBeUndefined()
  })

  it('has an accessible name for the field and stops at the server’s length limit', () => {
    const wrapper = mount(AddItemForm)
    const input = wrapper.get('input')

    expect(wrapper.get(`label[for="${input.attributes('id')}"]`).text()).toBe('New item')
    expect(input.attributes('maxlength')).toBe('100')
  })
})
