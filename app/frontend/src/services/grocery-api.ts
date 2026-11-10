import type {
  CostEstimate,
  GroceryItemChanges,
  GroceryItemMove,
  GroceryList,
  GroceryListChange,
  GroceryListSettings,
  GroceryListSettingsChanges,
  NewGroceryItem,
} from '@/types/grocery'

import type { RequestOptions } from './auth-api'
import { httpClient } from './http-client'

const ITEMS = '/grocery-list/items'

// The server gives Gemini a minute to price the list; this leaves room for the trip there and back.
const COST_ESTIMATE_TIMEOUT_MS = 75_000

/**
 * Returns the whole list, in order.
 *
 * @throws {@link ApiError} 401 when not signed in.
 */
export async function getList(options: RequestOptions = {}): Promise<GroceryList> {
  const { data } = await httpClient.get<GroceryList>('/grocery-list', options)
  return data
}

/**
 * Adds an item at the top of the list, or after `item.afterItemId`.
 *
 * @throws {@link ApiError} 400 with field errors, 409 when the list is full or the ID is taken.
 */
export async function createItem(
  item: NewGroceryItem,
  options: RequestOptions = {},
): Promise<GroceryListChange> {
  const { data } = await httpClient.post<GroceryListChange>(ITEMS, item, options)
  return data
}

/**
 * Changes the given fields of an item; fields left out keep their value and `''` clears one.
 *
 * @throws {@link ApiError} 400 with field errors, 404 when the item no longer exists.
 */
export async function updateItem(
  itemId: string,
  changes: GroceryItemChanges,
  options: RequestOptions = {},
): Promise<GroceryListChange> {
  const { data } = await httpClient.patch<GroceryListChange>(
    `${ITEMS}/${encodeURIComponent(itemId)}`,
    changes,
    options,
  )
  return data
}

/**
 * Moves an item directly after another one, or to the top when `move.afterItemId` is `null`, and changes its store
 * when `move.store` is given.
 *
 * @throws {@link ApiError} 400 on `Store` for a store that isn't offered, 404 when the item no longer exists, 409 when
 * `afterItemId` no longer does.
 */
export async function moveItem(
  itemId: string,
  move: GroceryItemMove,
  options: RequestOptions = {},
): Promise<GroceryListChange> {
  const { data } = await httpClient.post<GroceryListChange>(
    `${ITEMS}/${encodeURIComponent(itemId)}/move`,
    move,
    options,
  )
  return data
}

/**
 * Removes an item.
 *
 * @throws {@link ApiError} 404 when the item was already removed.
 */
export async function deleteItem(
  itemId: string,
  options: RequestOptions = {},
): Promise<GroceryListChange> {
  const { data } = await httpClient.delete<GroceryListChange>(
    `${ITEMS}/${encodeURIComponent(itemId)}`,
    options,
  )
  return data
}

/**
 * Changes the list's settings, for everyone. Settings left out keep their value.
 *
 * @throws {@link ApiError} 409 when someone changed them at the same moment.
 */
export async function updateSettings(
  changes: GroceryListSettingsChanges,
  options: RequestOptions = {},
): Promise<GroceryListSettings> {
  const { data } = await httpClient.patch<GroceryListSettings>(
    '/grocery-list/settings',
    changes,
    options,
  )
  return data
}

/**
 * Asks Google's Gemini, through the server, what the whole list costs at average U.S. prices. Each call is a paid
 * request that counts against the app's daily allowance, so call it only when the user asks.
 *
 * @throws {@link ApiError} 409 when the list is empty, 429 when today's estimates are used up, 502 or 503 when the AI
 * couldn't answer, 503 when the server has no Gemini API key.
 */
export async function estimateCost(options: RequestOptions = {}): Promise<CostEstimate> {
  const { data } = await httpClient.post<CostEstimate>('/grocery-list/cost-estimate', null, {
    ...options,
    timeout: COST_ESTIMATE_TIMEOUT_MS,
  })
  return data
}
