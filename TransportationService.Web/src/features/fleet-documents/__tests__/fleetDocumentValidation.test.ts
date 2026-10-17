import { describe, expect, it } from 'vitest'
import { fleetDocumentValidationKey } from '../validation'
import type { FleetDocumentType } from '../types'

// FL-M2: a fleet document needs at least a number or an expiry date, and some types need a specific one.

interface Fields {
  customTypeName: string | null
  documentNumber: string | null
  expiryDate: string | null
  issueDate: string | null
}
const base: Fields = { customTypeName: null, documentNumber: null, expiryDate: null, issueDate: null }
const doc = (documentType: FleetDocumentType, extra: Partial<typeof base> = {}) => ({ ...base, documentType, ...extra })

describe('fleetDocumentValidationKey', () => {
  it.each<FleetDocumentType>(['Insurance', 'TechnicalInspection', 'AdrCertificate', 'CraneInspection', 'RefrigerationCertificate', 'TachographCalibration'])(
    '%s requires an expiry date even with a number',
    (type) => {
      expect(fleetDocumentValidationKey(doc(type, { documentNumber: 'P-1' }))).toBe('fleet.docs.expiryRequired')
      expect(fleetDocumentValidationKey(doc(type, { expiryDate: '2027-01-01' }))).toBeNull()
    },
  )

  it.each<FleetDocumentType>(['Registration', 'Conformity'])('%s requires a document number', (type) => {
    expect(fleetDocumentValidationKey(doc(type, { expiryDate: '2027-01-01' }))).toBe('fleet.docs.numberRequired')
    expect(fleetDocumentValidationKey(doc(type, { documentNumber: '1-ABC-123' }))).toBeNull()
  })

  it('Other needs a name plus a number or a date', () => {
    expect(fleetDocumentValidationKey(doc('Other', { documentNumber: 'X' }))).toBe('fleet.docs.customNameRequired')
    expect(fleetDocumentValidationKey(doc('Other', { customTypeName: 'Vergunning' }))).toBe('fleet.docs.numberOrExpiryRequired')
    expect(fleetDocumentValidationKey(doc('Other', { customTypeName: 'Vergunning', expiryDate: '2027-01-01' }))).toBeNull()
    expect(fleetDocumentValidationKey(doc('Other', { customTypeName: 'Vergunning', documentNumber: '  ' }))).toBe('fleet.docs.numberOrExpiryRequired')
  })

  it('rejects an expiry before the issue date', () => {
    expect(fleetDocumentValidationKey(doc('Insurance', { issueDate: '2026-05-01', expiryDate: '2026-04-30' }))).toBe('fleet.docs.expiryBeforeIssue')
  })
})
