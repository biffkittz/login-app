import type { RouteLocationNormalized, RouteLocationRaw } from 'vue-router'

/**
 * Decides whether a navigation may proceed, based on the target route's `meta` and the session state.
 * Kept free of Vue and Pinia so it can be unit tested in isolation.
 *
 * @param to - The route being navigated to.
 * @param isAuthenticated - Whether a user is signed in.
 * @returns `true` to continue, or the location to redirect to instead.
 */
export function resolveAuthNavigation(
  to: Pick<RouteLocationNormalized, 'meta' | 'fullPath'>,
  isAuthenticated: boolean,
): true | RouteLocationRaw {
  if (to.meta.requiresAuth && !isAuthenticated) {
    // Remember where the user was heading so signing in can take them there (validated by useRedirectTarget).
    return to.fullPath === '/'
      ? { name: 'login' }
      : { name: 'login', query: { redirect: to.fullPath } }
  }

  if (to.meta.guestOnly && isAuthenticated) {
    return { name: 'home' }
  }

  return true
}
