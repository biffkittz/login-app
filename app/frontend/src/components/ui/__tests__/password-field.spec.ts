import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'

import PasswordField from '../password-field.vue'

describe('PasswordField', () => {
  it('hides the password until the user asks to see it', async () => {
    const wrapper = mount(PasswordField, {
      props: { label: 'Password', autocomplete: 'current-password', modelValue: 'hunter2' },
    })
    const input = wrapper.get('input')
    const toggle = wrapper.get('button')

    expect(input.attributes('type')).toBe('password')
    expect(toggle.text()).toBe('Show password')
    expect(toggle.attributes('aria-controls')).toBe(input.attributes('id'))

    await toggle.trigger('click')

    expect(input.attributes('type')).toBe('text')
    expect(toggle.text()).toBe('Hide password')
  })

  it('keeps browser autofill and spell-check behavior safe for passwords', () => {
    const input = mount(PasswordField, {
      props: { label: 'New password', autocomplete: 'new-password', modelValue: '' },
    }).get('input')

    expect(input.attributes('autocomplete')).toBe('new-password')
    expect(input.attributes('spellcheck')).toBe('false')
  })
})
