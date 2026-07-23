import { describe, expect, it, vi, beforeEach } from 'vitest'
import * as docsApi from '../../api/employeeDocumentsApi'
import * as itemsApi from '../../../issued-items/issuedItemsApi'
import {
  uploadPreparedDocuments,
  createPreparedIssuedItems,
  runEmployeeCreateFollowUps,
  type PreparedEmployeeDocument,
  type PreparedIssuedItem,
} from '../preparedFollowUp'

function doc(overrides: Partial<PreparedEmployeeDocument> = {}): PreparedEmployeeDocument {
  return {
    key: overrides.key ?? 'd1',
    file: new File(['x'], overrides.file?.name ?? 'id-card.pdf', { type: 'application/pdf' }),
    category: 'AdditionalDocument',
    customLabel: '',
    expiryDate: '',
    notes: '',
    ...overrides,
  }
}

function item(overrides: Partial<PreparedIssuedItem> = {}): PreparedIssuedItem {
  return {
    key: 'i1',
    templateId: 't1',
    name: 'Laptop',
    category: 'IT',
    quantity: 1,
    serialNumber: '',
    issuedDate: '2027-01-02',
    notes: '',
    returnRequired: true,
    requiresSerialNumber: false,
    ...overrides,
  }
}

beforeEach(() => {
  vi.restoreAllMocks()
})

describe('uploadPreparedDocuments', () => {
  it('uploads each document against the employee id and reports success', async () => {
    const spy = vi.spyOn(docsApi, 'uploadEmployeeDocument').mockResolvedValue({} as never)
    const results = await uploadPreparedDocuments('emp-1', [doc({ key: 'a' }), doc({ key: 'b' })])
    expect(spy).toHaveBeenCalledTimes(2)
    expect(spy.mock.calls[0][0]).toBe('emp-1')
    expect(results.every((r) => r.ok)).toBe(true)
  })

  it('captures a failed upload as a result instead of throwing', async () => {
    vi.spyOn(docsApi, 'uploadEmployeeDocument').mockRejectedValue(new Error('boom'))
    const results = await uploadPreparedDocuments('emp-1', [doc({ key: 'a' })])
    expect(results[0].ok).toBe(false)
    expect(results[0].error).toContain('boom')
  })
})

describe('createPreparedIssuedItems', () => {
  it('creates each issuance with status Issued against the employee id', async () => {
    const spy = vi.spyOn(itemsApi, 'saveEmployeeIssuedItem').mockResolvedValue({} as never)
    const results = await createPreparedIssuedItems('emp-1', [item()])
    expect(spy).toHaveBeenCalledWith('emp-1', null, expect.objectContaining({ status: 'Issued', quantity: 1 }))
    expect(results[0].ok).toBe(true)
  })

  it('captures a failed creation as a result instead of throwing', async () => {
    vi.spyOn(itemsApi, 'saveEmployeeIssuedItem').mockRejectedValue(new Error('nope'))
    const results = await createPreparedIssuedItems('emp-1', [item()])
    expect(results[0].ok).toBe(false)
  })
})

describe('runEmployeeCreateFollowUps', () => {
  it('returns a combined result set for documents and issued items', async () => {
    vi.spyOn(docsApi, 'uploadEmployeeDocument').mockResolvedValue({} as never)
    vi.spyOn(itemsApi, 'saveEmployeeIssuedItem').mockRejectedValue(new Error('x'))
    const results = await runEmployeeCreateFollowUps('emp-1', [doc()], [item()])
    expect(results).toHaveLength(2)
    expect(results.find((r) => r.kind === 'document')?.ok).toBe(true)
    expect(results.find((r) => r.kind === 'issued-item')?.ok).toBe(false)
  })
})
