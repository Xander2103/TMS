import { describe, expect, it } from 'vitest'
import { translate } from '../../../i18n/translations'
import { REVENUE_SOURCE_LABELS, formatEuro, marginTone } from '../types'

describe('marginTone', () => {
  it('flags losses, thin margins and healthy margins distinctly', () => {
    expect(marginTone(-3)).toBe('danger')
    expect(marginTone(0)).toBe('warning')
    expect(marginTone(9.9)).toBe('warning')
    expect(marginTone(10)).toBe('success')
    expect(marginTone(null)).toBe('neutral')
  })
})

describe('revenue source labels', () => {
  it('keeps revenue natures distinguishable (never one ambiguous value)', () => {
    // Key-map + NL-vertaling: logica keyt op de code, weergave via t().
    expect(translate('nl', REVENUE_SOURCE_LABELS.Invoiced)).toBe('Gefactureerd')
    expect(translate('nl', REVENUE_SOURCE_LABELS.Agreed)).toBe('Afgesproken')
    expect(translate('nl', REVENUE_SOURCE_LABELS.None)).toBe('Geen omzet')
  })
})

describe('formatEuro', () => {
  it('formats whole euros in Belgian locale', () => {
    expect(formatEuro(1250)).toContain('1.250')
    expect(formatEuro(1250)).toContain('€')
  })
})
