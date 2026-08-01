import { describe, expect, it, vi } from 'vitest'
import { detectBrowserLocale, translate, translateFrom, type MessageBundle } from '../translations'
import { formatCurrency, formatDate, formatDateTime } from '../formatters'

const bundle: MessageBundle = {
  nl: {
    common: {
      greeting: 'Hallo {name}',
      onlyDutch: 'Alleen Nederlands',
      nested: { deep: 'Diep' },
    },
  },
  fr: {
    common: {
      greeting: 'Bonjour {name}',
      nested: { deep: 'Profond' },
    },
  },
  en: {
    common: {
      greeting: 'Hello {name}',
      nested: { deep: 'Deep' },
    },
  },
}

describe('translateFrom', () => {
  it('resolves keys in the chosen locale, including nested paths', () => {
    expect(translateFrom(bundle, 'fr', 'common.nested.deep')).toBe('Profond')
    expect(translateFrom(bundle, 'en', 'common.nested.deep')).toBe('Deep')
  })

  it('interpolates {name}-style parameters and leaves unknown placeholders intact', () => {
    expect(translateFrom(bundle, 'nl', 'common.greeting', { name: 'Kaat' })).toBe('Hallo Kaat')
    expect(translateFrom(bundle, 'nl', 'common.greeting')).toBe('Hallo {name}')
  })

  it('falls back to the Dutch value when the chosen locale misses the key', () => {
    expect(translateFrom(bundle, 'fr', 'common.onlyDutch')).toBe('Alleen Nederlands')
    expect(translateFrom(bundle, 'en', 'common.onlyDutch')).toBe('Alleen Nederlands')
  })

  it('returns the key itself (and warns in dev) when no locale defines it', () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {})
    try {
      expect(translateFrom(bundle, 'fr', 'common.doesNotExist')).toBe('common.doesNotExist')
      expect(warn).toHaveBeenCalled()
    } finally {
      warn.mockRestore()
    }
  })
})

describe('translate (real bundle)', () => {
  it('serves the shipped portal strings per locale', () => {
    expect(translate('nl', 'navigation.logout')).toBe('Uitloggen')
    expect(translate('fr', 'navigation.logout')).toBe('Se déconnecter')
    expect(translate('en', 'navigation.logout')).toBe('Sign out')
  })
})

describe('detectBrowserLocale', () => {
  it('maps the pinned test browser language (nl-BE) to nl', () => {
    expect(detectBrowserLocale()).toBe('nl')
  })
})

describe('formatters', () => {
  it('formats date-only values without timezone day-shifts', () => {
    expect(formatDate('nl', '2026-07-20')).toBe('20/7/2026')
    expect(formatDate('en', '2026-07-20')).toBe('20/07/2026')
  })

  it('formats timestamps per locale', () => {
    // Assert shape rather than exact time: the runner's timezone shifts the clock time.
    expect(formatDateTime('nl', '2026-08-01T10:00:00Z')).toMatch(/\d{1,2}\/\d{1,2}\/\d{4}/)
    expect(formatDateTime('en', '2026-08-01T10:00:00Z')).toMatch(/\d{2}\/\d{2}\/\d{4}/)
  })

  it('formats currency per locale', () => {
    expect(formatCurrency('nl', 1210)).toContain('€')
    expect(formatCurrency('en', 1210, 'EUR')).toContain('1,210')
  })
})
