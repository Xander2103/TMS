import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { InvoiceDetailPage } from '../pages/InvoiceDetailPage'
import type { InvoiceDetail } from '../types'

/**
 * UX-correctie 2 + 3: sending a draft is irreversible, so it needs an explicit summary dialog,
 * a double click must never send twice, and the customer-facing text/PDF preview is reachable
 * before anything is frozen.
 */

vi.mock('../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }),
}))

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

function draft(overrides: Partial<InvoiceDetail> = {}): InvoiceDetail {
  return {
    id: 'inv-1',
    invoiceNumber: 'FAC-2026080001',
    customerId: 'cust-1',
    customerName: 'Distri-Frais SPRL',
    customerVatNumber: null,
    invoiceDate: '2026-08-28',
    dueDate: '2026-09-27',
    status: 'Draft',
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
    languageCode: 'fr',
    customerVatTreatment: 'DomesticVat',
    vatLegalText: null,
    allowedTransitions: ['Sent', 'Cancelled'],
    lines: [
      {
        id: 'l1',
        sequence: 1,
        transportOrderId: null,
        orderNumber: null,
        description: 'Administratieve kost',
        customerDescription: 'Frais administratifs',
        quantity: 1,
        unitPrice: 25,
        vatRatePercent: 21,
        lineTotal: 25,
        salesCategoryId: 'sc-1',
        salesCategoryName: 'Administratieve kost',
        ledgerAccountNumber: null,
        ledgerAccountName: null,
        ledgerWarning: null,
        unitCode: 'C62',
      },
    ],
    ...overrides,
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

describe('InvoiceDetailPage — verzenden en voorbeeld', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    api.getInvoice.mockResolvedValue(draft())
    api.fetchInvoicePdfUrl.mockResolvedValue('blob:preview')
  })

  it('shows the customer-facing (French) text of a draft line before it is sent', async () => {
    renderPage()
    expect(await screen.findByText('Frais administratifs')).toBeInTheDocument()
    // The internal wording stays visible underneath so the operator can still recognise it.
    expect(screen.getAllByText('Administratieve kost').length).toBeGreaterThan(0)
  })

  it('opens a summary dialog on Verzenden and only sends after explicit confirmation', async () => {
    const user = userEvent.setup()
    let resolveSend: (value: InvoiceDetail) => void = () => {}
    api.changeInvoiceStatus.mockImplementation(
      () => new Promise<InvoiceDetail>((resolve) => { resolveSend = resolve }),
    )
    renderPage()
    await screen.findByText('Frais administratifs')

    await user.click(screen.getByRole('button', { name: 'Verzenden' }))

    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByRole('heading', { name: 'Factuur verzenden' })).toBeInTheDocument()
    const summary = within(dialog).getByTestId('invoice-send-summary')
    expect(summary).toHaveTextContent('FAC-2026080001')
    expect(summary).toHaveTextContent('Distri-Frais SPRL')
    expect(summary).toHaveTextContent('VCB')
    expect(summary).toHaveTextContent('Frans')
    expect(summary).toHaveTextContent('Binnenlandse BTW')
    expect(summary).toHaveTextContent('30,25')
    expect(api.changeInvoiceStatus).not.toHaveBeenCalled()

    // Double click on the confirm button: exactly one status change.
    const confirm = within(dialog).getByTestId('invoice-send-confirm')
    await user.click(confirm)
    await user.click(confirm)
    expect(api.changeInvoiceStatus).toHaveBeenCalledTimes(1)
    expect(api.changeInvoiceStatus).toHaveBeenCalledWith('inv-1', 'Sent')

    resolveSend(draft({ status: 'Sent', allowedTransitions: ['Paid', 'Cancelled'] }))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  })

  it('keeps the draft untouched when the dialog is cancelled', async () => {
    const user = userEvent.setup()
    renderPage()
    await screen.findByText('Frais administratifs')
    await user.click(screen.getByRole('button', { name: 'Verzenden' }))
    const dialog = await screen.findByRole('dialog')
    await user.click(within(dialog).getByRole('button', { name: 'Annuleren' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(api.changeInvoiceStatus).not.toHaveBeenCalled()
  })

  it('offers "Creditnota maken" on a sent invoice and shows the relation both ways', async () => {
    const user = userEvent.setup()
    api.getInvoice.mockResolvedValue(draft({ status: 'Sent', allowedTransitions: ['Paid', 'Cancelled'], kind: 'Invoice' }))
    api.createCreditNote.mockResolvedValue(draft({ id: 'cn-1', invoiceNumber: 'CN-2026080001', kind: 'CreditNote', creditedInvoiceId: 'inv-1', creditedInvoiceNumber: 'FAC-2026080001' }))
    renderPage()
    await screen.findByText('Frais administratifs')

    // A sent invoice is never editable again: no Bewerken, and Verzenden is gone.
    expect(screen.queryByRole('button', { name: 'Bewerken' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Verzenden' })).not.toBeInTheDocument()
    // H-06: cancelling a sent invoice is not offered at all — even when a stale allowedTransitions
    // still carries it — omdat de server het weigert; de creditnota is de enige correctieweg.
    expect(screen.queryByRole('button', { name: 'Factuur annuleren' })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Creditnota maken' })).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Creditnota maken' }))
    await waitFor(() => expect(api.createCreditNote).toHaveBeenCalledWith('inv-1'))
    expect(api.changeInvoiceStatus).not.toHaveBeenCalled()
  })

  it('still offers cancelling a draft invoice', async () => {
    api.getInvoice.mockResolvedValue(draft({ allowedTransitions: ['Sent', 'Cancelled'] }))
    renderPage()
    await screen.findByText('Frais administratifs')
    expect(screen.getByRole('button', { name: 'Factuur annuleren' })).toBeInTheDocument()
  })

  it('shows which credit notes exist on an invoice, and which invoice a credit note credits', async () => {
    api.getInvoice.mockResolvedValue(draft({
      status: 'Sent',
      allowedTransitions: ['Paid', 'Cancelled'],
      creditNotes: [{ id: 'cn-1', invoiceNumber: 'CN-2026080001', status: 'Draft' }],
    }))
    renderPage()
    const relations = await screen.findByTestId('invoice-relations')
    expect(relations).toHaveTextContent("Creditnota's op deze factuur")
    expect(relations).toHaveTextContent('CN-2026080001')
    // With an open credit note the button is not offered a second time.
    expect(screen.queryByRole('button', { name: 'Creditnota maken' })).not.toBeInTheDocument()
  })

  it('opens the PDF preview without changing the invoice', async () => {
    const user = userEvent.setup()
    const open = vi.spyOn(window, 'open').mockImplementation(() => null)
    renderPage()
    await screen.findByText('Frais administratifs')

    await user.click(screen.getByRole('button', { name: 'Voorbeeld (pdf)' }))

    await waitFor(() => expect(api.fetchInvoicePdfUrl).toHaveBeenCalledWith('inv-1'))
    expect(open).toHaveBeenCalledWith('blob:preview', '_blank', 'noopener')
    expect(api.changeInvoiceStatus).not.toHaveBeenCalled()
    open.mockRestore()
  })
})
