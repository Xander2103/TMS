import { describe, expect, it, beforeEach, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { OrderImportsPage } from '../OrderImportsPage'
import type { OrderImportBatch } from '../../api/orderImportsApi'

const auth = vi.hoisted(() => ({ permissions: new Set<string>() }))
vi.mock('../../../auth/authContextValue', () => ({
  useAuth: () => ({ hasPermission: (code: string) => auth.permissions.has(code) }),
}))

vi.mock('../../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }),
}))

const api = vi.hoisted(() => ({
  batches: [] as OrderImportBatch[],
}))

vi.mock('../../api/orderImportsApi', () => ({
  listOrderImportProfiles: vi.fn(() =>
    Promise.resolve([
      { id: 'p1', name: 'Generiek v1', description: null, mappingJson: '{}', isActive: true },
    ]),
  ),
  listOrderImportBatches: vi.fn(() =>
    Promise.resolve({ items: api.batches, totalCount: api.batches.length, page: 1, pageSize: 25 }),
  ),
  getOrderImportBatch: vi.fn(),
  uploadOrderImport: vi.fn(),
}))

vi.mock('../../../customers/api/customersApi', () => ({
  searchCustomers: vi.fn(() => Promise.resolve({ items: [], totalCount: 0, page: 1, pageSize: 20 })),
}))

function batch(overrides: Partial<OrderImportBatch>): OrderImportBatch {
  return {
    id: 'b1',
    profileId: 'p1',
    profileName: 'Generiek v1',
    customerId: 'c1',
    customerName: 'Haven BV',
    fileName: 'orders.xlsx',
    status: 'Processed',
    rowCount: 3,
    successCount: 2,
    failureCount: 1,
    dryRun: false,
    createdAt: '2026-08-12T10:00:00Z',
    ...overrides,
  }
}

function renderPage() {
  return render(
    <MemoryRouter initialEntries={['/order-imports']}>
      <OrderImportsPage />
    </MemoryRouter>,
  )
}

describe('OrderImportsPage', () => {
  beforeEach(() => {
    auth.permissions = new Set()
    api.batches = []
  })

  it('shows a permission placeholder without order permissions', () => {
    renderPage()
    expect(screen.getByText('Je hebt geen rechten om opdrachtimporten te bekijken.')).toBeInTheDocument()
  })

  it('renders the batch history from the api', async () => {
    auth.permissions = new Set(['orders.view', 'orders.create'])
    api.batches = [
      batch({ id: 'b1', fileName: 'orders.xlsx', status: 'Processed' }),
      batch({ id: 'b2', fileName: 'proef.xlsx', status: 'Validated', dryRun: true, failureCount: 0 }),
    ]
    renderPage()

    expect(await screen.findByText('orders.xlsx')).toBeInTheDocument()
    expect(screen.getByText('proef.xlsx')).toBeInTheDocument()
    expect(screen.getByText('Verwerkt')).toBeInTheDocument()
    expect(screen.getByText('Gevalideerd (proef)')).toBeInTheDocument()
    expect(screen.getByText('2 ok / 1 fout / 3 totaal')).toBeInTheDocument()
    // Upload form is present for orders.create.
    expect(screen.getByLabelText('Enkel valideren (proefdraaien)')).toBeInTheDocument()
  })
})
