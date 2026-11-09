import { onBeforeUnmount, onMounted } from 'vue'

import { useGroceryListStore } from '@/stores/grocery-list'

import { useEventListener } from './useEventListener'

// Phones suspend pages in the background. After this long away, the realtime connection may be dead without the page
// knowing yet (it would only notice after up to 30 seconds), so reconnect at once instead of trusting it.
const BACKGROUND_RECONNECT_AFTER_MS = 15_000

/**
 * Keeps the grocery list live while the calling component (the list's view) is mounted: connects on mount,
 * disconnects on unmount, and reconnects when the page comes back from the background or the device comes back
 * online, so a phone pulled out of a pocket shows the list as it is now.
 */
export function useGroceryListLiveUpdates(): void {
  const groceryList = useGroceryListStore()
  let hiddenSince: number | null = null

  onMounted(() => groceryList.connect())
  onBeforeUnmount(() => void groceryList.disconnect())

  useEventListener(document, 'visibilitychange', () => {
    if (document.visibilityState === 'hidden') {
      hiddenSince = Date.now()
      return
    }

    const awayFor = hiddenSince === null ? 0 : Date.now() - hiddenSince
    hiddenSince = null
    if (awayFor > BACKGROUND_RECONNECT_AFTER_MS || groceryList.connectionState !== 'connected') {
      void groceryList.reconnect()
    }
  })

  useEventListener(window, 'online', () => {
    if (groceryList.connectionState !== 'connected') {
      void groceryList.reconnect()
    }
  })
}
