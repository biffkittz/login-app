import type { AuthenticatedUser, LoginCredentials, RegistrationDetails } from '@/types/auth'

import { httpClient } from './http-client'

/** Options shared by every API call. */
export interface RequestOptions {
  /** Aborts the request, e.g. when the component that started it unmounts. */
  signal?: AbortSignal
}

/**
 * Signs in. On success the API sets the HttpOnly auth cookie.
 *
 * @throws {@link ApiError} 401 for wrong credentials, 429 when locked out or rate limited.
 */
export async function login(
  credentials: LoginCredentials,
  options: RequestOptions = {},
): Promise<AuthenticatedUser> {
  const { data } = await httpClient.post<AuthenticatedUser>('/auth/login', credentials, options)
  return data
}

/**
 * Creates an account and signs it in.
 *
 * @throws {@link ApiError} 400 with field errors, e.g. when the email address is already registered.
 */
export async function register(
  details: RegistrationDetails,
  options: RequestOptions = {},
): Promise<AuthenticatedUser> {
  const { data } = await httpClient.post<AuthenticatedUser>('/auth/register', details, options)
  return data
}

/**
 * Signs out by asking the API to delete the auth cookie (JavaScript cannot touch an HttpOnly cookie).
 *
 * @throws {@link ApiError} 401 when there is no session left to end.
 */
export async function logout(options: RequestOptions = {}): Promise<void> {
  await httpClient.post('/auth/logout', undefined, options)
}

/**
 * Returns the user identified by the auth cookie.
 *
 * @throws {@link ApiError} 401 when not signed in.
 */
export async function getCurrentUser(options: RequestOptions = {}): Promise<AuthenticatedUser> {
  const { data } = await httpClient.get<AuthenticatedUser>('/auth/me', options)
  return data
}
