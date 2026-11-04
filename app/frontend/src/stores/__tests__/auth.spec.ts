import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { ApiError } from '@/services/api-error'
import * as authApi from '@/services/auth-api'
import type { AuthenticatedUser } from '@/types/auth'

import { useAuthStore } from '../auth'

vi.mock('@/services/auth-api')

function userExpiringIn(milliseconds: number): AuthenticatedUser {
  return {
    id: 'user-1',
    email: 'ada@example.com',
    displayName: 'Ada',
    sessionExpiresAt: new Date(Date.now() + milliseconds).toISOString(),
  }
}

const unauthorized = () => new ApiError('Unauthorized', { kind: 'http', status: 401 })

describe('auth store', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('signs in and exposes the user', async () => {
    vi.mocked(authApi.login).mockResolvedValue(userExpiringIn(60_000))
    const auth = useAuthStore()

    await auth.login({ email: 'ada@example.com', password: 'correct horse battery' })

    expect(auth.isAuthenticated).toBe(true)
    expect(auth.user?.displayName).toBe('Ada')
  })

  it('propagates login failures for the form to display', async () => {
    vi.mocked(authApi.login).mockRejectedValue(unauthorized())
    const auth = useAuthStore()

    await expect(
      auth.login({ email: 'ada@example.com', password: 'wrong' }),
    ).rejects.toBeInstanceOf(ApiError)
    expect(auth.isAuthenticated).toBe(false)
  })

  it('restores the session only once per page load', async () => {
    vi.mocked(authApi.getCurrentUser).mockResolvedValue(userExpiringIn(60_000))
    const auth = useAuthStore()

    await auth.restoreSession()
    await auth.restoreSession()

    expect(authApi.getCurrentUser).toHaveBeenCalledTimes(1)
    expect(auth.isAuthenticated).toBe(true)
  })

  it('treats a 401 while restoring as simply signed out', async () => {
    vi.mocked(authApi.getCurrentUser).mockRejectedValue(unauthorized())
    const auth = useAuthStore()

    await auth.restoreSession()

    expect(auth.isAuthenticated).toBe(false)
    expect(auth.signOutReason).toBeNull()
  })

  it('signs out locally even when the API call fails', async () => {
    vi.mocked(authApi.login).mockResolvedValue(userExpiringIn(60_000))
    vi.mocked(authApi.logout).mockRejectedValue(new ApiError('Network down', { kind: 'network' }))
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    const auth = useAuthStore()
    await auth.login({ email: 'ada@example.com', password: 'correct horse battery' })

    await auth.logout()

    expect(auth.isAuthenticated).toBe(false)
    expect(auth.signOutReason).toBe('signed-out')
  })

  it('ends the session in the UI when the auth cookie expires', async () => {
    vi.useFakeTimers()
    vi.mocked(authApi.login).mockResolvedValue(userExpiringIn(5_000))
    const auth = useAuthStore()
    await auth.login({ email: 'ada@example.com', password: 'correct horse battery' })

    vi.advanceTimersByTime(4_999)
    expect(auth.isAuthenticated).toBe(true)

    vi.advanceTimersByTime(1)
    expect(auth.isAuthenticated).toBe(false)
    expect(auth.signOutReason).toBe('session-expired')
  })
})
