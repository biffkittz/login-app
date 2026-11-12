import { computed, onScopeDispose, watch, type ComputedRef } from 'vue'
import { storeToRefs } from 'pinia'

import { useAuthStore } from '@/stores/auth'
import { useGroceryListStore } from '@/stores/grocery-list'
import type { GroceryRun, GroceryRunParticipant } from '@/types/grocery'

import { useNow } from './useNow'

// The server goes on believing a last call this long past its deadline, should its workflow not have moved it on
// (`GroceryRunQueries.DeadlineGrace`); the list stays locked as long.
const DEADLINE_GRACE_MS = 60_000

// Normally the shopping begins within a second of last call's deadline. If no word has come this long after it, the
// message was probably missed (a reconnect, a deployment): reload.
const CATCH_UP_AFTER_MS = 5_000

// A run this phone started should appear within a second or two. If it hasn't after this long, reload: the run is in
// the list's state either way.
const START_TIMEOUT_MS = 15_000

/** What the grocery list view shows about grocery runs. */
export interface GroceryRunView {
  /** The run under way (last call or shopping), or `null`. */
  run: ComputedRef<GroceryRun | null>
  /** Last call is on: items may be added and changed, but not removed or moved. */
  listLocked: ComputedRef<boolean>
  /** The signed-in user is the run's shopper. */
  isShopper: ComputedRef<boolean>
  /** The signed-in user's place among the people asked, or `null` when they weren't asked. */
  myParticipation: ComputedRef<GroceryRunParticipant | null>
  /** Whole seconds until last call ends (0 once it should have). */
  secondsLeft: ComputedRef<number>
}

/**
 * The grocery run as the list's view needs it, kept current by a once-a-second clock while a run is on screen.
 *
 * Also catches up when a run's next step seems to have been missed: when last call is past its deadline with no word
 * of the shopping, and when a run this phone started hasn't appeared, the list is reloaded.
 */
export function useGroceryRun(): GroceryRunView {
  const auth = useAuthStore()
  const groceryList = useGroceryListStore()
  const { run: latestRun, startingRunId } = storeToRefs(groceryList)
  const now = useNow(1_000)

  const run = computed(() => {
    const current = latestRun.value
    return current !== null && (current.status === 'lastCall' || current.status === 'shopping')
      ? current
      : null
  })

  const lastCallEndsAt = computed(() =>
    run.value === null ? 0 : Date.parse(run.value.lastCallEndsAt),
  )

  const listLocked = computed(
    () => run.value?.status === 'lastCall' && now.value < lastCallEndsAt.value + DEADLINE_GRACE_MS,
  )

  const isShopper = computed(
    () =>
      run.value !== null && run.value.shopperId !== null && run.value.shopperId === auth.user?.id,
  )

  const myParticipation = computed(
    () =>
      run.value?.participants.find((participant) => participant.userId === auth.user?.id) ?? null,
  )

  const secondsLeft = computed(() =>
    Math.max(0, Math.ceil((lastCallEndsAt.value - now.value) / 1_000)),
  )

  let caughtUpFor: string | null = null
  watch(now, (time) => {
    const current = run.value
    if (
      current?.status === 'lastCall' &&
      time > lastCallEndsAt.value + CATCH_UP_AFTER_MS &&
      caughtUpFor !== `${current.id}:${current.sequence}`
    ) {
      caughtUpFor = `${current.id}:${current.sequence}`
      void groceryList.load()
    }
  })

  let startTimer: ReturnType<typeof setTimeout> | undefined
  watch(startingRunId, (runId) => {
    clearTimeout(startTimer)
    if (runId !== null) {
      startTimer = setTimeout(() => {
        if (groceryList.startingRunId === runId) {
          groceryList.startingRunId = null
          void groceryList.load()
        }
      }, START_TIMEOUT_MS)
    }
  })
  onScopeDispose(() => clearTimeout(startTimer))

  return { run, listLocked, isShopper, myParticipation, secondsLeft }
}
