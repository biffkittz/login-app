import type { GroceryItem, GroceryItemChanges, GroceryItemDetails } from '@/types/grocery'

/** The fields of the item form, in display order. */
export const ITEM_FIELDS = [
  'name',
  'quantity',
  'brand',
  'store',
  'notes',
] as const satisfies ReadonlyArray<keyof GroceryItemDetails>

/**
 * An item's details as form values (`null` becomes `''`).
 *
 * @param item - The item to edit.
 */
export function toItemDetails(item: GroceryItem): GroceryItemDetails {
  return {
    name: item.name,
    quantity: item.quantity ?? '',
    brand: item.brand ?? '',
    store: item.store ?? '',
    notes: item.notes ?? '',
  }
}

/**
 * The fields the user actually changed, trimmed, for `PATCH`. Sending only those means someone else's simultaneous
 * change to another field isn't overwritten with the stale value the form was opened with.
 *
 * @param original - The values the form was opened with.
 * @param edited - The values in the form now.
 * @returns The changed fields; empty when nothing changed. `''` means "clear this field".
 */
export function changedDetails(
  original: GroceryItemDetails,
  edited: GroceryItemDetails,
): GroceryItemChanges {
  const changes: GroceryItemChanges = {}
  for (const field of ITEM_FIELDS) {
    const value = edited[field].trim()
    if (value !== original[field].trim()) {
      changes[field] = value
    }
  }
  return changes
}
