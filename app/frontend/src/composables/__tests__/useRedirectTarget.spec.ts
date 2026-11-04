import { describe, expect, it } from 'vitest'

import { isSafeInternalPath } from '../useRedirectTarget'

describe('isSafeInternalPath', () => {
  it.each(['/', '/profile', '/a/b?c=d#e'])('accepts the internal path %s', (path) => {
    expect(isSafeInternalPath(path)).toBe(true)
  })

  it.each([
    'https://evil.example',
    '//evil.example',
    '/\\evil.example',
    'javascript:alert(1)',
    'profile',
    '',
  ])('rejects %s, which could redirect off-site', (path) => {
    expect(isSafeInternalPath(path)).toBe(false)
  })
})
