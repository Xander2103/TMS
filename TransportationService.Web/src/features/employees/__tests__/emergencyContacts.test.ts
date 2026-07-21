import { describe, expect, it } from 'vitest'
import {
  addEmergencyContactRow,
  createEmergencyContactRow,
  emergencyContactRowsFromDetail,
  emergencyContactRowsToPayload,
  removeEmergencyContactRow,
  updateEmergencyContactRow,
} from '../utils/emergencyContacts'
import type { EmployeeEmergencyContact } from '../types/employee'

describe('emergency-contacts repeater helpers', () => {
  it('creates an empty row with priority = index + 1', () => {
    const row = createEmergencyContactRow(0)
    expect(row.priority).toBe(1)
    expect(row.name).toBe('')
    expect(row.id).toBeNull()
    expect(createEmergencyContactRow(2).priority).toBe(3)
  })

  it('always yields at least one row from an empty detail', () => {
    expect(emergencyContactRowsFromDetail(undefined)).toHaveLength(1)
    expect(emergencyContactRowsFromDetail([])).toHaveLength(1)
  })

  it('maps and sorts existing contacts by priority', () => {
    const contacts: EmployeeEmergencyContact[] = [
      { id: 'b', name: 'Bea', relationship: 'Zus', phone: '02', mobilePhone: null, notes: null, priority: 2 },
      { id: 'a', name: 'An', relationship: null, phone: null, mobilePhone: '0499', notes: 'x', priority: 1 },
    ]
    const rows = emergencyContactRowsFromDetail(contacts)
    expect(rows.map((r) => r.name)).toEqual(['An', 'Bea'])
    expect(rows[0].id).toBe('a')
    expect(rows[0].relationship).toBe('')
    expect(rows[1].relationship).toBe('Zus')
  })

  it('adds a row with the next priority', () => {
    const rows = addEmergencyContactRow([createEmergencyContactRow(0)])
    expect(rows).toHaveLength(2)
    expect(rows[1].priority).toBe(2)
  })

  it('removes a row by key but never drops the last one', () => {
    const one = createEmergencyContactRow(0)
    const two = createEmergencyContactRow(1)
    const after = removeEmergencyContactRow([one, two], one.key)
    expect(after).toHaveLength(1)
    expect(after[0].key).toBe(two.key)

    const cleared = removeEmergencyContactRow([two], two.key)
    expect(cleared).toHaveLength(1)
    expect(cleared[0].name).toBe('')
  })

  it('patches a single row by key', () => {
    const one = createEmergencyContactRow(0)
    const two = createEmergencyContactRow(1)
    const updated = updateEmergencyContactRow([one, two], two.key, { name: 'Jan' })
    expect(updated[0].name).toBe('')
    expect(updated[1].name).toBe('Jan')
  })

  it('drops nameless rows and maps the rest to the payload', () => {
    const rows = [
      { ...createEmergencyContactRow(0), name: '  An ', relationship: 'Partner', phone: ' 02 ', mobilePhone: '', notes: '', priority: 1 },
      { ...createEmergencyContactRow(1), name: '   ', phone: '099', priority: 2 },
    ]
    const payload = emergencyContactRowsToPayload(rows)
    expect(payload).toHaveLength(1)
    expect(payload[0]).toMatchObject({
      name: 'An',
      relationship: 'Partner',
      phone: '02',
      mobilePhone: null,
      notes: null,
      priority: 1,
    })
  })

  it('falls back to positional priority when priority is not positive', () => {
    const rows = [{ ...createEmergencyContactRow(0), name: 'An', priority: 0 }]
    expect(emergencyContactRowsToPayload(rows)[0].priority).toBe(1)
  })
})
