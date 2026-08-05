import type { CustomerContactInput, CustomerContactType } from '../types'

/**
 * Contact-person repeater helpers for the customer CREATE flow: rows live in client-side
 * form state (stable temp keys) and are posted in one go via `contacts[]` on the create
 * request. Fully empty rows are treated as "not entered" and dropped from the payload;
 * backend error paths (`contacts[i].firstName`, …) index the NON-empty rows in order, so
 * `payloadIndexByKey` maps those paths back to the visible rows.
 */
export interface ContactRow {
  /** Stable client-side identity for React keys and targeted updates. */
  key: string
  firstName: string
  lastName: string
  role: string
  contactType: CustomerContactType
  email: string
  phoneNumber: string
  mobilePhone: string
  /** Primair binnen dit type (hoogstens één per type). */
  isPrimary: boolean
}

function newKey(): string {
  // crypto.randomUUID is available in the browser and jsdom; fall back for safety.
  return typeof crypto !== 'undefined' && 'randomUUID' in crypto
    ? crypto.randomUUID()
    : `row-${Math.random().toString(36).slice(2)}`
}

export function createContactRow(): ContactRow {
  return {
    key: newKey(),
    firstName: '',
    lastName: '',
    role: '',
    contactType: 'Algemeen',
    email: '',
    phoneNumber: '',
    mobilePhone: '',
    isPrimary: false,
  }
}

export function addContactRow(rows: ContactRow[]): ContactRow[] {
  return [...rows, createContactRow()]
}

/** Removes the row with the given key; keeps one (empty) row visible at all times. */
export function removeContactRow(rows: ContactRow[], key: string): ContactRow[] {
  if (rows.length <= 1) return [createContactRow()]
  return rows.filter((row) => row.key !== key)
}

export function updateContactRow(
  rows: ContactRow[],
  key: string,
  patch: Partial<Omit<ContactRow, 'key'>>,
): ContactRow[] {
  return rows.map((row) => (row.key === key ? { ...row, ...patch } : row))
}

/** A row where the user typed nothing at all — dropped instead of validated. */
export function contactRowIsEmpty(row: ContactRow): boolean {
  return [row.firstName, row.lastName, row.role, row.email, row.phoneNumber, row.mobilePhone]
    .every((value) => value.trim() === '')
}

/**
 * Payload index per row key: non-empty rows get sequential indices matching the posted
 * `contacts[]` array; empty rows map to nothing. Backend errors like `contacts[1].lastName`
 * therefore always point at the right visible row.
 */
export function payloadIndexByKey(rows: ContactRow[]): Map<string, number> {
  const map = new Map<string, number>()
  let index = 0
  for (const row of rows) {
    if (!contactRowIsEmpty(row)) {
      map.set(row.key, index)
      index += 1
    }
  }
  return map
}

/**
 * Client-side validation, keyed by backend-style paths (`contacts[i].field`): names are
 * required on every filled row; at most one primary per type — the SECOND row of a type
 * gets the error on its checkbox.
 */
export function validateContactRows(rows: ContactRow[]): Record<string, string> {
  const errors: Record<string, string> = {}
  const indexByKey = payloadIndexByKey(rows)
  const primaryTypes = new Set<CustomerContactType>()
  for (const row of rows) {
    const index = indexByKey.get(row.key)
    if (index === undefined) continue
    if (!row.firstName.trim()) errors[`contacts[${index}].firstName`] = 'Voornaam van de contactpersoon is verplicht.'
    if (!row.lastName.trim()) errors[`contacts[${index}].lastName`] = 'Achternaam van de contactpersoon is verplicht.'
    if (row.isPrimary) {
      if (primaryTypes.has(row.contactType)) {
        errors[`contacts[${index}].isPrimary`] = 'Er is al een primaire contactpersoon van dit type.'
      } else {
        primaryTypes.add(row.contactType)
      }
    }
  }
  return errors
}

function nullable(value: string): string | null {
  const trimmed = value.trim()
  return trimmed ? trimmed : null
}

/** Maps the non-empty rows to the create payload (`contacts[]`), in visible order. */
export function contactRowsToPayload(rows: ContactRow[]): CustomerContactInput[] {
  return rows
    .filter((row) => !contactRowIsEmpty(row))
    .map((row) => ({
      firstName: row.firstName.trim(),
      lastName: row.lastName.trim(),
      role: nullable(row.role),
      email: nullable(row.email),
      phoneNumber: nullable(row.phoneNumber),
      mobilePhone: nullable(row.mobilePhone),
      contactType: row.contactType,
      isPrimary: row.isPrimary,
      notes: null,
      displayName: null,
      nickname: null,
      departmentId: null,
      preferredLanguageCode: null,
      isActive: true,
    }))
}
