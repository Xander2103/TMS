import { describe, expect, it } from 'vitest'
import { translate } from '../../../i18n/translations'
import { CIVIL_STATUS_LABELS, type CivilStatus } from '../types/employee'

describe('CIVIL_STATUS_LABELS', () => {
  it('maps every civil status to its i18n key', () => {
    expect(CIVIL_STATUS_LABELS).toEqual({
      Married: 'employees.civilStatus.Married',
      Unmarried: 'employees.civilStatus.Unmarried',
      Widowed: 'employees.civilStatus.Widowed',
      Divorced: 'employees.civilStatus.Divorced',
      LegallyCohabiting: 'employees.civilStatus.LegallyCohabiting',
      Single: 'employees.civilStatus.Single',
      Other: 'employees.civilStatus.Other',
    })
  })

  it('covers all seven statuses and resolves each key to a Dutch label', () => {
    const statuses: CivilStatus[] = [
      'Married',
      'Unmarried',
      'Widowed',
      'Divorced',
      'LegallyCohabiting',
      'Single',
      'Other',
    ]
    expect(Object.keys(CIVIL_STATUS_LABELS)).toHaveLength(statuses.length)
    for (const status of statuses) {
      const resolved = translate('nl', CIVIL_STATUS_LABELS[status])
      expect(resolved).toBeTruthy()
      // A missing key would fall back to the key itself — that counts as a failure.
      expect(resolved).not.toBe(CIVIL_STATUS_LABELS[status])
    }
  })

  it('resolves the canonical Dutch labels', () => {
    expect(translate('nl', CIVIL_STATUS_LABELS.Married)).toBe('Gehuwd')
    expect(translate('nl', CIVIL_STATUS_LABELS.Widowed)).toBe('Weduwe/weduwnaar')
    expect(translate('nl', CIVIL_STATUS_LABELS.LegallyCohabiting)).toBe('Wettelijk samenwonend')
  })
})
