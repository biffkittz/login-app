/** A grocery item, as the API and the realtime hub send it (`GroceryItemResponse` in the backend). */
export interface GroceryItem {
  id: string
  name: string
  quantity: string | null
  brand: string | null
  store: string | null
  notes: string | null
  /** The sort key: the list is ordered by ascending position. */
  position: number
  /** Increases with every change on the server. The copy with the highest version is the newest. */
  version: number
  /** ISO 8601 instant the item was added. */
  createdAt: string
  /** ISO 8601 instant the item's details last changed (moving it doesn't count). */
  lastModifiedAt: string
  /** Who added the item or last changed its details; `null` once that account no longer exists. */
  lastModifiedById: string | null
  lastModifiedByName: string | null
  /**
   * Where the item's AI-drawn picture is (a few kilobytes of WebP), or `null` when it has none. A URL always shows the
   * same picture, so the browser keeps it once downloaded.
   */
  imageUrl: string | null
  /** A picture is being drawn for the item; it arrives as a later change. */
  imagePending: boolean
}

/** The list's in-app settings, shared by everyone (`GroceryListSettingsResponse` in the backend). */
export interface GroceryListSettings {
  /** Increases with every change on the server. The settings with the highest version are the newest. */
  version: number
  /** New and renamed items get an AI-drawn picture. */
  imageGenerationEnabled: boolean
  /** The server can draw pictures at all: it has a Gemini API key. */
  imageGenerationAvailable: boolean
}

/** The body of `PATCH /api/grocery-list/settings`: only the settings to change. */
export type GroceryListSettingsChanges = Partial<
  Pick<GroceryListSettings, 'imageGenerationEnabled'>
>

/** The body of `GET /api/grocery-list`. */
export interface GroceryList {
  items: GroceryItem[]
  /** The stores an item's `store` may name, in the order the list is grouped by. Configured on the server. */
  stores: string[]
  settings: GroceryListSettings
}

/**
 * What one write did to the list. The API answers every write with it, and the realtime hub sends the same message to
 * every open list, so the app applies its own changes and everybody else's the same way.
 */
export interface GroceryListChange {
  savedItems: GroceryItem[]
  deletedItemIds: string[]
}

/** The editable details of an item, as form fields hold them: `''` means "not set". */
export interface GroceryItemDetails {
  name: string
  quantity: string
  brand: string
  store: string
  notes: string
}

/** The body of `PATCH /api/grocery-list/items/{id}`: only the fields that changed. `''` clears a field. */
export type GroceryItemChanges = Partial<GroceryItemDetails>

/** The body of `POST /api/grocery-list/items`. */
export interface NewGroceryItem extends Partial<GroceryItemDetails> {
  /** Chosen by the app, so the item can be shown before the server answers. */
  id: string
  name: string
  /** The item it should follow; `null` puts it at the top. */
  afterItemId?: string | null
}

/** The state of the realtime connection that keeps the list up to date. */
export type ConnectionState = 'connecting' | 'connected' | 'reconnecting' | 'disconnected'

/** A removed item and where it was, so "Undo" can put it back. */
export interface RemovedItem {
  item: GroceryItem
  /** The item that preceded it, or `null` if it was at the top. */
  afterItemId: string | null
}
