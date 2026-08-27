import { describe, expect, it } from 'vitest'
import {
  addContactRow,
  contactRowIsEmpty,
  contactRowsToPayload,
  createContactRow,
  payloadIndexByKey,
  removeContactRow,
  updateContactRow,
  validateContactRows,
  type ContactRow,
} from '../contactRows'

function row(overrides: Partial<ContactRow>): ContactRow {
  return { ...createContactRow(), ...overrides }
}

describe('contact rows', () => {
  it('treats rows without any typed value as empty', () => {
    expect(contactRowIsEmpty(createContactRow())).toBe(true)
    expect(contactRowIsEmpty(row({ isPrimary: true, contactType: 'Planning' }))).toBe(true)
    expect(contactRowIsEmpty(row({ email: 'a@b.be' }))).toBe(false)
  })

  it('keeps at least one row and preserves others on remove', () => {
    const a = row({ firstName: 'An' })
    const b = row({ firstName: 'Jan' })
    expect(removeContactRow([a, b], a.key)).toEqual([b])
    const fresh = removeContactRow([a], a.key)
    expect(fresh).toHaveLength(1)
    expect(fresh[0].firstName).toBe('')
  })

  it('adds and patches rows without touching the others', () => {
    const a = row({ firstName: 'An' })
    const rows = addContactRow([a])
    expect(rows).toHaveLength(2)
    const patched = updateContactRow(rows, rows[1].key, { firstName: 'Jan' })
    expect(patched[0].firstName).toBe('An')
    expect(patched[1].firstName).toBe('Jan')
  })

  it('maps only non-empty rows to payload indices', () => {
    const a = row({ firstName: 'An' })
    const empty = createContactRow()
    const b = row({ firstName: 'Jan' })
    const map = payloadIndexByKey([a, empty, b])
    expect(map.get(a.key)).toBe(0)
    expect(map.has(empty.key)).toBe(false)
    expect(map.get(b.key)).toBe(1)
  })

  it('requires names per filled row, using payload indices in the error paths', () => {
    const empty = createContactRow()
    const partial = row({ email: 'a@b.be' })
    const errors = validateContactRows([empty, partial])
    expect(errors['contacts[0].firstName']).toBe('customers.contactValidation.firstNameRequired')
    expect(errors['contacts[0].lastName']).toBe('customers.contactValidation.lastNameRequired')
  })

  it('allows one primary per type and flags the second of the same type', () => {
    const a = row({ firstName: 'An', lastName: 'Peeters', contactType: 'Algemeen', isPrimary: true })
    const b = row({ firstName: 'Jan', lastName: 'Claes', contactType: 'Facturatie', isPrimary: true })
    const c = row({ firstName: 'Els', lastName: 'Maes', contactType: 'Algemeen', isPrimary: true })
    const errors = validateContactRows([a, b, c])
    expect(Object.keys(errors)).toEqual(['contacts[2].isPrimary'])
    expect(errors['contacts[2].isPrimary']).toBe('customers.contactValidation.duplicatePrimary')
  })

  it('drops empty rows from the payload and trims/nullifies fields', () => {
    const a = row({
      firstName: ' An ',
      lastName: ' Peeters ',
      role: '  ',
      contactType: 'Planning',
      email: ' an@acme.be ',
      isPrimary: true,
    })
    const payload = contactRowsToPayload([createContactRow(), a])
    expect(payload).toHaveLength(1)
    expect(payload[0]).toMatchObject({
      firstName: 'An',
      lastName: 'Peeters',
      role: null,
      email: 'an@acme.be',
      phoneNumber: null,
      mobilePhone: null,
      contactType: 'Planning',
      isPrimary: true,
      isActive: true,
      departmentId: null,
    })
  })
})
