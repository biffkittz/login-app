import { computed, type ComputedRef } from 'vue'
import { useRoute, type RouteLocationRaw } from 'vue-router'

/**
 * True when `path` is a path within this app.
 *
 * Must start with exactly one "/": that rejects absolute URLs ("https://evil.example") and protocol-relative ones
 * ("//evil.example"). Backslashes are rejected because some browsers treat them as "/".
 */
export function isSafeInternalPath(path: string): boolean {
  return path.startsWith('/') && !path.startsWith('//') && !path.includes('\\')
}

/**
 * Where to go after signing in: the protected page the user originally asked for (`?redirect=/...`), or home.
 *
 * Only internal paths are honored. Following any URL would turn the login page into an open redirect that
 * phishing links could abuse (`/login?redirect=https://evil.example`).
 */
export function useRedirectTarget(): ComputedRef<RouteLocationRaw> {
  const route = useRoute()
  return computed(() => {
    const redirect = route.query.redirect
    return typeof redirect === 'string' && isSafeInternalPath(redirect)
      ? redirect
      : { name: 'home' }
  })
}
