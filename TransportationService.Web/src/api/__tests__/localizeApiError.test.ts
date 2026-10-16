import { afterEach, describe, expect, it } from 'vitest'
import { ApiError } from '../apiClient'
import { localizeApiError } from '../problemDetails'
import { setActiveLocale } from '../../i18n/activeLocale'

const t = (key: string) => (key === 'errors.codes.common.validation_failed' ? 'De invoer is ongeldig.' : key)

function validationError(detail: string): ApiError {
  return new ApiError(detail, 400, { title: 'Validatiefout', detail, code: 'common.validation_failed' })
}

afterEach(() => setActiveLocale('nl'))

// HR acceptance P2-5: the specific server reason must reach the user, not the generic text.
describe('localizeApiError with the generic validation code', () => {
  it('shows the specific Dutch server detail in a Dutch UI', () => {
    setActiveLocale('nl')
    const message = localizeApiError(t, validationError('Dit e-mailadres is al in gebruik bij Jan (MED-1).'), 'Mislukt')
    expect(message).toBe('Dit e-mailadres is al in gebruik bij Jan (MED-1).')
  })

  it('keeps the translated generic text for other UI languages', () => {
    setActiveLocale('en')
    expect(localizeApiError(t, validationError('Dit e-mailadres is al in gebruik.'), 'Failed')).toBe('De invoer is ongeldig.')
  })
})
