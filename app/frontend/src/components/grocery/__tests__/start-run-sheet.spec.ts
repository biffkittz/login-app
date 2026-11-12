import { mount } from '@vue/test-utils'
import { beforeAll, beforeEach, describe, expect, it } from 'vitest'

import StartRunSheet from '../start-run-sheet.vue'
import { polyfillDialog } from './dialog-polyfill'

function mountSheet(props: { available?: boolean; error?: string | null } = {}) {
  return mount(StartRunSheet, {
    props: { open: false, lengths: [15, 30, 60, 120], available: true, ...props },
    attachTo: document.body,
  })
}

describe('StartRunSheet', () => {
  beforeAll(() => {
    polyfillDialog()
  })

  beforeEach(() => {
    document.body.innerHTML = ''
  })

  it('offers the lengths of last call, with half an hour chosen', async () => {
    const wrapper = mountSheet()
    await wrapper.setProps({ open: true })

    const choices = wrapper.findAll('label')
    expect(choices.map((choice) => choice.text())).toEqual([
      '15 minutes',
      '30 minutes',
      '1 hour',
      '2 hours',
    ])
    expect(wrapper.get<HTMLInputElement>('input[value="30"]').element.checked).toBe(true)
  })

  it('starts a run with the chosen length', async () => {
    const wrapper = mountSheet()
    await wrapper.setProps({ open: true })

    await wrapper.get('input[value="60"]').setValue(true)
    await wrapper.get('form').trigger('submit')

    expect(wrapper.emitted('start')).toEqual([[60]])
  })

  it('says so when the server can’t hold runs', async () => {
    const wrapper = mountSheet({ available: false })
    await wrapper.setProps({ open: true })

    expect(wrapper.text()).toContain("Grocery runs aren't set up on this server")
    expect(wrapper.find('fieldset').exists()).toBe(false)
    expect(wrapper.find('button[type="submit"]').exists()).toBe(false)
  })

  it('shows why starting failed', async () => {
    const wrapper = mountSheet({ error: 'A grocery run is already under way.' })
    await wrapper.setProps({ open: true })

    expect(wrapper.get('[role="alert"]').text()).toContain('already under way')
  })
})
