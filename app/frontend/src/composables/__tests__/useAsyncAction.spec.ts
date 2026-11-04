import { CanceledError } from 'axios'
import { describe, expect, it } from 'vitest'
import { effectScope } from 'vue'

import { ApiError } from '@/services/api-error'

import { useAsyncAction } from '../useAsyncAction'

/** An action whose completion the test controls, and which honors its abort signal like Axios does. */
function controllableAction() {
  let resolve: (value: string) => void = () => {}
  let reject: (reason: unknown) => void = () => {}
  const signals: AbortSignal[] = []

  const action = (signal: AbortSignal) =>
    new Promise<string>((res, rej) => {
      signals.push(signal)
      resolve = res
      reject = rej
      signal.addEventListener('abort', () => rej(new CanceledError()))
    })

  return {
    action,
    signals,
    resolve: (value: string) => resolve(value),
    reject: (reason: unknown) => reject(reason),
  }
}

describe('useAsyncAction', () => {
  it('tracks pending state and returns the value on success', async () => {
    const { action, resolve } = controllableAction()
    const { run, isPending, error } = useAsyncAction(action)

    const result = run()
    expect(isPending.value).toBe(true)

    resolve('done')
    await expect(result).resolves.toEqual({ ok: true, value: 'done' })
    expect(isPending.value).toBe(false)
    expect(error.value).toBeNull()
  })

  it('exposes failures as ApiError', async () => {
    const { action, reject } = controllableAction()
    const { run, error } = useAsyncAction(action)

    const result = run()
    reject(new ApiError('Incorrect email or password.', { kind: 'http', status: 401 }))

    await expect(result).resolves.toMatchObject({ ok: false })
    expect(error.value?.message).toBe('Incorrect email or password.')
  })

  it('aborts a run that is superseded and never reports it as an error', async () => {
    const { action, signals, resolve } = controllableAction()
    const { run, isPending, error } = useAsyncAction(action)

    const first = run()
    const second = run()

    expect(signals[0]?.aborted).toBe(true)
    await expect(first).resolves.toEqual({ ok: false, error: null })
    expect(isPending.value).toBe(true)

    resolve('second')
    await expect(second).resolves.toEqual({ ok: true, value: 'second' })
    expect(error.value).toBeNull()
  })

  it('aborts the in-flight run when its owner is disposed', () => {
    const { action, signals } = controllableAction()
    const scope = effectScope()
    const asyncAction = scope.run(() => useAsyncAction(action))

    void asyncAction?.run()
    scope.stop()

    expect(signals[0]?.aborted).toBe(true)
  })
})
