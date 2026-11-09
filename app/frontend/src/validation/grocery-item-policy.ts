/**
 * Grocery item limits mirrored from the API (backend/Features/GroceryList/GroceryItem.cs) so the forms can stop input
 * before the server would reject it. The server stays the authority: keep these values in sync with it.
 */
export const ITEM_NAME_MAX_LENGTH = 100
export const ITEM_QUANTITY_MAX_LENGTH = 50
export const ITEM_BRAND_MAX_LENGTH = 100
export const ITEM_STORE_MAX_LENGTH = 100
export const ITEM_NOTES_MAX_LENGTH = 1000
