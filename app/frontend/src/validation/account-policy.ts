/**
 * Account rules mirrored from the API (backend/Features/Auth/PasswordPolicy.cs and the request contracts) so the
 * forms can give instant feedback. The server stays the authority: keep these values in sync with it.
 */
export const PASSWORD_MIN_LENGTH = 12
export const PASSWORD_MAX_LENGTH = 128
export const PASSWORD_MIN_UNIQUE_CHARACTERS = 4
export const DISPLAY_NAME_MAX_LENGTH = 64
export const EMAIL_MAX_LENGTH = 256
