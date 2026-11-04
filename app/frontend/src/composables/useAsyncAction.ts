import { computed, getCurrentScope, onScopeDispose, ref, shallowRef, type ComputedRef } from 'vue'

import { toApiError, type ApiError } from '@/services/api-error'

/** The outcome of one {@link AsyncAction.run}. `error` is `null` when the run was canceled. */
export type AsyncActionResult<TResult> =
  { ok: true; value: TResult } | { ok: false; error: ApiError | null }

/** Reactive state and controls returned by {@link useAsyncAction}. */
export interface AsyncAction<TArgs extends unknown[], TResult> {
  /** Starts the action, aborting any run still in flight. */
  run: (...args: TArgs) => Promise<AsyncActionResult<TResult>>
  /** True while a run is in flight. */
  isPending: ComputedRef<boolean>
  /** The most recent failure, cleared when a new run starts. Cancellations never appear here. */
  error: ComputedRef<ApiError | null>
}

/**
 * Wraps an async operation (usually an API call) with explicit pending and error state, so components never
 * juggle `try`/`catch` and loading flags by hand.
 *
 * - Starting a new run aborts the previous one, so a stale response can never overwrite a newer one.
 * - A run still in flight is aborted when the owning component unmounts.
 *
 * @param action - The operation; it receives an `AbortSignal` to pass on to the HTTP client.
 * @example
 * const signIn = useAsyncAction((signal, credentials: LoginCredentials) => auth.login(credentials, { signal }))
 * const result = await signIn.run(credentials)
 */
export function useAsyncAction<TArgs extends unknown[], TResult>(
  action: (signal: AbortSignal, ...args: TArgs) => Promise<TResult>,
): AsyncAction<TArgs, TResult> {
  const isPending = ref(false)
  const error = shallowRef<ApiError | null>(null)
  let inFlight: AbortController | null = null

  async function run(...args: TArgs): Promise<AsyncActionResult<TResult>> {
    inFlight?.abort()
    const controller = new AbortController()
    inFlight = controller
    isPending.value = true
    error.value = null

    try {
      return { ok: true, value: await action(controller.signal, ...args) }
    } catch (caught: unknown) {
      const apiError = toApiError(caught)
      const isCurrent = inFlight === controller
      if (apiError.isCanceled || !isCurrent) {
        return { ok: false, error: null }
      }
      error.value = apiError
      return { ok: false, error: apiError }
    } finally {
      // A superseded run must not clear the pending flag of the run that replaced it.
      if (inFlight === controller) {
        inFlight = null
        isPending.value = false
      }
    }
  }

  // Abort with the owning component or effect scope; also usable outside components (e.g. in tests).
  if (getCurrentScope()) {
    onScopeDispose(() => inFlight?.abort())
  }

  return {
    run,
    isPending: computed(() => isPending.value),
    error: computed(() => error.value),
  }
}
