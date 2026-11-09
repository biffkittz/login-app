import type { GroceryItem } from '@/types/grocery'

/**
 * Builds a grocery item for tests, with sensible defaults for everything not given.
 *
 * @param overrides - The fields that matter to the test.
 */
export function makeItem(overrides: Partial<GroceryItem> & Pick<GroceryItem, 'id'>): GroceryItem {
  return {
    name: `Item ${overrides.id}`,
    quantity: null,
    brand: null,
    store: null,
    notes: null,
    position: 0,
    version: 1,
    createdAt: '2026-11-09T10:00:00.000Z',
    lastModifiedAt: '2026-11-09T10:00:00.000Z',
    lastModifiedById: 'user-1',
    lastModifiedByName: 'Ada',
    imageUrl: null,
    imagePending: false,
    ...overrides,
  }
}
