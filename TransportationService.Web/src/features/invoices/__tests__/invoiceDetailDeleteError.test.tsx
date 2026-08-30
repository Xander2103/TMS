import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { ApiError } from '../../../api/apiClient'
import { InvoiceDetailPage } from '../pages/InvoiceDetailPage'
import type { InvoiceDetail } from '../types'

/**
 * Wave 1 fix A (A10) — H-06 gave deletion a real refusal ("Deze factuur is ooit verzonden en kan
 * niet verwijderd worden; ze blijft bewaard als historisch document"), but `handleDelete` was the
 * one handler on this page that swallowed the error and showed a generic sentence. The result was
 * a Verwijderen button that failed with no reason on exactly the documents where the reason is the
 * whole point. Every sibling handler already used `localizeApiError`.
 */

const toast = vi.hoisted(() => ({ showError: vi.fn(), showSuccess: vi.fn(), showToast: vi.fn() }))
vi.mock('../../../components/ui/toastContext', () => ({ useToast: () => toast }))

vi.mock('../../auth/authContextValue', () => ({
  useAuth: () => ({
    status: 'authenticated' as const,
    user: { id: 'u1', firstName: 'Ada', lastName: 'Byron', tenantName: 'Acme' },
    login: vi.fn(),
    logout: vi.fn(),
    hasPermission: () => true,
    hasAnyPermission: () => true,
  }),
}))

vi.mock('../components/InvoicePeppolPanel', () => ({ InvoicePeppolPanel: () => null }))
vi.mock('../../accounting/api/accountingApi', () => ({ listSalesCategories: () => Promise.resolve([]) }))
vi.mock('../api/invoiceAttachmentsApi', () => ({
  INVOICE_ATTACHMENT_ACCEPT: '.pdf',
  MAX_INVOICE_ATTACHMENT_BYTES: 1,
  listInvoiceAttachments: () => Promise.resolve([]),
  updateInvoiceAttachment: vi.fn(),
  deleteInvoiceAttachment: vi.fn(),
  uploadInvoiceAttachment: vi.fn(),
  downloadInvoiceAttachment: vi.fn(),
}))

const api = vi.hoisted(() => ({
  getInvoice: vi.fn(),
  changeInvoiceStatus: vi.fn(),
  createCreditNote: vi.fn(),
  fetchInvoicePdfUrl: vi.fn(),
  completeInvoiceLedgerSnapshots: vi.fn(),
  deleteInvoice: vi.fn(),
  overrideInvoiceNumber: vi.fn(),
  updateInvoice: vi.fn(),
}))
vi.mock('../api/invoicesApi', () => api)

const REFUSAL =
  'Deze factuur is ooit verzonden en kan niet verwijderd worden; ze blijft bewaard als historisch document.'

function cancelledInvoice(): InvoiceDetail {
  return {
    id: 'inv-1',
    invoiceNumber: 'FAC-2026080001',
    customerId: 'cust-1',
    customerName: 'Distri-Frais SPRL',
    customerVatNumber: null,
    invoiceDate: '2026-08-28',
    dueDate: '2026-09-27',
    status: 'Cancelled',
    kind: 'Invoice',
    currency: 'EUR',
    subtotal: 25,
    vatAmount: 5.25,
    total: 30.25,
    notes: null,
    purchaseOrderNumber: null,
    invoicePeriodYear: 2026,
    invoicePeriodMonth: 8,
    legalEntityId: 'le-1',
    legalEntityName: 'VCB',
    numberIsManual: false,
    languageCode: 'nl',
    customerVatTreatment: 'DomesticVat',
    vatLegalText: null,
    allowedTransitions: [],
    lines: [
      {
        id: 'l1', sequence: 1, transportOrderId: null, orderNumber: null,
        description: 'Administratieve kost', customerDescription: 'Administratieve kost',
        quantity: 1, unitPrice: 25, vatRatePercent: 21, lineTotal: 25,
        salesCategoryId: null, salesCategoryName: null,
        ledgerAccountNumber: '700400', ledgerAccountName: 'Verkoop', ledgerWarning: null, unitCode: 'C62',
      },
    ],
  } as unknown as InvoiceDetail
}

function renderPage() {
  return render(
    <MemoryRouter initialEntries={['/invoices/inv-1']}>
      <Routes>
        <Route path="/invoices/:id" element={<InvoiceDetailPage />} />
      </Routes>
    </MemoryRouter>,
  )
}

describe('InvoiceDetailPage — verwijderen', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    api.getInvoice.mockResolvedValue(cancelledInvoice())
    api.fetchInvoicePdfUrl.mockResolvedValue('blob:preview')
  })

  it('shows the server reason when the deletion is refused', async () => {
    const user = userEvent.setup()
    api.deleteInvoice.mockRejectedValue(new ApiError(REFUSAL, 400, { detail: REFUSAL }))
    renderPage()
    await screen.findByText('FAC-2026080001')

    await user.click(screen.getByRole('button', { name: 'Verwijderen' }))
    const dialog = await screen.findByRole('dialog')
    await user.click(within(dialog).getByRole('button', { name: 'Verwijderen' }))

    await waitFor(() => expect(api.deleteInvoice).toHaveBeenCalledWith('inv-1'))
    expect(toast.showError).toHaveBeenCalledWith(REFUSAL)
  })
})

