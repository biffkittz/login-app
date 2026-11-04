import axios from 'axios'

import { toApiError } from './api-error'

/**
 * The single Axios instance used to call the backend.
 *
 * Requests are same-origin (`/api/...`): in production the API itself serves this SPA, and in development Vite
 * proxies `/api` to the API. The browser therefore attaches the HttpOnly auth cookie on its own, and the access
 * token never passes through JavaScript where an injected script could steal it.
 */
export const httpClient = axios.create({
  baseURL: '/api',
  timeout: 15_000,
  headers: { Accept: 'application/json' },
})

// Normalize every failure into an ApiError, so callers deal with exactly one error type.
httpClient.interceptors.response.use(
  (response) => response,
  (error: unknown) => Promise.reject(toApiError(error)),
)
