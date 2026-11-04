import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'

import TextField from '../text-field.vue'

function mountField(props: Record<string, unknown> = {}) {
  const wrapper = mount(TextField, {
    props: {
      label: 'Email address',
      modelValue: '',
      // Mirrors what `v-model` does in a parent: feed the emitted value back in as the prop.
      'onUpdate:modelValue': (value: string) => wrapper.setProps({ modelValue: value }),
      ...props,
    },
  })
  return wrapper
}

describe('TextField', () => {
  it('labels the input', () => {
    const wrapper = mountField()
    const input = wrapper.get('input')

    expect(wrapper.get(`label[for="${input.attributes('id')}"]`).text()).toBe('Email address')
  })

  it('supports two-way binding through v-model (defineModel)', async () => {
    const wrapper = mountField({ modelValue: 'ada' })
    const input = wrapper.get('input')
    expect(input.element.value).toBe('ada')

    await input.setValue('ada@example.com')

    expect(wrapper.emitted('update:modelValue')?.at(-1)).toEqual(['ada@example.com'])
    expect(wrapper.props('modelValue')).toBe('ada@example.com')
  })

  it('links the hint and error to the input for screen readers', () => {
    const wrapper = mountField({
      hint: 'We never share it.',
      error: 'Enter a valid email address.',
    })
    const input = wrapper.get('input')

    expect(input.attributes('aria-invalid')).toBe('true')
    const describedBy = input.attributes('aria-describedby')?.split(' ') ?? []
    const descriptions = describedBy.map((id) => wrapper.get(`[id="${id}"]`).text())
    expect(descriptions).toEqual(['We never share it.', 'Enter a valid email address.'])
  })

  it('is not marked invalid without an error', () => {
    const input = mountField().get('input')

    expect(input.attributes('aria-invalid')).toBeUndefined()
    expect(input.attributes('aria-describedby')).toBeUndefined()
  })

  it('passes extra attributes to the input and reports blur', async () => {
    const wrapper = mountField({ inputmode: 'email', required: true })
    const input = wrapper.get('input')

    expect(input.attributes('inputmode')).toBe('email')
    expect(input.attributes('aria-required')).toBe('true')

    await input.trigger('blur')
    expect(wrapper.emitted('blur')).toHaveLength(1)
  })
})
