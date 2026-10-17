import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom'
import { InvoicesPage } from '../pages/InvoicesPage'
import type { InvoiceListItem } from '../types'

// ACC-M10: the invoice list searches on more than number/customer, filters on paid state, invoice
// date and Peppol status, keeps every filter in the URL, and shows drafts as "Concept" without a
// legal number (ACC-H3).

vi.mock('../../auth/authContextValue', () => ({
  useAuth: () => ({
    hasPermission: () => true,
    hasAnyPermission: () => true,
  }),
}))
vi.mock('../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }),
}))
const api = vi.hoisted(() => ({ searchInvoices: vi.fn() }))
vi.mock('../api/invoicesApi', () => api)

function row(overrides: Partial<InvoiceListItem> = {}): InvoiceListItem {
  return {
    id: 'inv-1', invoiceNumber: 'FAC-2026100011', invoiceDate: '2026-10-17', dueDate: '2026-11-16',
    customerId: 'c1', customerName: 'TEST ACCOUNTING Klant', customerNumber: 'KL-0006', status: 'Sent',
    currency: 'EUR', subtotal: 580.5, vatAmount: 0, total: 580.5, lineCount: 2, peppolStatus: 'Delivered',
    ...overrides,
  }
}

function LocationProbe() {
  const location = useLocation()
  return <div data-testid="search">{location.search}</div>
}

function renderAt(path = '/invoices') {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="/invoices" element={<><InvoicesPage /><LocationProbe /></>} />
      </Routes>
    </MemoryRouter>,
  )
}

describe('InvoicesPage filters (ACC-M10)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    api.searchInvoices.mockResolvedValue({ items: [row(), row({ id: 'inv-2', invoiceNumber: '', displayNumber: 'Concept 9d7b5731', status: 'Draft', peppolStatus: null })], totalCount: 2, page: 1, pageSize: 25 })
  })

  it('reads the filters from the URL and sends them to the API', async () => {
    renderAt('/invoices?status=Sent&paid=unpaid&dateFrom=2026-10-01&dateTo=2026-10-31&peppolStatus=Delivered&search=KL-0006')
    await waitFor(() =>
      expect(api.searchInvoices).toHaveBeenCalledWith(
        expect.objectContaining({ search: 'KL-0006', status: 'Sent', paid: 'unpaid', dateFrom: '2026-10-01', dateTo: '2026-10-31', peppolStatus: 'Delivered' }),
      ),
    )
    expect(screen.getByLabelText('Betaald of onbetaald')).toHaveValue('unpaid')
    expect(screen.getByLabelText('Factuurdatum van')).toHaveValue('2026-10-01')
    expect(screen.getByLabelText('Peppol-status')).toHaveValue('Delivered')
    expect(screen.getByRole('button', { name: 'Filters wissen' })).toBeInTheDocument()
  })

  it('writes a changed filter to the URL and resets to page 1', async () => {
    const user = userEvent.setup()
    renderAt('/invoices?page=3')
    await screen.findByText('FAC-2026100011')
    await user.selectOptions(screen.getByLabelText('Betaald of onbetaald'), 'paid')
    await waitFor(() => expect(screen.getByTestId('search').textContent).toBe('?paid=paid'))
    await user.selectOptions(screen.getByLabelText('Peppol-status'), 'Failed')
    await waitFor(() => expect(screen.getByTestId('search').textContent).toBe('?paid=paid&peppolStatus=Failed'))
    await waitFor(() => expect(api.searchInvoices).toHaveBeenLastCalledWith(expect.objectContaining({ paid: 'paid', peppolStatus: 'Failed', page: 1 })))
  })

  it('ignores a malformed date in the URL instead of sending it', async () => {
    renderAt('/invoices?dateFrom=gisteren')
    await waitFor(() => expect(api.searchInvoices).toHaveBeenCalled())
    expect(api.searchInvoices.mock.calls[0][0].dateFrom).toBeUndefined()
  })

  it('shows the Peppol status column, the customer number and drafts as "Concept" with their draft reference', async () => {
    renderAt()
    await screen.findByRole('link', { name: 'FAC-2026100011' })
    // The badge in the row, not the option of the Peppol filter.
    expect(screen.getByText('Afgeleverd', { selector: '.ui-badge' })).toBeInTheDocument()
    expect(screen.getAllByText(/KL-0006/).length).toBeGreaterThan(0)
    const draft = screen.getByRole('link', { name: 'Concept 9d7b5731' })
    expect(draft).toHaveAttribute('href', '/invoices/inv-2')
    expect(screen.getAllByText('Concept').length).toBeGreaterThan(0)
    expect(screen.getByPlaceholderText(/factuurnummer, klant, klantnummer, dossier of opdracht/)).toBeInTheDocument()
  })
})
