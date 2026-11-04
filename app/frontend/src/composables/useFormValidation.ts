import { computed, ref, watch, type ComputedRef } from 'vue'

import type { FieldErrors } from '@/services/api-error'
import type { ValidationRule } from '@/validation/rules'

/** Field values of a form. Every field is a string, as produced by text inputs. */
export type FormValues = Record<string, string>

/** The rules to apply to each field, in order; the first failing rule supplies the message. */
export type FormRules<TValues extends FormValues> = {
  [TField in keyof TValues]?: ReadonlyArray<ValidationRule<TValues[TField], TValues>>
}

/** One message per field that currently has a problem. */
export type FormErrors<TValues extends FormValues> = Partial<Record<keyof TValues, string>>

/** Reactive validation state and actions returned by {@link useFormValidation}. */
export interface FormValidation<TValues extends FormValues> {
  /** The message to show under each invalid field. */
  errors: ComputedRef<FormErrors<TValues>>
  /** Starts validating a field, typically when it loses focus. */
  touch: (field: keyof TValues) => void
  /** Validates every field and reports whether the form may be submitted. */
  validateAll: () => boolean
  /** Shows errors returned by the API next to the matching fields. */
  setServerErrors: (fieldErrors: FieldErrors) => void
}

/**
 * Client-side form validation, timed the way users find least annoying:
 *
 * 1. A field is first checked when it loses focus with something typed in it, never while the user is still typing
 *    their first attempt, and never merely for being tabbed past while empty.
 * 2. After that it is re-checked on every change, so the message disappears the moment the problem is fixed.
 * 3. Submitting checks every field, empty ones included.
 *
 * Errors from the server (e.g. "email already registered") stay visible until that field is edited.
 *
 * @param values - The reactive form values (`reactive({...})`).
 * @param rules - The rules for each field.
 */
export function useFormValidation<TValues extends FormValues>(
  values: TValues,
  rules: FormRules<TValues>,
): FormValidation<TValues> {
  const clientErrors = ref<FormErrors<TValues>>({})
  const serverErrors = ref<FormErrors<TValues>>({})
  const touched = new Set<keyof TValues>()
  const validatedFields = Object.keys(rules) as Array<keyof TValues>

  function firstFailure(field: keyof TValues): string | undefined {
    for (const rule of rules[field] ?? []) {
      const message = rule(values[field], values)
      if (message !== undefined) {
        return message
      }
    }
    return undefined
  }

  function validateField(field: keyof TValues): boolean {
    const message = firstFailure(field)
    const next: FormErrors<TValues> = { ...clientErrors.value }
    if (message === undefined) {
      delete next[field]
    } else {
      next[field] = message
    }
    clientErrors.value = next
    return message === undefined
  }

  function touch(field: keyof TValues): void {
    // Tabbing past a field the user hasn't typed in yet is not a mistake; "required" messages wait for submit.
    if (!touched.has(field) && (values[field] ?? '').trim() === '') {
      return
    }
    touched.add(field)
    validateField(field)
  }

  function validateAll(): boolean {
    let valid = true
    for (const field of validatedFields) {
      touched.add(field)
      valid = validateField(field) && valid
    }
    return valid && Object.keys(serverErrors.value).length === 0
  }

  // Snapshot of the values, used to tell which fields an edit actually touched.
  let previous = { ...values }

  function setServerErrors(fieldErrors: FieldErrors): void {
    const next: FormErrors<TValues> = {}
    for (const [field, messages] of Object.entries(fieldErrors)) {
      if (field in values && messages[0] !== undefined) {
        next[field as keyof TValues] = messages[0]
      }
    }
    serverErrors.value = next
    // The server judged the values as they are right now, so only edits made from here on should clear its errors.
    previous = { ...values }
  }

  // Revalidate on any change, not just the edited field, so cross-field rules ("passwords match") update when
  // either field changes. A server error is cleared only when its own field is edited.
  watch(values, () => {
    const remainingServerErrors: FormErrors<TValues> = { ...serverErrors.value }
    for (const field of Object.keys(values) as Array<keyof TValues>) {
      if (values[field] !== previous[field]) {
        delete remainingServerErrors[field]
      }
    }
    serverErrors.value = remainingServerErrors
    previous = { ...values }

    for (const field of touched) {
      validateField(field)
    }
  })

  return {
    // Client-side problems take precedence: they describe the value as it is now.
    errors: computed(() => ({ ...serverErrors.value, ...clientErrors.value })),
    touch,
    validateAll,
    setServerErrors,
  }
}
