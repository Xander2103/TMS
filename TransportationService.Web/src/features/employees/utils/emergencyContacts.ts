import type { EmployeeEmergencyContact, EmployeeEmergencyContactInput } from '../types/employee'

/** One row in the emergency-contacts repeater. `key` is a stable client-side identity. */
export interface EmergencyContactRow {
  key: string
  id: string | null
  name: string
  relationship: string
  phone: string
  mobilePhone: string
  notes: string
  priority: number
}

function newKey(): string {
  // crypto.randomUUID is available in the browser and jsdom; fall back for safety.
  return typeof crypto !== 'undefined' && 'randomUUID' in crypto
    ? crypto.randomUUID()
    : `row-${Math.random().toString(36).slice(2)}`
}

/** Builds an empty row with a priority defaulting to `index + 1`. */
export function createEmergencyContactRow(index: number): EmergencyContactRow {
  return {
    key: newKey(),
    id: null,
    name: '',
    relationship: '',
    phone: '',
    mobilePhone: '',
    notes: '',
    priority: index + 1,
  }
}

/**
 * Initial rows for the form. Existing contacts (sorted by priority) are mapped; when none
 * exist a single empty row is shown so the first noodcontact is always visible.
 */
export function emergencyContactRowsFromDetail(contacts: EmployeeEmergencyContact[] | undefined): EmergencyContactRow[] {
  if (!contacts || contacts.length === 0) return [createEmergencyContactRow(0)]
  return [...contacts]
    .sort((a, b) => a.priority - b.priority)
    .map((c, index) => ({
      key: newKey(),
      id: c.id,
      name: c.name,
      relationship: c.relationship ?? '',
      phone: c.phone ?? '',
      mobilePhone: c.mobilePhone ?? '',
      notes: c.notes ?? '',
      priority: c.priority || index + 1,
    }))
}

/** Appends an empty row, priority defaulting to the next position. */
export function addEmergencyContactRow(rows: EmergencyContactRow[]): EmergencyContactRow[] {
  return [...rows, createEmergencyContactRow(rows.length)]
}

/** Removes the row with the given key; never removes the last remaining row (keeps one visible). */
export function removeEmergencyContactRow(rows: EmergencyContactRow[], key: string): EmergencyContactRow[] {
  if (rows.length <= 1) return [createEmergencyContactRow(0)]
  return rows.filter((row) => row.key !== key)
}

/** Patches a single row by key. */
export function updateEmergencyContactRow(
  rows: EmergencyContactRow[],
  key: string,
  patch: Partial<Omit<EmergencyContactRow, 'key'>>,
): EmergencyContactRow[] {
  return rows.map((row) => (row.key === key ? { ...row, ...patch } : row))
}

function nullable(value: string): string | null {
  const trimmed = value.trim()
  return trimmed ? trimmed : null
}

/**
 * Maps rows to the API payload. Rows without a (trimmed) name are dropped — they are treated
 * as empty. Priority defaults to the row's position when not a positive number.
 */
export function emergencyContactRowsToPayload(rows: EmergencyContactRow[]): EmployeeEmergencyContactInput[] {
  return rows
    .filter((row) => row.name.trim().length > 0)
    .map((row, index) => ({
      id: row.id,
      name: row.name.trim(),
      relationship: nullable(row.relationship),
      phone: nullable(row.phone),
      mobilePhone: nullable(row.mobilePhone),
      notes: nullable(row.notes),
      priority: row.priority > 0 ? row.priority : index + 1,
    }))
}
