import { describe, expect, it } from 'vitest'
import { nextTick, reactive } from 'vue'

import { matchesField, minLength, required } from '@/validation/rules'

import { useFormValidation } from '../useFormValidation'

type SignUp = { email: string; password: string; confirmPassword: string }

function setup() {
  const values = reactive<SignUp>({ email: '', password: '', confirmPassword: '' })
  const validation = useFormValidation(values, {
    email: [required('Enter your email.')],
    password: [required('Choose a password.'), minLength(12, 'Too short.')],
    confirmPassword: [matchesField<SignUp>('password', "Passwords don't match.")],
  })
  return { values, ...validation }
}

describe('useFormValidation', () => {
  it('does not complain about untouched fields while the user is typing', async () => {
    const { values, errors } = setup()

    values.password = 'short'
    await nextTick()

    expect(errors.value).toEqual({})
  })

  it('does not flag an empty field the user merely tabbed past', () => {
    const { errors, touch, validateAll } = setup()

    touch('email')
    expect(errors.value.email).toBeUndefined()

    validateAll()
    expect(errors.value.email).toBe('Enter your email.')
  })

  it('validates a field once it loses focus, then live as it changes', async () => {
    const { values, errors, touch } = setup()

    values.password = 'short'
    touch('password')
    expect(errors.value.password).toBe('Too short.')

    values.password = 'long enough password'
    await nextTick()
    expect(errors.value.password).toBeUndefined()
  })

  it('validateAll checks every field and reports validity', () => {
    const { values, errors, validateAll } = setup()

    expect(validateAll()).toBe(false)
    expect(errors.value).toEqual({ email: 'Enter your email.', password: 'Choose a password.' })

    values.email = 'ada@example.com'
    values.password = 'long enough password'
    values.confirmPassword = 'long enough password'
    expect(validateAll()).toBe(true)
  })

  it('re-checks dependent fields when the field they compare against changes', async () => {
    const { values, errors, touch } = setup()
    values.password = 'long enough password'
    values.confirmPassword = 'long enough password'
    touch('confirmPassword')

    values.password = 'a different password'
    await nextTick()

    expect(errors.value.confirmPassword).toBe("Passwords don't match.")
  })

  it('keeps a server error until its own field is edited', async () => {
    const { values, errors, setServerErrors } = setup()
    values.email = 'ada@example.com'
    setServerErrors({ email: ['An account with this email address already exists.'] })

    values.password = 'editing another field'
    await nextTick()
    expect(errors.value.email).toBe('An account with this email address already exists.')

    values.email = 'grace@example.com'
    await nextTick()
    expect(errors.value.email).toBeUndefined()
  })

  it('ignores server errors for fields the form does not have', () => {
    const { errors, setServerErrors } = setup()

    setServerErrors({ unknownField: ['Nope.'] })

    expect(errors.value).toEqual({})
  })
})
