import { describe, expect, it } from 'vitest'

import { resolveAuthNavigation } from '../guards'

describe('resolveAuthNavigation', () => {
  it('sends signed-out users to the login page, remembering where they were going', () => {
    const result = resolveAuthNavigation(
      { meta: { requiresAuth: true }, fullPath: '/settings' },
      false,
    )

    expect(result).toEqual({ name: 'login', query: { redirect: '/settings' } })
  })

  it('omits the redirect when the destination is the home page anyway', () => {
    const result = resolveAuthNavigation({ meta: { requiresAuth: true }, fullPath: '/' }, false)

    expect(result).toEqual({ name: 'login' })
  })

  it('sends signed-in users away from guest-only pages', () => {
    const result = resolveAuthNavigation({ meta: { guestOnly: true }, fullPath: '/login' }, true)

    expect(result).toEqual({ name: 'home' })
  })

  it.each([
    [{ requiresAuth: true }, true],
    [{ guestOnly: true }, false],
    [{}, false],
    [{}, true],
  ])('allows navigation with meta %o when signed in is %s', (meta, isAuthenticated) => {
    expect(resolveAuthNavigation({ meta, fullPath: '/x' }, isAuthenticated)).toBe(true)
  })
})
