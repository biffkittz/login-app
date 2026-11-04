/** The signed-in user, as returned by the API (`UserResponse` in the backend). */
export interface AuthenticatedUser {
  id: string
  email: string
  displayName: string
  /** ISO 8601 instant at which the auth cookie, and therefore the session, expires. */
  sessionExpiresAt: string
}

/** The body of `POST /api/auth/login`. */
export interface LoginCredentials {
  email: string
  password: string
}

/** The body of `POST /api/auth/register`. */
export interface RegistrationDetails {
  displayName: string
  email: string
  password: string
}

/** Why the user was returned to the login page; decides which notice the login page shows. */
export type SignOutReason = 'signed-out' | 'session-expired'
