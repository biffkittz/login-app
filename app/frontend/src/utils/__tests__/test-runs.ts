import type { GroceryRun, GroceryRunParticipant } from '@/types/grocery'

/** A run in last call, started a minute ago and ending in 30 minutes, with overrides. */
export function makeRun(overrides: Partial<GroceryRun> = {}): GroceryRun {
  const now = Date.now()
  return {
    id: 'run-1',
    sequence: 1,
    status: 'lastCall',
    shopperId: 'ada',
    shopperName: 'Ada',
    startedAt: new Date(now - 60_000).toISOString(),
    lastCallEndsAt: new Date(now + 30 * 60_000).toISOString(),
    shoppingStartedAt: null,
    endedAt: null,
    lastCallOutcome: null,
    participants: [],
    ...overrides,
  }
}

/** Someone asked during last call. */
export function participant(
  userId: string,
  displayName: string,
  ready = false,
): GroceryRunParticipant {
  return { userId, displayName, ready }
}
