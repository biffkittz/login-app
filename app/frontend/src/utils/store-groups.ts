import type { GroceryItem } from '@/types/grocery'

/** The heading of the group for items with no store. */
export const NO_STORE_LABEL = 'No store'

/** Items bought at the same store, in list order. */
export interface StoreGroup {
  /** Stable key: the store's name, or `''` for items with no store. */
  key: string
  /** The store, or `null` for the no-store group. */
  store: string | null
  /** The heading to show. */
  label: string
  items: GroceryItem[]
}

/**
 * Groups the list by store.
 *
 * - **No store first.** New items arrive without a store, and right below the "Add an item" bar is where the person
 *   who just added one looks for it.
 * - **Then the offered stores, in their configured order,** so each store's section stays in the same place.
 * - **Then any other store** (saved before the choices existed), alphabetically, so nothing is ever hidden.
 *
 * Empty groups are left out. Store names match regardless of case, as the server does.
 *
 * @param items - Every item, in list order. Each group keeps that order.
 * @param stores - The offered stores, in display order.
 */
export function groupByStore(
  items: readonly GroceryItem[],
  stores: readonly string[],
): StoreGroup[] {
  const canonical = new Map(stores.map((store) => [store.toLowerCase(), store]))
  const byStore = new Map<string, GroceryItem[]>()
  for (const item of items) {
    const store = item.store === null ? '' : (canonical.get(item.store.toLowerCase()) ?? item.store)
    const group = byStore.get(store)
    if (group) {
      group.push(item)
    } else {
      byStore.set(store, [item])
    }
  }

  const others = [...byStore.keys()]
    .filter((store) => store !== '' && !canonical.has(store.toLowerCase()))
    .sort((a, b) => a.localeCompare(b))

  return ['', ...stores, ...others].flatMap((store) => {
    const groupItems = byStore.get(store)
    return groupItems === undefined
      ? []
      : [
          {
            key: store,
            store: store === '' ? null : store,
            label: store === '' ? NO_STORE_LABEL : store,
            items: groupItems,
          },
        ]
  })
}

/**
 * The groups a dragged item could also be dropped into but that aren't shown, because nothing is bought there yet:
 * "No store" and every offered store without items, in the same order as {@link groupByStore}. Each has no items.
 *
 * @param groups - The groups shown, from {@link groupByStore}.
 * @param stores - The offered stores, in display order.
 */
export function emptyStoreGroups(
  groups: readonly StoreGroup[],
  stores: readonly string[],
): StoreGroup[] {
  const shown = new Set(groups.map((group) => group.key))
  return ['', ...stores]
    .filter((store) => !shown.has(store))
    .map((store) => ({
      key: store,
      store: store === '' ? null : store,
      label: store === '' ? NO_STORE_LABEL : store,
      items: [],
    }))
}

/**
 * Whether items can be dropped into a group: "No store" and the offered stores. A store that is no longer offered
 * keeps the items saved with it, but the server refuses to give it to any other.
 */
export function acceptsDrops(group: StoreGroup, stores: readonly string[]): boolean {
  return group.store === null || stores.includes(group.store)
}
