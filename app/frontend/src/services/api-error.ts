import axios from 'axios'

/** Validation messages keyed by camelCase form field (`email`, `password`, ...). `''` holds form-wide messages. */
export type FieldErrors = Record<string, string[]>

/** What went wrong at the transport level. */
export type ApiErrorKind = 'http' | 'network' | 'timeout' | 'canceled'

interface ApiErrorOptions {
  kind: ApiErrorKind
  status?: number | null
  fieldErrors?: FieldErrors
  cause?: unknown
}

/** The subset of an RFC 9457 problem details body that the UI uses. */
interface ProblemDetails {
  title?: string
  detail?: string
  errors?: Record<string, string[]>
}

const GENERIC_MESSAGE = 'Something went wrong. Please try again.'
const NETWORK_MESSAGE = "We couldn't reach the server. Check your connection and try again."
const TIMEOUT_MESSAGE = 'The server took too long to respond. Please try again.'
const CANCELED_MESSAGE = 'The request was canceled.'

/**
 * A failed API call. Axios errors and RFC 9457 problem details are normalized into this one shape so that stores
 * and components never need to know about Axios or HTTP response formats.
 */
export class ApiError extends Error {
  readonly kind: ApiErrorKind
  readonly status: number | null
  readonly fieldErrors: FieldErrors

  constructor(message: string, options: ApiErrorOptions) {
    super(message, { cause: options.cause })
    this.name = 'ApiError'
    this.kind = options.kind
    this.status = options.status ?? null
    this.fieldErrors = options.fieldErrors ?? {}
  }

  /** True when the server rejected the request because there is no valid session. */
  get isUnauthorized(): boolean {
    return this.status === 401
  }

  /** True when the request was aborted on purpose (superseded or its component unmounted). */
  get isCanceled(): boolean {
    return this.kind === 'canceled'
  }
}

/**
 * Converts anything thrown by an API call into an {@link ApiError}.
 *
 * @param error - The value caught from Axios or other code.
 * @returns An `ApiError` carrying a user-presentable message.
 */
export function toApiError(error: unknown): ApiError {
  if (error instanceof ApiError) {
    return error
  }

  if (axios.isCancel(error)) {
    return new ApiError(CANCELED_MESSAGE, { kind: 'canceled', cause: error })
  }

  if (!axios.isAxiosError(error)) {
    return new ApiError(GENERIC_MESSAGE, { kind: 'http', cause: error })
  }

  if (error.code === 'ECONNABORTED' || error.code === 'ETIMEDOUT') {
    return new ApiError(TIMEOUT_MESSAGE, { kind: 'timeout', cause: error })
  }

  if (!error.response) {
    return new ApiError(NETWORK_MESSAGE, { kind: 'network', cause: error })
  }

  const problem = isProblemDetails(error.response.data) ? error.response.data : {}
  return new ApiError(problem.title ?? problem.detail ?? GENERIC_MESSAGE, {
    kind: 'http',
    status: error.response.status,
    fieldErrors: normalizeFieldErrors(problem.errors),
    cause: error,
  })
}

/**
 * Picks the message to show above a form. Errors that belong to a specific field are shown next to that field,
 * so the summary only repeats what has nowhere better to go.
 *
 * @param error - The failure to describe.
 * @returns The form-level message, or `null` when every problem is already shown beside a field.
 */
export function formLevelMessage(error: ApiError): string | null {
  const general = error.fieldErrors['']?.[0]
  const hasFieldSpecificErrors = Object.keys(error.fieldErrors).some((field) => field !== '')
  return general ?? (hasFieldSpecificErrors ? null : error.message)
}

function isProblemDetails(data: unknown): data is ProblemDetails {
  return typeof data === 'object' && data !== null
}

// ASP.NET Core keys validation errors by C# property name ("Email"), while the forms use camelCase field names
// ("email"). Normalizing here keeps that server-side detail out of the UI.
function normalizeFieldErrors(errors: Record<string, string[]> | undefined): FieldErrors {
  const normalized: FieldErrors = {}
  for (const [key, messages] of Object.entries(errors ?? {})) {
    const field = key.charAt(0).toLowerCase() + key.slice(1)
    normalized[field] = [...(normalized[field] ?? []), ...messages]
  }
  return normalized
}
