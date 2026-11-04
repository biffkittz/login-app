import { AxiosError, AxiosHeaders, CanceledError, type AxiosResponse } from 'axios'
import { describe, expect, it } from 'vitest'

import { ApiError, formLevelMessage, toApiError } from '../api-error'

function httpError(status: number, data: unknown): AxiosError {
  const config = { headers: new AxiosHeaders() }
  const response: AxiosResponse = { status, statusText: '', data, headers: {}, config }
  return new AxiosError('Request failed', 'ERR_BAD_REQUEST', config, {}, response)
}

describe('toApiError', () => {
  it('uses the problem details title as the message', () => {
    const error = toApiError(httpError(401, { title: 'Incorrect email or password.', status: 401 }))

    expect(error).toBeInstanceOf(ApiError)
    expect(error.kind).toBe('http')
    expect(error.status).toBe(401)
    expect(error.isUnauthorized).toBe(true)
    expect(error.message).toBe('Incorrect email or password.')
  })

  it('normalizes ASP.NET Core field keys to camelCase form fields', () => {
    const error = toApiError(
      httpError(400, {
        title: 'One or more validation errors occurred.',
        errors: { Email: ['Taken.'], DisplayName: ['Required.'] },
      }),
    )

    expect(error.fieldErrors).toEqual({ email: ['Taken.'], displayName: ['Required.'] })
  })

  it('recognizes network failures and timeouts', () => {
    expect(toApiError(new AxiosError('Network Error', 'ERR_NETWORK')).kind).toBe('network')
    expect(toApiError(new AxiosError('timeout', 'ECONNABORTED')).kind).toBe('timeout')
  })

  it('marks aborted requests as canceled', () => {
    expect(toApiError(new CanceledError()).isCanceled).toBe(true)
  })

  it('falls back to a generic message for unexpected values', () => {
    expect(toApiError(new TypeError('boom')).message).toBe(
      'Something went wrong. Please try again.',
    )
  })
})

describe('formLevelMessage', () => {
  it('returns the overall message when no field-specific errors exist', () => {
    const error = new ApiError('Incorrect email or password.', { kind: 'http', status: 401 })

    expect(formLevelMessage(error)).toBe('Incorrect email or password.')
  })

  it('stays silent when every problem is shown next to a field', () => {
    const error = new ApiError('Validation failed', {
      kind: 'http',
      status: 400,
      fieldErrors: { email: ['Taken.'] },
    })

    expect(formLevelMessage(error)).toBeNull()
  })

  it('surfaces form-wide errors that belong to no field', () => {
    const error = new ApiError('Validation failed', {
      kind: 'http',
      status: 400,
      fieldErrors: { '': ['Registration is closed.'], email: ['Taken.'] },
    })

    expect(formLevelMessage(error)).toBe('Registration is closed.')
  })
})
