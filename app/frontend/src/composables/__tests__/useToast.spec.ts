import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { useToast } from '../useToast'

describe('useToast', () => {
  beforeEach(() => {
    vi.useFakeTimers()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('shows one toast at a time and hides it after a while', () => {
    const { toast, show } = useToast()

    show('First')
    show('Second', { durationMs: 1_000 })
    expect(toast.value?.message).toBe('Second')

    vi.advanceTimersByTime(1_000)
    expect(toast.value).toBeNull()
  })

  it('waits while the user is interacting with the toast', () => {
    const { toast, show, pause, resume } = useToast()

    show('Removed Milk', { durationMs: 1_000 })
    pause()
    vi.advanceTimersByTime(5_000)
    expect(toast.value?.message).toBe('Removed Milk')

    resume()
    vi.advanceTimersByTime(1_000)
    expect(toast.value).toBeNull()
  })
})
