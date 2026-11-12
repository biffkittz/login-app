import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { ApiError } from '@/services/api-error'
import * as groceryApi from '@/services/grocery-api'
import { createGroceryListHub, type GroceryListHubHandlers } from '@/services/grocery-list-hub'
import type {
  GroceryItem,
  GroceryList,
  GroceryListChange,
  GroceryListSettings,
} from '@/types/grocery'
import { makeItem } from '@/utils/__tests__/test-items'
import { makeRun, participant } from '@/utils/__tests__/test-runs'

import { useAuthStore } from '../auth'
import { useGroceryListStore } from '../grocery-list'

vi.mock('@/services/grocery-api')
vi.mock('@/services/grocery-list-hub')

function saved(...items: GroceryItem[]): GroceryListChange {
  return { savedItems: items, deletedItemIds: [] }
}

function deleted(...itemIds: string[]): GroceryListChange {
  return { savedItems: [], deletedItemIds: itemIds }
}

const STORES = ['Whole Foods', 'Indian Grocery', 'Teeter/Lion', 'Costco']

const PICTURES_ON: GroceryListSettings = {
  version: 1,
  imageGenerationEnabled: true,
  imageGenerationAvailable: true,
  costEstimateAvailable: true,
  groceryRunsAvailable: false,
}

function listOf(items: GroceryItem[], settings: GroceryListSettings = PICTURES_ON): GroceryList {
  return { items, stores: STORES, settings, run: null, lastCallMinutes: [15, 30, 60, 120] }
}

const networkError = () => new ApiError("We couldn't reach the server.", { kind: 'network' })

function signIn(): void {
  useAuthStore().$patch({
    user: {
      id: 'me',
      email: 'ada@example.com',
      displayName: 'Ada',
      sessionExpiresAt: new Date(Date.now() + 3_600_000).toISOString(),
    },
  })
}

async function storeWith(...items: GroceryItem[]): Promise<ReturnType<typeof useGroceryListStore>> {
  vi.mocked(groceryApi.getList).mockResolvedValue(listOf(items))
  const store = useGroceryListStore()
  await store.load()
  return store
}

const ids = (store: ReturnType<typeof useGroceryListStore>): string[] =>
  store.items.map((item) => item.id)

describe('grocery list store', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    signIn()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  describe('loading', () => {
    it('shows the list in position order', async () => {
      const store = await storeWith(
        makeItem({ id: 'b', position: 2 }),
        makeItem({ id: 'a', position: 1 }),
      )

      expect(ids(store)).toEqual(['a', 'b'])
      expect(store.loadState).toBe('ready')
    })

    it('reports a failed first load', async () => {
      vi.mocked(groceryApi.getList).mockRejectedValue(networkError())
      const store = useGroceryListStore()

      await store.load()

      expect(store.loadState).toBe('error')
      expect(store.loadError?.kind).toBe('network')
    })

    it('keeps the list on screen when a later reload fails', async () => {
      const store = await storeWith(makeItem({ id: 'a' }))
      vi.mocked(groceryApi.getList).mockRejectedValue(networkError())

      await store.load()

      expect(ids(store)).toEqual(['a'])
      expect(store.loadState).toBe('ready')
    })

    it('applies changes that arrive during a reload after it, so none are lost', async () => {
      const store = await storeWith(makeItem({ id: 'a', version: 1 }))
      let answer!: (list: GroceryList) => void
      vi.mocked(groceryApi.getList).mockReturnValue(new Promise((resolve) => (answer = resolve)))

      const reload = store.load()
      // Sent after the snapshot was read, but received before the snapshot arrives.
      store.applyChange(saved(makeItem({ id: 'b', version: 1, position: 5 })))
      store.applyChange(deleted('a'))
      answer(listOf([makeItem({ id: 'a', version: 1 })]))
      await reload

      expect(ids(store)).toEqual(['b'])
    })

    it('signs the user out when the server says the session has ended', async () => {
      vi.mocked(groceryApi.getList).mockRejectedValue(
        new ApiError('Your session has ended.', { kind: 'http', status: 401 }),
      )
      const store = useGroceryListStore()

      await store.load()

      expect(useAuthStore().isAuthenticated).toBe(false)
      expect(useAuthStore().signOutReason).toBe('session-expired')
    })

    it('forgets the list when the user signs out', async () => {
      const store = await storeWith(makeItem({ id: 'a' }))

      useAuthStore().expireSession()
      await vi.waitFor(() => expect(store.items).toEqual([]))

      expect(store.loadState).toBe('idle')
    })
  })

  describe('realtime changes', () => {
    it('keeps the copy with the highest version, whatever order changes arrive in', async () => {
      const store = await storeWith(makeItem({ id: 'a', name: 'Milk', version: 1 }))

      store.applyChange(saved(makeItem({ id: 'a', name: 'Oat milk', version: 3 })))
      store.applyChange(saved(makeItem({ id: 'a', name: 'Whole milk', version: 2 })))

      expect(store.items[0]?.name).toBe('Oat milk')
    })

    it('never brings back an item that was removed', async () => {
      const store = await storeWith(makeItem({ id: 'a', version: 1 }))

      store.applyChange(deleted('a'))
      store.applyChange(saved(makeItem({ id: 'a', version: 2 })))

      expect(store.items).toEqual([])
    })

    it('holds changes back while asked to, then applies them in order', async () => {
      const store = await storeWith(makeItem({ id: 'a', position: 1 }))

      store.holdChanges()
      store.applyChange(saved(makeItem({ id: 'b', position: 0 })))
      expect(ids(store)).toEqual(['a'])

      store.releaseChanges()
      expect(ids(store)).toEqual(['b', 'a'])
    })

    it('offers the stores the server lists, and groups the list by them', async () => {
      const store = await storeWith(
        makeItem({ id: 'a', store: 'Costco', position: 1 }),
        makeItem({ id: 'b', store: 'Whole Foods', position: 2 }),
        makeItem({ id: 'c', store: 'Costco', position: 3 }),
        makeItem({ id: 'd', position: 4 }),
      )

      expect(store.stores).toEqual(STORES)
      expect(
        store.groups.map((group) => [group.label, group.items.map((item) => item.id)]),
      ).toEqual([
        ['No store', ['d']],
        ['Whole Foods', ['b']],
        ['Costco', ['a', 'c']],
      ])
    })

    it('regroups an item as soon as its store changes', async () => {
      const store = await storeWith(makeItem({ id: 'a', store: 'Costco', version: 1 }))

      store.applyChange(saved(makeItem({ id: 'a', store: 'Teeter/Lion', version: 2 })))

      expect(store.groups.map((group) => group.label)).toEqual(['Teeter/Lion'])
    })
  })

  describe('adding', () => {
    it('shows the item at once, at the top, then settles on the server’s copy', async () => {
      const store = await storeWith(makeItem({ id: 'a', position: 0 }))
      let answer!: (change: GroceryListChange) => void
      vi.mocked(groceryApi.createItem).mockReturnValue(new Promise((resolve) => (answer = resolve)))

      const adding = store.addItem({ name: '  Milk ', quantity: ' 2 ' })

      const optimistic = store.items[0]
      expect(optimistic).toMatchObject({
        name: 'Milk',
        quantity: '2',
        version: 0,
        lastModifiedByName: 'Ada',
      })
      expect(groceryApi.createItem).toHaveBeenCalledWith(
        expect.objectContaining({
          id: optimistic?.id,
          name: 'Milk',
          quantity: '2',
          afterItemId: null,
        }),
      )

      answer(saved(makeItem({ id: optimistic!.id, name: 'Milk', version: 1, position: -1024 })))
      await adding
      expect(store.items[0]).toMatchObject({ id: optimistic?.id, version: 1 })
      expect(store.items).toHaveLength(2)
    })

    it('puts an item back after a given neighbor, for "Undo"', async () => {
      const store = await storeWith(
        makeItem({ id: 'a', position: 0 }),
        makeItem({ id: 'b', position: 10 }),
      )
      vi.mocked(groceryApi.createItem).mockReturnValue(new Promise(() => {}))

      void store.addItem({ name: 'Restored' }, 'a')

      expect(store.items.map((item) => item.name)).toEqual(['Item a', 'Restored', 'Item b'])
      expect(groceryApi.createItem).toHaveBeenCalledWith(
        expect.objectContaining({ afterItemId: 'a' }),
      )
    })

    it('shows a new item waiting for its picture while pictures are on', async () => {
      const store = await storeWith()
      vi.mocked(groceryApi.createItem).mockReturnValue(new Promise(() => {}))

      void store.addItem({ name: 'Milk' })

      expect(store.items[0]).toMatchObject({ imageUrl: null, imagePending: true })
    })

    it.each([
      ['off', { ...PICTURES_ON, imageGenerationEnabled: false }],
      ['unavailable', { ...PICTURES_ON, imageGenerationAvailable: false }],
    ])('expects no picture for a new item while pictures are %s', async (_, settings) => {
      vi.mocked(groceryApi.getList).mockResolvedValue(listOf([], settings))
      const store = useGroceryListStore()
      await store.load()
      vi.mocked(groceryApi.createItem).mockReturnValue(new Promise(() => {}))

      void store.addItem({ name: 'Milk' })

      expect(store.items[0]).toMatchObject({ imageUrl: null, imagePending: false })
    })

    it('takes the item off the list again when the server refuses it', async () => {
      const store = await storeWith()
      vi.mocked(groceryApi.createItem).mockRejectedValue(
        new ApiError('The list is full.', { kind: 'http', status: 409 }),
      )

      await expect(store.addItem({ name: 'Milk' })).rejects.toBeInstanceOf(ApiError)

      expect(store.items).toEqual([])
    })
  })

  describe('editing', () => {
    it('shows changes at once and sends only the changed fields', async () => {
      const store = await storeWith(makeItem({ id: 'a', name: 'Milk', brand: 'Organic Valley' }))
      vi.mocked(groceryApi.updateItem).mockReturnValue(new Promise(() => {}))

      void store.updateItem('a', { quantity: ' 2 ', brand: '' })

      expect(store.items[0]).toMatchObject({ name: 'Milk', quantity: '2', brand: null })
      expect(groceryApi.updateItem).toHaveBeenCalledWith('a', { quantity: '2', brand: '' })
    })

    it('puts the item back when the server refuses the change', async () => {
      const original = makeItem({ id: 'a', name: 'Milk', version: 4 })
      const store = await storeWith(original)
      vi.mocked(groceryApi.updateItem).mockRejectedValue(networkError())

      await expect(store.updateItem('a', { name: 'Oat milk' })).rejects.toBeInstanceOf(ApiError)

      expect(store.items[0]).toEqual(original)
    })
  })

  describe('pictures of renamed items', () => {
    const pictured = () =>
      makeItem({ id: 'a', name: 'Oat milk', imageUrl: '/api/grocery-list/images/1' })

    it('expects a new picture when the name really changes', async () => {
      const store = await storeWith(pictured())
      vi.mocked(groceryApi.updateItem).mockReturnValue(new Promise(() => {}))

      void store.updateItem('a', { name: 'Almond milk' })

      expect(store.items[0]).toMatchObject({ imageUrl: null, imagePending: true })
    })

    it.each([
      ['its capitalization or spacing changes', { name: ' OAT   milk ' }],
      ['other details change', { quantity: '2' }],
    ])('keeps the picture when %s', async (_, changes) => {
      const store = await storeWith(pictured())
      vi.mocked(groceryApi.updateItem).mockReturnValue(new Promise(() => {}))

      void store.updateItem('a', changes)

      expect(store.items[0]).toMatchObject({
        imageUrl: '/api/grocery-list/images/1',
        imagePending: false,
      })
    })
  })

  describe('settings', () => {
    it('takes the settings from the list', async () => {
      const store = await storeWith()

      expect(store.settings).toEqual(PICTURES_ON)
    })

    it('keeps the newest settings, whatever order they arrive in', async () => {
      const store = await storeWith()
      const off = { ...PICTURES_ON, version: 3, imageGenerationEnabled: false }

      store.applySettings(off)
      store.applySettings({ ...PICTURES_ON, version: 2 })

      expect(store.settings).toEqual(off)
    })

    it('turns pictures off at once, then settles on the server’s answer', async () => {
      const store = await storeWith()
      let answer!: (settings: GroceryListSettings) => void
      vi.mocked(groceryApi.updateSettings).mockReturnValue(
        new Promise((resolve) => (answer = resolve)),
      )

      const turningOff = store.setImageGeneration(false)

      expect(store.settings?.imageGenerationEnabled).toBe(false)
      expect(groceryApi.updateSettings).toHaveBeenCalledWith({ imageGenerationEnabled: false })
      answer({ ...PICTURES_ON, version: 2, imageGenerationEnabled: false })
      await turningOff
      expect(store.settings).toEqual({ ...PICTURES_ON, version: 2, imageGenerationEnabled: false })
    })

    it('puts the switch back when the server refuses', async () => {
      const store = await storeWith()
      vi.mocked(groceryApi.updateSettings).mockRejectedValue(networkError())

      await expect(store.setImageGeneration(false)).rejects.toBeInstanceOf(ApiError)

      expect(store.settings).toEqual(PICTURES_ON)
    })

    it('sends nothing when the setting already has that value', async () => {
      const store = await storeWith()

      await store.setImageGeneration(true)

      expect(groceryApi.updateSettings).not.toHaveBeenCalled()
    })

    it('forgets the settings when the user signs out', async () => {
      const store = await storeWith()

      useAuthStore().expireSession()

      await vi.waitFor(() => expect(store.settings).toBeNull())
    })
  })

  describe('moving', () => {
    it('moves the item at once and tells the server which item it now follows', async () => {
      const store = await storeWith(
        makeItem({ id: 'a', position: 0 }),
        makeItem({ id: 'b', position: 1024 }),
        makeItem({ id: 'c', position: 2048 }),
      )
      vi.mocked(groceryApi.moveItem).mockReturnValue(new Promise(() => {}))

      void store.moveItem('a', 2)
      expect(ids(store)).toEqual(['b', 'c', 'a'])
      expect(groceryApi.moveItem).toHaveBeenLastCalledWith('a', { afterItemId: 'c' })

      void store.moveItem('c', 0)
      expect(ids(store)).toEqual(['c', 'b', 'a'])
      expect(groceryApi.moveItem).toHaveBeenLastCalledWith('c', { afterItemId: null })
    })

    it('moves the item back when the server refuses', async () => {
      const store = await storeWith(
        makeItem({ id: 'a', position: 0 }),
        makeItem({ id: 'b', position: 1024 }),
      )
      vi.mocked(groceryApi.moveItem).mockRejectedValue(networkError())

      await expect(store.moveItem('a', 1)).rejects.toBeInstanceOf(ApiError)

      expect(ids(store)).toEqual(['a', 'b'])
    })

    it('moves an item into another store at once, as one change by the current user', async () => {
      const store = await storeWith(
        makeItem({ id: 'a', position: 0, store: 'Costco' }),
        makeItem({ id: 'b', position: 1024, store: 'Whole Foods', lastModifiedById: 'someone' }),
      )
      vi.mocked(groceryApi.moveItem).mockReturnValue(new Promise(() => {}))

      void store.moveItem('b', 0, 'Costco')

      expect(ids(store)).toEqual(['b', 'a'])
      expect(store.items[0]).toMatchObject({ store: 'Costco', lastModifiedById: 'me' })
      expect(groceryApi.moveItem).toHaveBeenLastCalledWith('b', {
        afterItemId: null,
        store: 'Costco',
      })
    })

    it('changes only the store when the item keeps its place, and clears it with an empty store', async () => {
      const store = await storeWith(
        makeItem({ id: 'a', position: 0 }),
        makeItem({ id: 'b', position: 1024, store: 'Costco' }),
      )
      vi.mocked(groceryApi.moveItem).mockReturnValue(new Promise(() => {}))

      void store.moveItem('b', 1, '')

      expect(ids(store)).toEqual(['a', 'b'])
      expect(store.items[1]).toMatchObject({ store: null, position: 1024 })
      expect(groceryApi.moveItem).toHaveBeenLastCalledWith('b', { afterItemId: 'a', store: '' })
    })

    it('sends nothing for a store the item already has and a place it already is in', async () => {
      const store = await storeWith(makeItem({ id: 'a', position: 0, store: 'Costco' }))
      vi.mocked(groceryApi.moveItem).mockClear()

      await store.moveItem('a', 0, 'Costco')

      expect(groceryApi.moveItem).not.toHaveBeenCalled()
    })

    it('puts the item back in its store and place when the server refuses the store', async () => {
      const original = makeItem({ id: 'b', position: 1024, store: 'Whole Foods' })
      const store = await storeWith(makeItem({ id: 'a', position: 0, store: 'Costco' }), original)
      vi.mocked(groceryApi.moveItem).mockRejectedValue(networkError())

      await expect(store.moveItem('b', 0, 'Costco')).rejects.toBeInstanceOf(ApiError)

      expect(ids(store)).toEqual(['a', 'b'])
      expect(store.items[1]).toEqual(original)
    })
  })

  describe('removing', () => {
    it('removes the item at once', async () => {
      const store = await storeWith(makeItem({ id: 'a' }))
      vi.mocked(groceryApi.deleteItem).mockResolvedValue(deleted('a'))

      const removing = store.removeItem('a')

      expect(store.items).toEqual([])
      await removing
      expect(groceryApi.deleteItem).toHaveBeenCalledWith('a')
    })

    it('counts an item someone else removed first as removed', async () => {
      const store = await storeWith(makeItem({ id: 'a' }))
      vi.mocked(groceryApi.deleteItem).mockRejectedValue(
        new ApiError('Not found', { kind: 'http', status: 404 }),
      )

      await store.removeItem('a')

      expect(store.items).toEqual([])
    })

    it('puts the item back when the server refuses', async () => {
      const store = await storeWith(makeItem({ id: 'a' }))
      vi.mocked(groceryApi.deleteItem).mockRejectedValue(networkError())

      await expect(store.removeItem('a')).rejects.toBeInstanceOf(ApiError)

      expect(ids(store)).toEqual(['a'])
    })
  })

  describe('live connection', () => {
    function connectWithFakeHub(): GroceryListHubHandlers {
      let handlers: GroceryListHubHandlers | undefined
      vi.mocked(createGroceryListHub).mockImplementation((given) => {
        handlers = given
        return { start: vi.fn().mockResolvedValue(undefined), stop: vi.fn(), restart: vi.fn() }
      })
      useGroceryListStore().connect()
      return handlers!
    }

    it('applies changes pushed by the hub', async () => {
      vi.mocked(groceryApi.getList).mockResolvedValue(listOf([]))
      const hub = connectWithFakeHub()
      const store = useGroceryListStore()
      await vi.waitFor(() => expect(store.loadState).toBe('ready'))

      hub.onChange(saved(makeItem({ id: 'pushed' })))

      expect(ids(store)).toEqual(['pushed'])
    })

    it('applies settings pushed by the hub', async () => {
      vi.mocked(groceryApi.getList).mockResolvedValue(listOf([]))
      const hub = connectWithFakeHub()
      const store = useGroceryListStore()
      await vi.waitFor(() => expect(store.loadState).toBe('ready'))

      hub.onSettingsChange({ ...PICTURES_ON, version: 2, imageGenerationEnabled: false })

      expect(store.settings?.imageGenerationEnabled).toBe(false)
    })

    it('reloads the list on every (re)connection, to catch up on missed changes', async () => {
      vi.mocked(groceryApi.getList).mockResolvedValue(listOf([]))
      const hub = connectWithFakeHub()
      const store = useGroceryListStore()
      await vi.waitFor(() => expect(store.loadState).toBe('ready'))
      vi.mocked(groceryApi.getList).mockResolvedValue(listOf([makeItem({ id: 'missed' })]))

      hub.onConnected()

      await vi.waitFor(() => expect(ids(store)).toEqual(['missed']))
    })

    it('checks in over HTTP while the live connection is down, which reveals an ended session', async () => {
      vi.mocked(groceryApi.getList).mockResolvedValue(listOf([]))
      const hub = connectWithFakeHub()
      const store = useGroceryListStore()
      await vi.waitFor(() => expect(store.loadState).toBe('ready'))
      vi.mocked(groceryApi.getList).mockRejectedValue(
        new ApiError('Your session has ended.', { kind: 'http', status: 401 }),
      )

      hub.onStateChange('disconnected')

      await vi.waitFor(() => expect(useAuthStore().signOutReason).toBe('session-expired'))
      await store.disconnect()
    })

    it('keeps the list fresh over HTTP every 30 seconds until the live connection is back', async () => {
      vi.useFakeTimers()
      vi.mocked(groceryApi.getList).mockResolvedValue(listOf([]))
      const hub = connectWithFakeHub()
      await vi.advanceTimersByTimeAsync(0)
      vi.mocked(groceryApi.getList).mockClear()

      hub.onStateChange('disconnected')
      await vi.advanceTimersByTimeAsync(60_000)
      expect(groceryApi.getList).toHaveBeenCalledTimes(3)

      hub.onStateChange('connected')
      await vi.advanceTimersByTimeAsync(60_000)
      expect(groceryApi.getList).toHaveBeenCalledTimes(3)
      await useGroceryListStore().disconnect()
    })

    it('reports the connection state', () => {
      vi.mocked(groceryApi.getList).mockResolvedValue(listOf([]))
      const hub = connectWithFakeHub()

      hub.onStateChange('reconnecting')

      expect(useGroceryListStore().connectionState).toBe('reconnecting')
    })
  })
  describe('grocery runs', () => {
    it('shows the run the list came with, and the lengths of last call on offer', async () => {
      vi.mocked(groceryApi.getList).mockResolvedValue({ ...listOf([]), run: makeRun() })
      const store = useGroceryListStore()

      await store.load()

      expect(store.run?.id).toBe('run-1')
      expect(store.lastCallMinutes).toEqual([15, 30, 60, 120])
    })

    it('keeps the newest copy of a run, and a later run over an earlier one', async () => {
      const store = await storeWith()

      store.applyRun(makeRun({ sequence: 3, status: 'shopping' }))
      store.applyRun(makeRun({ sequence: 2 }))
      expect(store.run?.status).toBe('shopping')

      const earlier = makeRun({
        id: 'run-0',
        startedAt: '2026-11-01T10:00:00.000Z',
        status: 'finished',
      })
      store.applyRun(earlier)
      expect(store.run?.id).toBe('run-1')

      const later = makeRun({ id: 'run-2', startedAt: new Date(Date.now() + 1_000).toISOString() })
      store.applyRun(later)
      expect(store.run?.id).toBe('run-2')
    })

    it('applies run messages that arrive during a reload after it', async () => {
      const store = await storeWith()
      let answer!: (list: GroceryList) => void
      vi.mocked(groceryApi.getList).mockReturnValue(new Promise((resolve) => (answer = resolve)))

      const reloading = store.load()
      store.applyRun(makeRun({ sequence: 1 }))
      answer(listOf([]))
      await reloading

      // The reload's answer predates the message, which wins.
      expect(store.run?.id).toBe('run-1')
    })

    it('remembers the run it started until the run arrives', async () => {
      const store = await storeWith()
      vi.mocked(groceryApi.startGroceryRun).mockResolvedValue('run-1')

      await store.startRun(30)
      expect(groceryApi.startGroceryRun).toHaveBeenCalledWith(30)
      expect(store.startingRunId).toBe('run-1')

      store.applyRun(makeRun())
      expect(store.startingRunId).toBeNull()
    })

    it('shows "I\'m good" at once, and takes it back when the server refuses', async () => {
      const store = await storeWith()
      store.applyRun(
        makeRun({ participants: [participant('me', 'Ada'), participant('ben', 'Ben')] }),
      )
      vi.mocked(groceryApi.sendGroceryRunAction).mockRejectedValue(networkError())

      const sending = store.sendRunAction('ready')
      expect(store.run?.participants.find((asked) => asked.userId === 'me')?.ready).toBe(true)
      await expect(sending).rejects.toThrow()

      expect(store.run?.participants.find((asked) => asked.userId === 'me')?.ready).toBe(false)
      expect(groceryApi.sendGroceryRunAction).toHaveBeenCalledWith('run-1', 'ready')
    })

    it('reloads when the server says a run request no longer applies', async () => {
      const store = await storeWith()
      store.applyRun(makeRun())
      vi.mocked(groceryApi.sendGroceryRunAction).mockRejectedValue(
        new ApiError('Last call is over: the list is final.', { kind: 'http', status: 409 }),
      )
      vi.mocked(groceryApi.getList).mockClear()

      await expect(store.sendRunAction('start-shopping')).rejects.toThrow('Last call is over')

      expect(groceryApi.getList).toHaveBeenCalledOnce()
    })

    it('forgets the run when the user signs out', async () => {
      const store = await storeWith()
      store.applyRun(makeRun())

      useAuthStore().$patch({ user: null })
      await Promise.resolve()

      expect(store.run).toBeNull()
    })
  })
})
