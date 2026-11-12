import { mount } from '@vue/test-utils'
import { beforeAll, beforeEach, describe, expect, it } from 'vitest'

import type { GroceryListSettings } from '@/types/grocery'

import ListSettings from '../list-settings.vue'
import { polyfillDialog } from './dialog-polyfill'

const PICTURES_ON: GroceryListSettings = {
  version: 1,
  imageGenerationEnabled: true,
  imageGenerationAvailable: true,
  costEstimateAvailable: true,
  groceryRunsAvailable: false,
}

function mountSettings(settings: GroceryListSettings | null, error: string | null = null) {
  return mount(ListSettings, {
    props: { open: true, settings, error },
    attachTo: document.body,
  })
}

describe('ListSettings', () => {
  beforeAll(() => {
    polyfillDialog()
  })

  beforeEach(() => {
    document.body.innerHTML = ''
  })

  it('shows the picture setting as a labeled, described switch', () => {
    const wrapper = mountSettings(PICTURES_ON)

    const toggle = wrapper.get<HTMLInputElement>('input[role="switch"]')
    expect(toggle.element.checked).toBe(true)
    expect(toggle.element.disabled).toBe(false)
    expect(wrapper.get(`label[for="${toggle.attributes('id')}"]`).text()).toBe('AI pictures')
    const description = wrapper.get(`#${toggle.attributes('aria-describedby')}`).text()
    expect(description).toContain('only the name is sent')
  })

  it('asks to turn pictures off when the switch is flipped', async () => {
    const wrapper = mountSettings(PICTURES_ON)

    await wrapper.get('input[role="switch"]').setValue(false)

    expect(wrapper.emitted('setImageGeneration')).toEqual([[false]])
  })

  it('explains, and disables the switch, when the server can’t draw pictures', () => {
    const wrapper = mountSettings({ ...PICTURES_ON, imageGenerationAvailable: false })

    expect(wrapper.get<HTMLInputElement>('input[role="switch"]').element.disabled).toBe(true)
    expect(wrapper.text()).toContain('it needs a Gemini API key')
  })

  it('shows why a change failed', () => {
    const wrapper = mountSettings(PICTURES_ON, "Couldn't change the setting.")

    expect(wrapper.get('[role="alert"]').text()).toContain("Couldn't change the setting.")
  })
})
