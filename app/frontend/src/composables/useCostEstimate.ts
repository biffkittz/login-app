import { onScopeDispose, ref, shallowRef, type Ref, type ShallowRef } from 'vue'

import { toApiError } from '@/services/api-error'
import { estimateCost } from '@/services/grocery-api'
import { useAuthStore } from '@/stores/auth'
import type { CostEstimate } from '@/types/grocery'

/** What {@link useCostEstimate} offers. */
export interface CostEstimateState {
  /** The latest estimate, or `null` before the first one (or after a failed one). */
  estimate: ShallowRef<CostEstimate | null>
  /** An estimate is on its way. */
  loading: Ref<boolean>
  /** Why the latest estimate failed, ready to show. */
  error: Ref<string | null>
  /** Asks for a new estimate, abandoning one still on its way. */
  run: () => Promise<void>
  /** Abandons an estimate still on its way. */
  cancel: () => void
}

/**
 * Asks the server for the list's estimated cost, on demand. Each request is a paid call to Gemini that counts against
 * the app's daily allowance, so nothing is fetched until {@link CostEstimateState.run} is called, and nothing is
 * retried behind the user's back.
 *
 * Estimates aren't list state shared with everyone, so they live here rather than in the grocery list store. A request
 * still on its way when the component unmounts is cancelled.
 */
export function useCostEstimate(): CostEstimateState {
  const auth = useAuthStore()
  const estimate = shallowRef<CostEstimate | null>(null)
  const loading = ref(false)
  const error = ref<string | null>(null)
  let pending: AbortController | null = null

  async function run(): Promise<void> {
    pending?.abort()
    const request = new AbortController()
    pending = request
    loading.value = true
    error.value = null
    try {
      estimate.value = await estimateCost({ signal: request.signal })
    } catch (caught: unknown) {
      const apiError = toApiError(caught)
      if (apiError.isCanceled || pending !== request) {
        return
      }
      estimate.value = null
      if (apiError.isUnauthorized) {
        auth.expireSession()
      }
      error.value = apiError.message
    } finally {
      if (pending === request) {
        pending = null
        loading.value = false
      }
    }
  }

  function cancel(): void {
    pending?.abort()
    pending = null
    loading.value = false
  }

  onScopeDispose(cancel)

  return { estimate, loading, error, run, cancel }
}
