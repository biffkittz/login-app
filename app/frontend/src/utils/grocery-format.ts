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

/**
 * Normalizes an item name the way the server decides which items share a picture: spacing and capitalization don't
 * count, so "oat milk" and "Oat  Milk" are one name. Mirrors `GroceryImageNames.KeyFor` in the backend.
 *
 * @param name - An item name.
 */
export function pictureNameKey(name: string): string {
  return name.trim().split(/\s+/).join(' ').toUpperCase()
}

const usd = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' })

/**
 * Formats an amount of U.S. dollars the American way, whatever the browser's locale: the estimates are U.S. prices.
 *
 * @param amount - Dollars, e.g. `4.5`.
 * @returns The amount with its dollar sign and cents, e.g. "$4.50" or "$1,234.00".
 */
export function formatUsd(amount: number): string {
  return usd.format(amount)
}

/**
 * Formats a countdown the way a kitchen timer shows it: "4:05" for minutes and seconds, "1:04:05" from an hour up.
 *
 * @param totalSeconds - Whole seconds left; anything below 0 counts as 0.
 */
export function formatCountdown(totalSeconds: number): string {
  const seconds = Math.max(0, Math.floor(totalSeconds))
  const hours = Math.floor(seconds / 3_600)
  const minutes = Math.floor((seconds % 3_600) / 60)
  const rest = String(seconds % 60).padStart(2, '0')
  return hours > 0 ? `${hours}:${String(minutes).padStart(2, '0')}:${rest}` : `${minutes}:${rest}`
}

/**
 * Formats the time of day of an instant in the reader's locale, e.g. "5:30 PM".
 *
 * @param instant - An ISO 8601 instant.
 * @param locale - The locale to format in; the browser's by default.
 */
export function formatClockTime(instant: string, locale?: string): string {
  const time = Date.parse(instant)
  return Number.isNaN(time)
    ? ''
    : new Intl.DateTimeFormat(locale, { hour: 'numeric', minute: '2-digit' }).format(new Date(time))
}

/**
 * Says how long a last call lasts, for the choices when starting a grocery run: "15 minutes", "1 hour", "2 hours".
 *
 * @param minutes - The length in minutes.
 */
export function formatLastCallLength(minutes: number): string {
  if (minutes % 60 === 0) {
    const hours = minutes / 60
    return hours === 1 ? '1 hour' : `${hours} hours`
  }
  return minutes === 1 ? '1 minute' : `${minutes} minutes`
}
