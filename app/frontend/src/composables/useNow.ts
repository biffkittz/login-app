import { getCurrentScope, onScopeDispose, readonly, ref, type Ref } from 'vue'

/**
 * The current time, updated every `intervalMs`, for labels such as "5 minutes ago" that must age while on screen.
 *
 * @param intervalMs - How often to update; a minute is plenty for minute-level labels.
 * @returns The time in milliseconds since the epoch.
 */
export function useNow(intervalMs = 30_000): Readonly<Ref<number>> {
  const now = ref(Date.now())
  const timer = setInterval(() => {
    now.value = Date.now()
  }, intervalMs)

  if (getCurrentScope()) {
    onScopeDispose(() => clearInterval(timer))
  }
  return readonly(now)
}
