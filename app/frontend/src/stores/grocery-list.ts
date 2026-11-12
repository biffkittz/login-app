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
  GroceryListSettings,
  GroceryRun,
  GroceryRunAction,
} from '@/types/grocery'
import { pictureNameKey } from '@/utils/grocery-format'
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
 *
 * **Pictures and settings.** An item's AI picture arrives as one more change to the item, after the server has drawn
 * it. The list's settings (pictures on or off) are shared by everyone and follow the same rule as items: the copy
 * with the highest version wins.
 *
 * **Grocery runs.** The run under way arrives with the list and then over the realtime connection, whenever its
 * workflow moves it on. Like items, a run's copy with the highest `sequence` wins, and a run that started later
 * replaces an earlier one. Starting a run and the banner's buttons are requests: their effect arrives as a realtime
 * message a moment later, for everyone at once.
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

  /** The list's settings, shared by everyone (`null` until the first load). */
  const settings = shallowRef<GroceryListSettings | null>(null)

  /**
   * The latest grocery run this phone knows of: under way, or just ended (so the page can say how it ended). `null`
   * when there is none.
   */
  const run = shallowRef<GroceryRun | null>(null)

  /** The lengths of last call a new run can have, in minutes, as the server offers them. */
  const lastCallMinutes = ref<number[]>([])

  /** The run this phone has just started, until the server's workflow announces it. */
  const startingRunId = ref<string | null>(null)

  const removedIds = new Set<string>()
  const heldChanges: GroceryListChange[] = []
  const heldRuns: GroceryRun[] = []
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

  /** Applies settings from the server, unless newer ones have arrived already. Safe to call in any order. */
  function applySettings(next: GroceryListSettings): void {
    if (settings.value === null || next.version > settings.value.version) {
      settings.value = next
    }
  }

  /**
   * Applies a grocery run from the server: a newer copy of the same run (higher sequence), or a run that started no
   * earlier than the one shown. Safe to call with duplicate, stale or out-of-order runs.
   */
  function applyRun(next: GroceryRun): void {
    if (holdCount > 0) {
      heldRuns.push(next)
      return
    }

    const current = run.value
    const isNewer =
      current === null ||
      (next.id === current.id
        ? next.sequence > current.sequence
        : Date.parse(next.startedAt) >= Date.parse(current.startedAt))
    if (isNewer) {
      run.value = next
    }
    if (next.id === startingRunId.value) {
      startingRunId.value = null
    }
  }

  // Whether a new or renamed item will wait for a picture. The server decides; guessing the same way here means the
  // picture's placeholder is there from the start instead of popping in a moment later.
  function picturesWillBeDrawn(): boolean {
    return (
      settings.value?.imageGenerationEnabled === true && settings.value.imageGenerationAvailable
    )
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
      for (const held of heldRuns.splice(0)) {
        applyRun(held)
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
      applySettings(list.settings)
      // The server's word on the run under way: messages about runs that came in meanwhile are applied afterwards.
      run.value = list.run
      lastCallMinutes.value = list.lastCallMinutes
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
      imageUrl: null,
      imagePending: picturesWillBeDrawn(),
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
    // A picture shows what the name says, so the server replaces it when the name really changes (not just its
    // capitalization or spacing).
    const renamed =
      trimmed.name !== undefined &&
      trimmed.name !== '' &&
      pictureNameKey(trimmed.name) !== pictureNameKey(original.name)
    putItem({
      ...original,
      ...(trimmed.name ? { name: trimmed.name } : {}),
      ...optional(trimmed, 'quantity'),
      ...optional(trimmed, 'brand'),
      ...optional(trimmed, 'store'),
      ...optional(trimmed, 'notes'),
      ...(renamed ? { imageUrl: null, imagePending: picturesWillBeDrawn() } : {}),
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
   * Moves an item so that it ends up at `toIndex` in the list, and into another store when `store` is given (it was
   * dropped into that store's section): one of the offered stores, or `''` for none. The server saves both as one
   * change, so the item never shows up moved but in its old store.
   *
   * @throws {@link ApiError} when the server refuses; the item has gone back to where it was.
   */
  async function moveItem(itemId: string, toIndex: number, store?: string): Promise<void> {
    const list = items.value
    const fromIndex = list.findIndex((item) => item.id === itemId)
    const original = list[fromIndex]
    const newStore = store === undefined ? undefined : store.trim() || null
    const storeChanged =
      original !== undefined && newStore !== undefined && newStore !== original.store
    if (
      original === undefined ||
      (toIndex === fromIndex && !storeChanged) ||
      toIndex < 0 ||
      toIndex >= list.length
    ) {
      return
    }

    const others = list.filter((item) => item.id !== itemId)
    const before = others[toIndex - 1] ?? null
    const after = others[toIndex] ?? null
    putItem({
      ...original,
      position:
        toIndex === fromIndex
          ? original.position
          : positionBetween(before?.position ?? null, after?.position ?? null),
      // A new store is a change to the item's details, as it is from the editor.
      ...(storeChanged
        ? { store: newStore, lastModifiedAt: new Date().toISOString(), ...currentEditor() }
        : {}),
    })

    try {
      applyChange(
        await groceryApi.moveItem(itemId, {
          afterItemId: before?.id ?? null,
          ...(storeChanged ? { store: newStore ?? '' } : {}),
        }),
      )
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

  /**
   * Turns AI pictures on or off, for everyone. The switch moves at once; it moves back if the server refuses.
   *
   * @throws {@link ApiError} when the server refuses.
   */
  async function setImageGeneration(enabled: boolean): Promise<void> {
    const original = settings.value
    if (original === null || original.imageGenerationEnabled === enabled) {
      return
    }

    settings.value = { ...original, imageGenerationEnabled: enabled }
    try {
      applySettings(await groceryApi.updateSettings({ imageGenerationEnabled: enabled }))
    } catch (error: unknown) {
      // Unless someone else's change has arrived meanwhile, which is newer than both.
      if (settings.value.version === original.version) {
        settings.value = original
      }
      throw recoverFromFailedWrite(error)
    }
  }

  /**
   * Starts a grocery run with the signed-in user as the shopper. The run appears once the server's workflow has
   * opened last call, a moment later; until then {@link startingRunId} holds its ID.
   *
   * @throws {@link ApiError} when the server refuses (a run is already under way, runs are unavailable).
   */
  async function startRun(minutes: number): Promise<void> {
    try {
      startingRunId.value = await groceryApi.startGroceryRun(minutes)
    } catch (error: unknown) {
      throw recoverFromFailedRunRequest(error)
    }
  }

  /**
   * Sends one of the run's actions: "I'm good" (shown at once), or the shopper's start shopping now, call it off, or
   * done shopping (shown when the server confirms, for everyone at once).
   *
   * @throws {@link ApiError} when the server refuses; "I'm good" is taken back.
   */
  async function sendRunAction(action: GroceryRunAction): Promise<void> {
    const original = run.value
    if (original === null) {
      return
    }

    const me = auth.user?.id
    if (action === 'ready') {
      run.value = {
        ...original,
        participants: original.participants.map((participant) =>
          participant.userId === me ? { ...participant, ready: true } : participant,
        ),
      }
    }

    try {
      await groceryApi.sendGroceryRunAction(original.id, action)
    } catch (error: unknown) {
      if (run.value?.id === original.id && run.value.sequence === original.sequence) {
        run.value = original
      }
      throw recoverFromFailedRunRequest(error)
    }
  }

  // A refused run request usually means this phone's picture of the run is out of date (it has moved on, or ended):
  // reload to catch up.
  function recoverFromFailedRunRequest(error: unknown): ApiError {
    const apiError = toApiError(error)
    if (!endSessionIfUnauthorized(apiError) && apiError.status === 409) {
      void load()
    }
    return apiError
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
      onSettingsChange: applySettings,
      onRunChange: applyRun,
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
        settings.value = null
        run.value = null
        startingRunId.value = null
        removedIds.clear()
        heldChanges.length = 0
        heldRuns.length = 0
        loadState.value = 'idle'
        loadError.value = null
      }
    },
  )

  return {
    items,
    stores,
    groups,
    settings,
    run,
    lastCallMinutes,
    startingRunId,
    loadState,
    loadError,
    connectionState,
    load,
    applyChange,
    applySettings,
    applyRun,
    holdChanges,
    releaseChanges,
    addItem,
    updateItem,
    moveItem,
    removeItem,
    setImageGeneration,
    startRun,
    sendRunAction,
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
