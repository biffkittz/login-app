import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { h } from 'vue'
import { createMemoryHistory, createRouter } from 'vue-router'

import { polyfillDialog } from '@/components/grocery/__tests__/dialog-polyfill'
import * as groceryApi from '@/services/grocery-api'
import { createGroceryListHub } from '@/services/grocery-list-hub'
import { useAuthStore } from '@/stores/auth'
import { useGroceryListStore } from '@/stores/grocery-list'
import type { CostEstimate, GroceryItem, GroceryList } from '@/types/grocery'
import { makeRun, participant } from '@/utils/__tests__/test-runs'
import { makeItem } from '@/utils/__tests__/test-items'

import GroceryListView from '../grocery-list-view.vue'

vi.mock('@/services/grocery-api')
vi.mock('@/services/grocery-list-hub')

function listOf(items: GroceryItem[], overrides: Partial<GroceryList> = {}): GroceryList {
  return {
    items,
    stores: ['Whole Foods', 'Indian Grocery', 'Teeter/Lion', 'Costco'],
    settings: {
      version: 1,
      imageGenerationEnabled: true,
      imageGenerationAvailable: true,
      costEstimateAvailable: true,
      groceryRunsAvailable: false,
    },
    run: null,
    lastCallMinutes: [15, 30, 60, 120],
    ...overrides,
  }
}

async function mountView(...items: GroceryItem[]) {
  return mountListView(listOf(items))
}

async function mountListView(list: GroceryList) {
  vi.mocked(groceryApi.getList).mockResolvedValue(list)
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', name: 'home', component: GroceryListView },
      { path: '/login', name: 'login', component: { render: () => h('p', 'login') } },
    ],
  })
  await router.push('/')
  await router.isReady()

  const wrapper = mount(GroceryListView, { global: { plugins: [router] }, attachTo: document.body })
  await flushPromises()
  return wrapper
}

const rowNames = (wrapper: Awaited<ReturnType<typeof mountView>>): string[] =>
  wrapper.findAll('li').map((row) => row.find('button:nth-of-type(2) span span').text())

describe('GroceryListView', () => {
  beforeAll(polyfillDialog)

  beforeEach(() => {
    setActivePinia(createPinia())
    document.body.innerHTML = ''
    useAuthStore().$patch({
      user: {
        id: 'me',
        email: 'ada@example.com',
        displayName: 'Ada',
        sessionExpiresAt: new Date(Date.now() + 3_600_000).toISOString(),
      },
    })
    vi.mocked(createGroceryListHub).mockReturnValue({
      start: vi.fn().mockResolvedValue(undefined),
      stop: vi.fn().mockResolvedValue(undefined),
      restart: vi.fn().mockResolvedValue(undefined),
    })
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('shows the shared list in order and connects for live updates', async () => {
    const wrapper = await mountView(
      makeItem({ id: 'b', name: 'Bread', position: 2 }),
      makeItem({ id: 'a', name: 'Apples', position: 1 }),
    )

    expect(rowNames(wrapper)).toEqual(['Apples', 'Bread'])
    expect(createGroceryListHub).toHaveBeenCalledOnce()
    expect(wrapper.get('h1').text()).toBe('Grocery list')
  })

  it('groups the list by store: no store first, then the stores in their offered order', async () => {
    const wrapper = await mountView(
      makeItem({ id: 'a', name: 'Apples', position: 1, store: 'Costco' }),
      makeItem({ id: 'b', name: 'Bread', position: 2 }),
      makeItem({ id: 'c', name: 'Chai', position: 3, store: 'Indian Grocery' }),
      makeItem({ id: 'd', name: 'Dates', position: 4, store: 'Costco' }),
    )

    const sections = wrapper.findAll('section section')
    expect(sections.map((section) => section.get('h2').text().replace(/\s+/g, ' '))).toEqual([
      'No store: 1 item',
      'Indian Grocery: 1 item',
      'Costco: 2 items',
    ])
    expect(
      sections[2]?.findAll('li').map((row) => row.find('button:nth-of-type(2) span span').text()),
    ).toEqual(['Apples', 'Dates'])
    expect(sections[2]?.get('ul').attributes('aria-label')).toBe('Costco items')
  })

  it('moves an item within its store, placing it next to the right neighbor in the whole list', async () => {
    const wrapper = await mountView(
      makeItem({ id: 'a', name: 'Apples', position: 1, store: 'Costco' }),
      makeItem({ id: 'b', name: 'Bread', position: 2 }),
      makeItem({ id: 'd', name: 'Dates', position: 3, store: 'Costco' }),
    )
    vi.mocked(groceryApi.moveItem).mockReturnValue(new Promise(() => {}))

    await wrapper.get('[aria-label="Move Apples"]').trigger('keydown', { key: 'ArrowDown' })

    expect(groceryApi.moveItem).toHaveBeenCalledWith('a', { afterItemId: 'd' })
  })

  it('invites the user to add something when the list is empty', async () => {
    const wrapper = await mountView()

    expect(wrapper.text()).toContain('The list is empty.')
  })

  it('adds an item at the top of the list', async () => {
    const wrapper = await mountView(makeItem({ id: 'a', name: 'Apples' }))
    vi.mocked(groceryApi.createItem).mockReturnValue(new Promise(() => {}))

    await wrapper.get('form input').setValue('Milk')
    await wrapper.get('form').trigger('submit')

    expect(rowNames(wrapper)).toEqual(['Milk', 'Apples'])
    expect(groceryApi.createItem).toHaveBeenCalledWith(expect.objectContaining({ name: 'Milk' }))
  })

  it('checks an item off, then offers to undo it', async () => {
    vi.useFakeTimers()
    const wrapper = await mountView(
      makeItem({ id: 'a', name: 'Apples', position: 1 }),
      makeItem({ id: 'b', name: 'Bread', position: 2, store: 'Aldi' }),
    )
    vi.mocked(groceryApi.deleteItem).mockResolvedValue({ savedItems: [], deletedItemIds: ['b'] })
    vi.mocked(groceryApi.createItem).mockReturnValue(new Promise(() => {}))

    await wrapper.get('[aria-label="Check off Bread"]').trigger('click')
    expect(wrapper.get('[aria-label="Check off Bread"]').attributes('aria-pressed')).toBe('true')

    await vi.advanceTimersByTimeAsync(400)
    expect(rowNames(wrapper)).toEqual(['Apples'])
    expect(groceryApi.deleteItem).toHaveBeenCalledWith('b')
    expect(wrapper.text()).toContain('Checked off Bread.')

    const undo = wrapper.findAll('button').find((button) => button.text() === 'Undo')
    await undo?.trigger('click')

    expect(rowNames(wrapper)).toEqual(['Apples', 'Bread'])
    expect(groceryApi.createItem).toHaveBeenCalledWith(
      expect.objectContaining({ name: 'Bread', store: 'Aldi', afterItemId: 'a' }),
    )
  })

  it('saves the changed details of an item from its editor', async () => {
    const wrapper = await mountView(makeItem({ id: 'a', name: 'Apples', version: 1 }))
    vi.mocked(groceryApi.updateItem).mockResolvedValue({
      savedItems: [makeItem({ id: 'a', name: 'Apples', quantity: '6', version: 2 })],
      deletedItemIds: [],
    })

    await wrapper.get('li button:nth-of-type(2)').trigger('click')
    const quantityId = wrapper
      .findAll('dialog label')
      .find((label) => label.text() === 'Quantity')
      ?.attributes('for')
    await wrapper.get(`[id="${quantityId}"]`).setValue('6')
    await wrapper.get('dialog form').trigger('submit')
    await flushPromises()

    expect(groceryApi.updateItem).toHaveBeenCalledWith('a', { quantity: '6' })
    expect(wrapper.get('dialog').attributes('open')).toBeUndefined()
    expect(wrapper.get('li').text()).toContain('6')
  })

  it('lines names up once any item has a picture', async () => {
    const wrapper = await mountView(
      makeItem({ id: 'a', name: 'Apples', imageUrl: '/api/grocery-list/images/1' }),
      makeItem({ id: 'b', name: 'Bread', store: 'Costco' }),
    )

    // Bread, in another section and without a picture, still gets a tile.
    expect(wrapper.findAll('li [data-item-picture]')).toHaveLength(2)
    expect(wrapper.find('li [data-picture-none]').exists()).toBe(true)
  })

  it('shows no picture tiles while no item has a picture', async () => {
    const wrapper = await mountView(makeItem({ id: 'a', name: 'Apples' }))

    expect(wrapper.find('li [data-item-picture]').exists()).toBe(false)
  })

  it('turns AI pictures off for everyone from the list settings', async () => {
    const wrapper = await mountView(makeItem({ id: 'a', name: 'Apples' }))
    vi.mocked(groceryApi.updateSettings).mockResolvedValue({
      version: 2,
      imageGenerationEnabled: false,
      imageGenerationAvailable: true,
      costEstimateAvailable: true,
      groceryRunsAvailable: false,
    })

    await wrapper.get('button[aria-label="List settings"]').trigger('click')
    const sheet = wrapper
      .findAll('dialog')
      .find((dialog) => dialog.text().includes('List settings'))
    expect(sheet?.attributes('open')).toBeDefined()
    await sheet!.get('input[role="switch"]').setValue(false)
    await flushPromises()

    expect(groceryApi.updateSettings).toHaveBeenCalledWith({ imageGenerationEnabled: false })
    expect(sheet!.get<HTMLInputElement>('input[role="switch"]').element.checked).toBe(false)
  })

  it('explains in the settings sheet when the setting could not be changed', async () => {
    const wrapper = await mountView()
    vi.mocked(groceryApi.updateSettings).mockRejectedValue(
      Object.assign(new Error('offline'), { isAxiosError: true, code: 'ERR_NETWORK' }),
    )

    await wrapper.get('button[aria-label="List settings"]').trigger('click')
    const sheet = wrapper
      .findAll('dialog')
      .find((dialog) => dialog.text().includes('List settings'))
    await sheet!.get('input[role="switch"]').setValue(false)
    await flushPromises()

    expect(sheet!.get('[role="alert"]').text()).toContain("Couldn't change the setting.")
    expect(sheet!.get<HTMLInputElement>('input[role="switch"]').element.checked).toBe(true)
  })

  it('tells the user when a change could not be saved', async () => {
    const wrapper = await mountView()
    vi.mocked(groceryApi.createItem).mockRejectedValue(
      Object.assign(new Error('offline'), { isAxiosError: true, code: 'ERR_NETWORK' }),
    )

    await wrapper.get('form input').setValue('Milk')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(wrapper.text()).toContain("We couldn't reach the server.")
    expect(wrapper.findAll('li')).toHaveLength(0)
  })

  describe('Estimate Cost', () => {
    const estimate: CostEstimate = {
      totalUsd: 7.68,
      estimatedItemCount: 2,
      items: [
        { itemId: 'a', name: 'Apples', quantity: '3 lb', estimatedUsd: 4.19, basis: 'Bagged' },
        { itemId: 'b', name: 'Bread', quantity: null, estimatedUsd: 3.49, basis: null },
      ],
      note: null,
      estimatedAt: '2026-11-10T18:00:00.000Z',
    }

    const estimateButton = (wrapper: Awaited<ReturnType<typeof mountView>>) =>
      wrapper.findAll('button').find((button) => button.text() === 'Estimate Cost')

    const estimateSheet = (wrapper: Awaited<ReturnType<typeof mountView>>) =>
      wrapper.findAll('dialog').find((dialog) => dialog.text().includes('Estimated cost'))

    it('prices the whole list when the button is pressed', async () => {
      const wrapper = await mountView(
        makeItem({ id: 'a', name: 'Apples', quantity: '3 lb', position: 1 }),
        makeItem({ id: 'b', name: 'Bread', position: 2 }),
      )
      vi.mocked(groceryApi.estimateCost).mockResolvedValue(estimate)

      await estimateButton(wrapper)!.trigger('click')
      await flushPromises()

      expect(groceryApi.estimateCost).toHaveBeenCalledOnce()
      const sheet = estimateSheet(wrapper)
      expect(sheet?.attributes('open')).toBeDefined()
      expect(sheet!.get('[data-total]').text()).toBe('$7.68')
      expect(sheet!.text()).toContain('Bread')
    })

    it("isn't offered while the list is empty", async () => {
      const wrapper = await mountView()

      expect(estimateButton(wrapper)).toBeUndefined()
    })

    it('shows why the estimate failed, and tries again on request', async () => {
      const wrapper = await mountView(makeItem({ id: 'a', name: 'Apples' }))
      vi.mocked(groceryApi.estimateCost).mockRejectedValueOnce(
        Object.assign(new Error('Too many'), {
          isAxiosError: true,
          response: {
            status: 429,
            data: {
              title:
                'The app has made its 500 cost estimates for today. Please try again tomorrow.',
            },
          },
        }),
      )

      await estimateButton(wrapper)!.trigger('click')
      await flushPromises()
      const sheet = estimateSheet(wrapper)!
      expect(sheet.get('[role="alert"]').text()).toContain('500 cost estimates for today')

      vi.mocked(groceryApi.estimateCost).mockResolvedValueOnce(estimate)
      await sheet
        .findAll('button')
        .find((button) => button.text() === 'Try again')!
        .trigger('click')
      await flushPromises()

      expect(groceryApi.estimateCost).toHaveBeenCalledTimes(2)
      expect(estimateSheet(wrapper)!.get('[data-total]').text()).toBe('$7.68')
    })

    it("doesn't call the server when it can't estimate", async () => {
      const wrapper = await mountView(makeItem({ id: 'a', name: 'Apples' }))
      const groceryList = useGroceryListStore()
      groceryList.settings = { ...groceryList.settings!, costEstimateAvailable: false }
      await flushPromises()

      await estimateButton(wrapper)!.trigger('click')
      await flushPromises()

      expect(groceryApi.estimateCost).not.toHaveBeenCalled()
      expect(estimateSheet(wrapper)!.text()).toContain('it needs a Gemini API key')
    })
  })
  describe('grocery runs', () => {
    const RUNS_ON = {
      version: 1,
      imageGenerationEnabled: true,
      imageGenerationAvailable: true,
      costEstimateAvailable: true,
      groceryRunsAvailable: true,
    }

    it('offers to start a run, and starts one with the chosen last call', async () => {
      vi.mocked(groceryApi.startGroceryRun).mockResolvedValue('run-1')
      const wrapper = await mountListView(listOf([], { settings: RUNS_ON }))

      const start = wrapper
        .findAll('button')
        .find((button) => button.text() === 'Start grocery run')
      expect(start).toBeDefined()
      await start!.trigger('click')
      await flushPromises()
      const sheet = wrapper
        .findAll('dialog')
        .find((dialog) => dialog.text().includes('Start a grocery run'))!
      await sheet.get('input[value="15"]').setValue(true)
      await sheet.get('form').trigger('submit')
      await flushPromises()

      expect(groceryApi.startGroceryRun).toHaveBeenCalledWith(15)
      expect(wrapper.text()).toContain('Starting…')
    })

    it('shows last call, locks checking off and moving, and hides the start button', async () => {
      const run = makeRun({
        shopperId: 'ben',
        shopperName: 'Ben',
        participants: [participant('me', 'Ada')],
      })
      const wrapper = await mountListView(
        listOf([makeItem({ id: 'a', name: 'Apples' })], { settings: RUNS_ON, run }),
      )

      expect(wrapper.get('[data-grocery-run] h2').text()).toBe('Last call: Ben is going shopping')
      expect(
        wrapper.get<HTMLButtonElement>('button[aria-label="Check off Apples"]').element.disabled,
      ).toBe(true)
      expect(
        wrapper.get<HTMLButtonElement>('button[aria-label="Move Apples"]').element.disabled,
      ).toBe(true)
      expect(
        wrapper.findAll('button').some((button) => button.text() === 'Start grocery run'),
      ).toBe(false)
    })

    it('sends "I\'m good" from the banner', async () => {
      vi.mocked(groceryApi.sendGroceryRunAction).mockResolvedValue()
      const run = makeRun({
        shopperId: 'ben',
        shopperName: 'Ben',
        participants: [participant('me', 'Ada')],
      })
      const wrapper = await mountListView(listOf([], { settings: RUNS_ON, run }))

      await wrapper.get('[data-grocery-run] button').trigger('click')
      await flushPromises()

      expect(groceryApi.sendGroceryRunAction).toHaveBeenCalledWith('run-1', 'ready')
      expect(wrapper.get('[data-grocery-run]').text()).toContain("You're good to go")
    })

    it('tells everyone else when a run starts, and when the shopping begins', async () => {
      const wrapper = await mountListView(listOf([], { settings: RUNS_ON }))
      const groceryList = useGroceryListStore()

      const run = makeRun({
        shopperId: 'ben',
        shopperName: 'Ben',
        participants: [participant('me', 'Ada')],
      })
      groceryList.applyRun(run)
      await flushPromises()
      expect(wrapper.text()).toContain('Last call: Ben is going shopping. Add anything you need.')

      groceryList.applyRun({ ...run, sequence: 2, status: 'shopping', lastCallOutcome: 'timeUp' })
      await flushPromises()
      expect(wrapper.text()).toContain('The list is final: Ben is shopping.')
      expect(wrapper.get('[data-grocery-run] h2').text()).toBe('Ben is shopping')
    })
  })
})
