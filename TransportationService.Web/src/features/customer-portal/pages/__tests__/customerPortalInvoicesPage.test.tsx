import { describe, expect, it, vi, beforeEach } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { CustomerPortalInvoicesPage } from '../CustomerPortalInvoicesPage'
import type { PortalInvoiceListItem } from '../../api/customerPortalApi'

const api = vi.hoisted(() => ({
  listPortalInvoices: vi.fn(),
}))
vi.mock('../../api/customerPortalApi', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../api/customerPortalApi')>()),
  listPortalInvoices: api.listPortalInvoices,
}))

function invoice(overrides: Partial<PortalInvoiceListItem> = {}): PortalInvoiceListItem {
  return {
    id: 'inv-1',
    invoiceNumber: 'F2026-0001',
    invoiceDate: '2026-07-01',
    dueDate: '2026-07-31',
    status: 'Sent',
    total: 1210,
    currency: 'EUR',
    peppolStatus: null,
    kind: 'Invoice',
    ...overrides,
  }
}

function renderPage() {
  return render(
    <MemoryRouter>
      <CustomerPortalInvoicesPage />
    </MemoryRouter>,
  )
}

describe('CustomerPortalInvoicesPage', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('shows the Creditnota label for a credit note', async () => {
    api.listPortalInvoices.mockResolvedValue([
      invoice(),
      invoice({ id: 'inv-2', invoiceNumber: 'CN2026-0001', kind: 'CreditNote' }),
    ])
    renderPage()

    expect(await screen.findByText('CN2026-0001')).toBeInTheDocument()
    expect(screen.getByText('Creditnota')).toBeInTheDocument()
  })

  it('shows the Dutch Peppol status when provided and a dash otherwise', async () => {
    api.listPortalInvoices.mockResolvedValue([
      invoice({ peppolStatus: 'Delivered' }),
      invoice({ id: 'inv-2', invoiceNumber: 'F2026-0002', peppolStatus: null }),
    ])
    renderPage()

    expect(await screen.findByText('Afgeleverd')).toBeInTheDocument()
    expect(screen.getByText('—')).toBeInTheDocument()
  })
})
