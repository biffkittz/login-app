/**
 * A validation rule: returns an error message, or `undefined` when the value is acceptable.
 *
 * @typeParam TValue - The type of the field being validated.
 * @typeParam TValues - The type of the whole form, for rules that compare fields.
 */
export type ValidationRule<TValue, TValues = unknown> = (
  value: TValue,
  values: TValues,
) => string | undefined

// Rules other than `required` accept an empty value, so an empty field reports "required" instead of a
// confusing "too short" or "invalid email" message.
const isEmpty = (value: string): boolean => value.trim().length === 0

/** Fails when the field is empty or whitespace only. */
export const required =
  (message: string): ValidationRule<string> =>
  (value) =>
    isEmpty(value) ? message : undefined

// Deliberately permissive: something@something.something. Anything stricter starts rejecting real addresses;
// the server performs its own check.
const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]+$/

/** Fails when the value does not look like an email address. */
export const email =
  (message: string): ValidationRule<string> =>
  (value) =>
    isEmpty(value) || EMAIL_PATTERN.test(value.trim()) ? undefined : message

/** Fails when the value has fewer than `length` characters. */
export const minLength =
  (length: number, message: string): ValidationRule<string> =>
  (value) =>
    isEmpty(value) || value.length >= length ? undefined : message

/** Fails when the value has more than `length` characters. */
export const maxLength =
  (length: number, message: string): ValidationRule<string> =>
  (value) =>
    value.length <= length ? undefined : message

/** Fails when the value uses fewer than `count` distinct characters (rejects "aaaaaaaaaaaa"). */
export const minUniqueCharacters =
  (count: number, message: string): ValidationRule<string> =>
  (value) =>
    isEmpty(value) || new Set(value).size >= count ? undefined : message

/** Fails unless the value equals another field's value (e.g. "confirm password"). */
export const matchesField =
  <TValues>(field: keyof TValues, message: string): ValidationRule<string, TValues> =>
  (value, values) =>
    isEmpty(value) || value === values[field] ? undefined : message
