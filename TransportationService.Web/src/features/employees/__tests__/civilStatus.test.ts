import { describe, expect, it } from 'vitest'
import { CIVIL_STATUS_LABELS, type CivilStatus } from '../types/employee'

describe('CIVIL_STATUS_LABELS', () => {
  it('maps every civil status to its Dutch label', () => {
    expect(CIVIL_STATUS_LABELS).toEqual({
      Married: 'Gehuwd',
      Unmarried: 'Ongehuwd',
      Widowed: 'Weduwe/weduwnaar',
      Divorced: 'Gescheiden',
      LegallyCohabiting: 'Wettelijk samenwonend',
      Single: 'Alleenstaand',
      Other: 'Andere',
    })
  })

  it('covers all seven statuses without empty labels', () => {
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
      expect(CIVIL_STATUS_LABELS[status]).toBeTruthy()
    }
  })
})
