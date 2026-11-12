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
  /** The server can estimate the list's cost: it has a Gemini API key, and estimates aren't turned off. */
  costEstimateAvailable: boolean
  /** The server can hold grocery runs: it is connected to Temporal. */
  groceryRunsAvailable: boolean
}

/** The body of `PATCH /api/grocery-list/settings`: only the settings to change. */
export type GroceryListSettingsChanges = Partial<
  Pick<GroceryListSettings, 'imageGenerationEnabled'>
>

/** One item of a {@link CostEstimate} (`CostEstimateLineResponse` in the backend). */
export interface CostEstimateLine {
  itemId: string
  /** The item's name and quantity, from the list. */
  name: string
  quantity: string | null
  /** The average U.S. price of that quantity in dollars, or `null` when the AI couldn't price it. */
  estimatedUsd: number | null
  /** What the AI priced, in a few words ("1 gallon, store brand"). Plain text written by the AI. */
  basis: string | null
}

/**
 * What the whole list would cost at average U.S. prices, as Google's Gemini estimates them: the body of
 * `POST /api/grocery-list/cost-estimate` (`CostEstimateResponse` in the backend).
 */
export interface CostEstimate {
  /** The sum of the items' estimates, in dollars, added up by the server. */
  totalUsd: number
  /** How many items have an estimate. */
  estimatedItemCount: number
  /** Every item on the list, in list order. */
  items: CostEstimateLine[]
  /** The AI's one-sentence remark on the estimate. Plain text written by the AI. */
  note: string | null
  /** ISO 8601 instant the estimate was made. */
  estimatedAt: string
}

/**
 * Where a grocery run is (`GroceryRunStatus` in the backend). Last call comes first, then shopping; a run ends as
 * finished (the shopper said so), expired (nobody did, within the time limit) or cancelled.
 */
export type GroceryRunStatus = 'lastCall' | 'shopping' | 'finished' | 'expired' | 'cancelled'

/** Why last call ended (`LastCallOutcome` in the backend). */
export type LastCallOutcome = 'everyoneReady' | 'timeUp' | 'shopperWentEarly'

/** Someone asked during a run's last call: everyone else who had the list open when it started. */
export interface GroceryRunParticipant {
  userId: string
  displayName: string
  /** They said "I'm good". */
  ready: boolean
}

/**
 * A grocery run (`GroceryRunResponse` in the backend): "speak now or forever hold your peace". During last call
 * everyone can add items but nobody can remove or move them; it ends when everyone asked is ready, when the time is up,
 * or when the shopper goes early. Then the list is final and open again while the shopper shops.
 */
export interface GroceryRun {
  id: string
  /** Increases with every change to the run. The copy with the highest sequence is the newest. */
  sequence: number
  status: GroceryRunStatus
  /** Who is going shopping; `null` once that account no longer exists. */
  shopperId: string | null
  shopperName: string | null
  /** ISO 8601 instant the run (and last call) started. */
  startedAt: string
  /** ISO 8601 instant last call ends, unless everyone is ready sooner. */
  lastCallEndsAt: string
  /** ISO 8601 instant the shopping began, once it has. */
  shoppingStartedAt: string | null
  /** ISO 8601 instant the run ended, once it has. */
  endedAt: string | null
  lastCallOutcome: LastCallOutcome | null
  participants: GroceryRunParticipant[]
}

/** What a run's banner can ask of the server (`POST /api/grocery-list/run/{id}/{action}`). */
export type GroceryRunAction = 'ready' | 'start-shopping' | 'cancel' | 'finish'

/** The body of `GET /api/grocery-list`. */
export interface GroceryList {
  items: GroceryItem[]
  /** The stores an item's `store` may name, in the order the list is grouped by. Configured on the server. */
  stores: string[]
  settings: GroceryListSettings
  /** The grocery run under way, or `null`. */
  run: GroceryRun | null
  /** The lengths of last call a new run can have, in minutes, in the order to offer them. */
  lastCallMinutes: number[]
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

/** The body of `POST /api/grocery-list/items/{id}/move`. */
export interface GroceryItemMove {
  /** The item it should follow; `null` puts it at the top. */
  afterItemId: string | null
  /**
   * Its new store, when it was dropped into another store's section: one of the offered stores, or `''` for none.
   * Left out, the item keeps its store.
   */
  store?: string
}

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
