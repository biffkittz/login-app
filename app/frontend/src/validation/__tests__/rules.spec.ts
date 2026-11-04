import { describe, expect, it } from 'vitest'

import { email, matchesField, maxLength, minLength, minUniqueCharacters, required } from '../rules'

describe('validation rules', () => {
  it('required rejects empty and whitespace-only values', () => {
    const rule = required('Required')

    expect(rule('', undefined)).toBe('Required')
    expect(rule('   ', undefined)).toBe('Required')
    expect(rule('x', undefined)).toBeUndefined()
  })

  it.each(['ada@example.com', ' grace@navy.mil ', "o'brien@example.ie"])(
    'email accepts %s',
    (value) => {
      expect(email('Invalid')(value, undefined)).toBeUndefined()
    },
  )

  it.each(['ada', 'ada@', '@example.com', 'ada@example', 'a da@example.com'])(
    'email rejects %s',
    (value) => {
      expect(email('Invalid')(value, undefined)).toBe('Invalid')
    },
  )

  it('leaves empty values to the required rule so only one message shows', () => {
    expect(email('Invalid')('', undefined)).toBeUndefined()
    expect(minLength(12, 'Too short')('', undefined)).toBeUndefined()
    expect(minUniqueCharacters(4, 'Too repetitive')('', undefined)).toBeUndefined()
  })

  it('minLength and maxLength enforce inclusive bounds', () => {
    expect(minLength(3, 'Too short')('ab', undefined)).toBe('Too short')
    expect(minLength(3, 'Too short')('abc', undefined)).toBeUndefined()
    expect(maxLength(3, 'Too long')('abcd', undefined)).toBe('Too long')
    expect(maxLength(3, 'Too long')('abc', undefined)).toBeUndefined()
  })

  it('minUniqueCharacters rejects repetitive passwords', () => {
    const rule = minUniqueCharacters(4, 'Too repetitive')

    expect(rule('aaaaaaaaaaaa', undefined)).toBe('Too repetitive')
    expect(rule('abcabcabcabc', undefined)).toBe('Too repetitive')
    expect(rule('abcdabcdabcd', undefined)).toBeUndefined()
  })

  it('matchesField compares against another field', () => {
    type Form = { password: string; confirmPassword: string }
    const rule = matchesField<Form>('password', 'No match')

    expect(rule('secret', { password: 'secret', confirmPassword: 'secret' })).toBeUndefined()
    expect(rule('secreT', { password: 'secret', confirmPassword: 'secreT' })).toBe('No match')
  })
})
