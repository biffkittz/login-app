import { computed, ref } from 'vue'
import { defineStore } from 'pinia'

import { ApiError } from '@/services/api-error'
import * as authApi from '@/services/auth-api'
import type { RequestOptions } from '@/services/auth-api'
import type {
  AuthenticatedUser,
  LoginCredentials,
  RegistrationDetails,
  SignOutReason,
} from '@/types/auth'

// setTimeout stores its delay as a signed 32-bit integer; longer delays fire immediately.
const MAX_TIMER_DELAY_MS = 2_147_483_647

/**
 * Global authentication state (Pinia setup store).
 *
 * The real session is the HttpOnly auth cookie, which JavaScript cannot read. This store only mirrors what the
 * API reports about it, and is deliberately **not** persisted to localStorage: after a reload,
 * {@link restoreSession} asks the API again, so the UI can never claim a session the server no longer honors.
 */
export const useAuthStore = defineStore('auth', () => {
  const user = ref<AuthenticatedUser | null>(null)
  const signOutReason = ref<SignOutReason | null>(null)
  const isAuthenticated = computed(() => user.value !== null)

  let sessionRestore: Promise<void> | null = null
  let expiryTimer: ReturnType<typeof setTimeout> | undefined

  function startSession(authenticatedUser: AuthenticatedUser): void {
    user.value = authenticatedUser
    signOutReason.value = null
    scheduleExpiry(authenticatedUser.sessionExpiresAt)
  }

  function endSession(reason: SignOutReason | null): void {
    clearTimeout(expiryTimer)
    user.value = null
    signOutReason.value = reason
  }

  // The auth cookie expires together with its token. Mirror that moment so an idle tab returns to the login
  // page instead of showing a "signed in" page whose next request would fail.
  function scheduleExpiry(expiresAt: string): void {
    clearTimeout(expiryTimer)
    const remaining = Date.parse(expiresAt) - Date.now()
    if (Number.isNaN(remaining)) {
      return
    }
    expiryTimer = setTimeout(
      () => endSession('session-expired'),
      Math.min(Math.max(remaining, 0), MAX_TIMER_DELAY_MS),
    )
  }

  /**
   * Asks the API whether the auth cookie still identifies a user. Runs once per page load; later calls return
   * the same promise, so the router guard can await it on every navigation for free.
   */
  function restoreSession(): Promise<void> {
    sessionRestore ??= authApi.getCurrentUser().then(startSession, (error: unknown) => {
      // 401 just means "not signed in". Anything else (API unreachable) is logged and the user starts signed
      // out; the login form reports the problem if it persists.
      if (!(error instanceof ApiError && error.isUnauthorized)) {
        console.error('[auth] Could not restore the session.', error)
      }
      endSession(null)
    })
    return sessionRestore
  }

  /** Signs in. Throws an {@link ApiError} that the login form presents. */
  async function login(credentials: LoginCredentials, options?: RequestOptions): Promise<void> {
    startSession(await authApi.login(credentials, options))
  }

  /** Creates an account and signs it in. Throws an {@link ApiError} with field errors on failure. */
  async function register(details: RegistrationDetails, options?: RequestOptions): Promise<void> {
    startSession(await authApi.register(details, options))
  }

  /** Signs out. Never throws: the UI always ends up signed out, whatever the network does. */
  async function logout(): Promise<void> {
    try {
      await authApi.logout()
    } catch (error: unknown) {
      // 401: the session had already ended. Otherwise the API could not delete the cookie, which still expires
      // on its own schedule; either way the user asked to leave, so the UI signs out regardless.
      if (!(error instanceof ApiError && error.isUnauthorized)) {
        console.warn('[auth] Sign-out request failed; signing out locally.', error)
      }
    } finally {
      endSession('signed-out')
    }
  }

  /** Forgets why the user was signed out, once the login page has told them. */
  function clearSignOutReason(): void {
    signOutReason.value = null
  }

  return {
    user,
    signOutReason,
    isAuthenticated,
    restoreSession,
    login,
    register,
    logout,
    clearSignOutReason,
  }
})
