import { computed, ref, shallowRef, watch } from 'vue'
import { defineStore } from 'pinia'

import { toApiError, type ApiError } from '@/services/api-error'
import * as groceryApi from '@/services/grocery-api'
import { createGroceryListHub, type GroceryListHub } from '@/services/grocery-list-hub'
import { useAuthStore } from '@/stores/auth'
import type {
  ConnectionState,
  GroceryItem,
  GroceryItemChanges,
  GroceryItemDetails,
  GroceryListChange,
} from '@/types/grocery'
import { positionBetween, sortByPosition } from '@/utils/list-order'
import { groupByStore } from '@/utils/store-groups'

// How often the list is reloaded over HTTP while the realtime connection is down.
const OFFLINE_POLL_INTERVAL_MS = 30_000

/** Whether the list has been loaded. */
export type ListLoadState = 'idle' | 'loading' | 'ready' | 'error'

/** The details of a new item; only the name is required. */
export type NewItemDetails = Pick<GroceryItemDetails, 'name'> & Partial<GroceryItemDetails>

/**
 * The shared grocery list (Pinia setup store), kept in step with the server in real time.
 *
 * **How it stays consistent.** Every change reaches this store as a {@link GroceryListChange}: the answer to its own
 * writes, or a message from the realtime hub for everybody's writes (its own included). Changes can arrive twice and
 * in any order, so applying one follows two rules that make the order irrelevant:
 * - an item is replaced only by a copy with a **higher version** (the server increments it with every change);
 * - a removed item's ID is remembered, so a late copy can't bring it back. IDs are never reused.
 *
 * **Optimistic updates.** Adding, editing, moving and removing show on screen at once and are then confirmed by the
 * server's answer. If the server refuses, the item goes back to how it was, and the list is reloaded to be safe.
 *
 * **Missed changes.** While the realtime connection is down, nothing arrives, so the list is reloaded every time the
 * connection is (re-)established. Changes that arrive during a reload are held back and applied after it, as they
 * are while the user drags an item, so rows never jump under a finger.
 */
export const useGroceryListStore = defineStore('grocery-list', () => {
  const auth = useAuthStore()

  // Replaced, never mutated, on every change: each update is one cheap re-sort of a few hundred items, and rows whose
  // item didn't change keep the same object, so Vue skips re-rendering them.
  const itemsById = shallowRef<ReadonlyMap<string, GroceryItem>>(new Map())
  const loadState = ref<ListLoadState>('idle')
  const loadError = shallowRef<ApiError | null>(null)
  const connectionState = ref<ConnectionState>('disconnected')

  /** Every item, in list order. */
  const items = computed(() => sortByPosition(itemsById.value.values()))

  /** The stores an item can be bought at, as the server offers them (empty until the first load). */
  const stores = ref<string[]>([])

  /** The list grouped by store: no store first, then the offered stores in order, then any others. */
  const groups = computed(() => groupByStore(items.value, stores.value))

  const removedIds = new Set<string>()
  const heldChanges: GroceryListChange[] = []
  let holdCount = 0
  let loadGeneration = 0
  let hub: GroceryListHub | null = null
  let live = false
  let offlinePoll: ReturnType<typeof setInterval> | undefined

  function setItems(next: ReadonlyMap<string, GroceryItem>): void {
    itemsById.value = next
  }

  function putItem(item: GroceryItem): void {
    setItems(new Map(itemsById.value).set(item.id, item))
  }

  function dropItem(itemId: string): void {
    const next = new Map(itemsById.value)
    next.delete(itemId)
    setItems(next)
  }

  /**
   * Applies a change from the server, unless changes are being held back (see {@link holdChanges}). Safe to call
   * with duplicate, stale or out-of-order changes.
   */
  function applyChange(change: GroceryListChange): void {
    if (holdCount > 0) {
      heldChanges.push(change)
      return
    }

    const next = new Map(itemsById.value)
    let changed = false
    for (const itemId of change.deletedItemIds) {
      removedIds.add(itemId)
      changed = next.delete(itemId) || changed
    }
    for (const item of change.savedItems) {
      const current = next.get(item.id)
      if (!removedIds.has(item.id) && (current === undefined || item.version > current.version)) {
        next.set(item.id, item)
        changed = true
      }
    }
    if (changed) {
      setItems(next)
    }
  }

  /**
   * Holds back incoming changes until {@link releaseChanges} is called as many times as this was. Used while a row
   * is being dragged and while a reload is in flight. The app's own optimistic edits still show at once.
   */
  function holdChanges(): void {
    holdCount++
  }

  /** Applies the changes held back since {@link holdChanges}, in the order they arrived. */
  function releaseChanges(): void {
    holdCount = Math.max(holdCount - 1, 0)
    if (holdCount === 0) {
      for (const change of heldChanges.splice(0)) {
        applyChange(change)
      }
    }
  }

  // The API answers 401 when the session has ended on the server's side; the user must sign in again.
  function endSessionIfUnauthorized(error: ApiError): boolean {
    if (error.isUnauthorized) {
      auth.expireSession()
      return true
    }
    return false
  }

  // After a refused write the item has been put back; a reload makes sure everything else is right too, for example
  // when the item was deleted by someone else in the meantime.
  function recoverFromFailedWrite(error: unknown): ApiError {
    const apiError = toApiError(error)
    if (!endSessionIfUnauthorized(apiError)) {
      void load()
    }
    return apiError
  }

  // Undoes an optimistic edit, unless a newer copy has arrived from the server meanwhile.
  function restore(original: GroceryItem): void {
    const current = itemsById.value.get(original.id)
    if (current === undefined || current.version === original.version) {
      putItem(original)
    }
  }

  /**
   * Loads the whole list from the server, replacing what's shown. Changes that arrive meanwhile are applied afterwards.
   * Never throws: a failure is reported through {@link loadState} and {@link loadError}.
   */
  async function load(): Promise<void> {
    const generation = ++loadGeneration
    if (loadState.value !== 'ready') {
      loadState.value = 'loading'
    }

    holdChanges()
    try {
      const list = await groceryApi.getList()
      if (generation !== loadGeneration) {
        return // a newer load has started; its result wins
      }

      const next = new Map(
        list.items.filter((item) => !removedIds.has(item.id)).map((item) => [item.id, item]),
      )
      // Items added here whose creation the server hasn't confirmed yet (version 0) aren't in the snapshot. Keep them:
      // their own response will settle them.
      for (const item of itemsById.value.values()) {
        if (item.version === 0 && !next.has(item.id)) {
          next.set(item.id, item)
        }
      }
      setItems(next)
      stores.value = list.stores
      loadState.value = 'ready'
      loadError.value = null
    } catch (error: unknown) {
      const apiError = toApiError(error)
      // A list already on screen stays there; the connection indicator tells the user it may be out of date.
      if (
        !endSessionIfUnauthorized(apiError) &&
        generation === loadGeneration &&
        loadState.value !== 'ready'
      ) {
        loadState.value = 'error'
        loadError.value = apiError
      }
    } finally {
      releaseChanges()
    }
  }

  function currentEditor(): Pick<GroceryItem, 'lastModifiedById' | 'lastModifiedByName'> {
    return {
      lastModifiedById: auth.user?.id ?? null,
      lastModifiedByName: auth.user?.displayName ?? null,
    }
  }

  /**
   * Adds an item: at the top of the list, or after `afterItemId` when that item still exists ("Undo" uses this to
   * put an item back where it was).
   *
   * @throws {@link ApiError} when the server refuses; the item has been taken off the list again.
   */
  async function addItem(
    details: NewItemDetails,
    afterItemId: string | null = null,
  ): Promise<void> {
    const id = crypto.randomUUID()
    const fields = {
      name: details.name.trim(),
      quantity: details.quantity?.trim() ?? '',
      brand: details.brand?.trim() ?? '',
      store: details.store?.trim() ?? '',
      notes: details.notes?.trim() ?? '',
    }

    const list = items.value
    const anchorIndex =
      afterItemId === null ? -1 : list.findIndex((item) => item.id === afterItemId)
    const now = new Date().toISOString()
    putItem({
      id,
      name: fields.name,
      quantity: fields.quantity || null,
      brand: fields.brand || null,
      store: fields.store || null,
      notes: fields.notes || null,
      position: positionBetween(
        list[anchorIndex]?.position ?? null,
        list[anchorIndex + 1]?.position ?? null,
      ),
      version: 0, // not yet saved: any copy from the server is newer
      createdAt: now,
      lastModifiedAt: now,
      ...currentEditor(),
    })

    try {
      applyChange(await groceryApi.createItem({ id, ...fields, afterItemId }))
    } catch (error: unknown) {
      if (itemsById.value.get(id)?.version === 0) {
        dropItem(id)
      }
      throw recoverFromFailedWrite(error)
    }
  }

  /**
   * Saves changed details of an item. Only the fields in `changes` are sent, so someone else's simultaneous change
   * to a different field survives.
   *
   * @throws {@link ApiError} when the server refuses; the item has been put back as it was.
   */
  async function updateItem(itemId: string, changes: GroceryItemChanges): Promise<void> {
    const original = itemsById.value.get(itemId)
    if (original === undefined || Object.keys(changes).length === 0) {
      return
    }

    const trimmed = Object.fromEntries(
      Object.entries(changes).map(([field, value]) => [field, value.trim()]),
    ) as GroceryItemChanges
    putItem({
      ...original,
      ...(trimmed.name ? { name: trimmed.name } : {}),
      ...optional(trimmed, 'quantity'),
      ...optional(trimmed, 'brand'),
      ...optional(trimmed, 'store'),
      ...optional(trimmed, 'notes'),
      lastModifiedAt: new Date().toISOString(),
      ...currentEditor(),
    })

    try {
      applyChange(await groceryApi.updateItem(itemId, trimmed))
    } catch (error: unknown) {
      restore(original)
      throw recoverFromFailedWrite(error)
    }
  }

  /**
   * Moves an item so that it ends up at `toIndex` in the list.
   *
   * @throws {@link ApiError} when the server refuses; the item has gone back to where it was.
   */
  async function moveItem(itemId: string, toIndex: number): Promise<void> {
    const list = items.value
    const fromIndex = list.findIndex((item) => item.id === itemId)
    const original = list[fromIndex]
    if (original === undefined || toIndex === fromIndex || toIndex < 0 || toIndex >= list.length) {
      return
    }

    const others = list.filter((item) => item.id !== itemId)
    const before = others[toIndex - 1] ?? null
    const after = others[toIndex] ?? null
    putItem({
      ...original,
      position: positionBetween(before?.position ?? null, after?.position ?? null),
    })

    try {
      applyChange(await groceryApi.moveItem(itemId, before?.id ?? null))
    } catch (error: unknown) {
      restore(original)
      throw recoverFromFailedWrite(error)
    }
  }

  /**
   * Removes an item. An item someone else removed first counts as removed.
   *
   * @throws {@link ApiError} when the server refuses; the item is back on the list.
   */
  async function removeItem(itemId: string): Promise<void> {
    const item = itemsById.value.get(itemId)
    if (item === undefined) {
      return
    }

    removedIds.add(itemId)
    dropItem(itemId)
    try {
      applyChange(await groceryApi.deleteItem(itemId))
    } catch (error: unknown) {
      const apiError = toApiError(error)
      if (apiError.status === 404) {
        return
      }
      removedIds.delete(itemId)
      if (!itemsById.value.has(itemId)) {
        putItem(item)
      }
      throw recoverFromFailedWrite(apiError)
    }
  }

  // While the realtime connection keeps failing, check in over HTTP now and then. Browsers don't reveal why a
  // WebSocket was refused, so this is how the app finds out that the real problem is a session the server no longer
  // accepts (a 401 signs the user out). It also keeps the list fresh on networks that block WebSockets.
  function pollWhileOffline(state: ConnectionState): void {
    clearInterval(offlinePoll)
    offlinePoll = undefined
    if (live && state === 'disconnected') {
      void load()
      offlinePoll = setInterval(() => void load(), OFFLINE_POLL_INTERVAL_MS)
    }
  }

  /** Starts showing live changes: loads the list and opens the realtime connection, which reloads on every reconnect. */
  function connect(): void {
    live = true
    hub ??= createGroceryListHub({
      onChange: applyChange,
      onStateChange: (state) => {
        connectionState.value = state
        pollWhileOffline(state)
      },
      onConnected: () => void load(),
    })
    void hub.start()
    // Show the list without waiting for the WebSocket; the reload once it connects closes any gap.
    if (loadState.value !== 'ready') {
      void load()
    }
  }

  /** Closes the realtime connection. */
  async function disconnect(): Promise<void> {
    live = false
    clearInterval(offlinePoll)
    await hub?.stop()
  }

  /**
   * Reconnects now, e.g. when the page returns from the background or the device comes back online. The list is
   * reloaded over HTTP at the same time, so the user sees it as it is without waiting for the WebSocket.
   */
  async function reconnect(): Promise<void> {
    if (live) {
      void load()
      await hub?.restart()
    }
  }

  // Signing out clears the list from memory. Everyone shares one list, but it is still nobody's business on a
  // device whose user has left.
  watch(
    () => auth.isAuthenticated,
    (isAuthenticated) => {
      if (!isAuthenticated) {
        loadGeneration++
        setItems(new Map())
        removedIds.clear()
        heldChanges.length = 0
        loadState.value = 'idle'
        loadError.value = null
      }
    },
  )

  return {
    items,
    stores,
    groups,
    loadState,
    loadError,
    connectionState,
    load,
    applyChange,
    holdChanges,
    releaseChanges,
    addItem,
    updateItem,
    moveItem,
    removeItem,
    connect,
    disconnect,
    reconnect,
  }
})

// `''` clears an optional field; a field that isn't in `changes` keeps its value.
function optional(
  changes: GroceryItemChanges,
  field: 'quantity' | 'brand' | 'store' | 'notes',
): Partial<GroceryItem> {
  const value = changes[field]
  return value === undefined ? {} : { [field]: value === '' ? null : value }
}
