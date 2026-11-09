import type { GroceryItem } from '@/types/grocery'

const MINUTE = 60_000
const HOUR = 60 * MINUTE
const DAY = 24 * HOUR

/**
 * Says how long ago something happened, the way people say it: "just now", "5 minutes ago", "yesterday". Anything
 * older than a week gets its date instead ("Nov 2"), plus the year when it isn't this year.
 *
 * @param instant - An ISO 8601 instant.
 * @param now - The current time in milliseconds since the epoch.
 * @param locale - The locale to format in; the browser's by default.
 */
export function formatRelativeTime(instant: string, now: number, locale?: string): string {
  const then = Date.parse(instant)
  if (Number.isNaN(then)) {
    return ''
  }

  const elapsed = Math.max(now - then, 0)
  const relative = new Intl.RelativeTimeFormat(locale, { numeric: 'auto' })
  if (elapsed < 45_000) {
    return 'just now'
  }
  if (elapsed < 45 * MINUTE) {
    return relative.format(-Math.max(Math.round(elapsed / MINUTE), 1), 'minute')
  }
  if (elapsed < 22 * HOUR) {
    return relative.format(-Math.round(elapsed / HOUR), 'hour')
  }
  if (elapsed < 7 * DAY) {
    return relative.format(-Math.max(Math.round(elapsed / DAY), 1), 'day')
  }

  const date = new Date(then)
  const sameYear = date.getFullYear() === new Date(now).getFullYear()
  return new Intl.DateTimeFormat(locale, {
    month: 'short',
    day: 'numeric',
    year: sameYear ? undefined : 'numeric',
  }).format(date)
}

/**
 * Names whoever last changed an item, from the point of view of the person reading: "you", their display name, or
 * "a former member" when that account has been deleted.
 *
 * @param item - The item.
 * @param currentUserId - The signed-in user's ID.
 */
export function describeEditor(
  item: Pick<GroceryItem, 'lastModifiedById' | 'lastModifiedByName'>,
  currentUserId: string | undefined,
): string {
  if (item.lastModifiedById !== null && item.lastModifiedById === currentUserId) {
    return 'you'
  }
  return item.lastModifiedByName ?? 'a former member'
}
