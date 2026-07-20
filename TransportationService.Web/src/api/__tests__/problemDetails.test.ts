import { describe, expect, it } from 'vitest'
import { describeApiError, extractFieldErrors, getFieldError, normalizeFieldPath } from '../problemDetails'

describe('normalizeFieldPath', () => {
  it('camelCases every segment and preserves indexers', () => {
    expect(normalizeFieldPath('VatNumber')).toBe('vatNumber')
    expect(normalizeFieldPath('Stops[0].City')).toBe('stops[0].city')
    expect(normalizeFieldPath('cargoItems[2].expectedQuantity')).toBe('cargoItems[2].expectedQuantity')
  })

  it('drops leading $ and request segments', () => {
    expect(normalizeFieldPath('$.stops[1].city')).toBe('stops[1].city')
    expect(normalizeFieldPath('Request.Name')).toBe('name')
    expect(normalizeFieldPath('request.contact.email')).toBe('contact.email')
  })

  it('leaves already-normalised paths untouched', () => {
    expect(normalizeFieldPath('vatNumber')).toBe('vatNumber')
  })
})

describe('extractFieldErrors', () => {
  it('reads the ProblemDetails errors dictionary', () => {
    const body = {
      title: 'Validatiefout',
      detail: 'Ongeldig BTW-nummer.',
      status: 400,
      errors: { vatNumber: ['Ongeldig BTW-nummer.'] },
    }
    expect(extractFieldErrors(body)).toEqual({ vatNumber: ['Ongeldig BTW-nummer.'] })
  })

  it('normalises ASP.NET ValidationProblemDetails keys', () => {
    const body = { errors: { 'Stops[0].City': ['Gemeente is verplicht.'], Name: ['Naam is verplicht.'] } }
    expect(extractFieldErrors(body)).toEqual({
      'stops[0].city': ['Gemeente is verplicht.'],
      name: ['Naam is verplicht.'],
    })
  })

  it('accepts single-string values and merges colliding keys', () => {
    const body = { errors: { Iban: 'Ongeldig formaat.', iban: ['Ongeldig controlegetal.'] } }
    expect(extractFieldErrors(body)).toEqual({ iban: ['Ongeldig formaat.', 'Ongeldig controlegetal.'] })
  })

  it('returns an empty object for bodies without usable errors', () => {
    expect(extractFieldErrors(undefined)).toEqual({})
    expect(extractFieldErrors(null)).toEqual({})
    expect(extractFieldErrors({ message: 'Naam is verplicht.' })).toEqual({})
    expect(extractFieldErrors({ errors: 'kapot' })).toEqual({})
    expect(extractFieldErrors({ errors: { veld: [42] } })).toEqual({})
  })
})

describe('getFieldError', () => {
  const errors = { vatNumber: ['Ongeldig BTW-nummer.'], 'stops[0].city': ['Gemeente is verplicht.'] }

  it('returns the first message for the first matching path', () => {
    expect(getFieldError(errors, 'vatNumber')).toBe('Ongeldig BTW-nummer.')
    expect(getFieldError(errors, 'btw', 'vatNumber')).toBe('Ongeldig BTW-nummer.')
    expect(getFieldError(errors, 'stops[0].city')).toBe('Gemeente is verplicht.')
  })

  it('returns undefined when nothing matches', () => {
    expect(getFieldError(errors, 'iban')).toBeUndefined()
    expect(getFieldError(undefined, 'iban')).toBeUndefined()
  })
})

describe('describeApiError', () => {
  it('surfaces message and field errors from an ApiError-shaped error', () => {
    const error = Object.assign(new Error('Ongeldig BTW-nummer.'), {
      fieldErrors: { vatNumber: ['Ongeldig BTW-nummer.'] },
    })
    expect(describeApiError(error, 'Opslaan mislukt.')).toEqual({
      message: 'Ongeldig BTW-nummer.',
      fieldErrors: { vatNumber: ['Ongeldig BTW-nummer.'] },
    })
  })

  it('falls back for plain errors and non-errors', () => {
    expect(describeApiError(new Error('kapot'), 'Opslaan mislukt.')).toEqual({ message: 'kapot', fieldErrors: {} })
    expect(describeApiError('boom', 'Opslaan mislukt.')).toEqual({ message: 'Opslaan mislukt.', fieldErrors: {} })
    expect(describeApiError(new Error(''), 'Opslaan mislukt.')).toEqual({ message: 'Opslaan mislukt.', fieldErrors: {} })
  })
})
