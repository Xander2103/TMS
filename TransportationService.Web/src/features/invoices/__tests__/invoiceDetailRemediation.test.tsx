import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, MemoryRouter, Route, RouterProvider, Routes } from 'react-router-dom'
import { ApiError } from '../../../api/apiClient'
import { InvoiceDetailPage } from '../pages/InvoiceDetailPage'
import type { InvoiceDetail } from '../types'

/**
 * Accounting remediation on the invoice detail page:
 * ACC-M4 preflight + server reasons, ACC-M5 unsaved guard, ACC-M8 action visibility and 404,
 * ACC-M11 credit-note presentation, ACC-M2 precision, ACC-H1 VAT default, ACC-H5 due date,
 * ACC-H3 draft reference.
 */

const toast = vi.hoisted(() => ({ showError: vi.fn(), showSuccess: vi.fn(), showToast: vi.fn() }))
vi.mock('../../../components/ui/toastContext', () => ({ useToast: () => toast }))

const auth = vi.hoisted(() => ({ permissions: new Set<string>(['*']) }))
vi.mock('../../auth/authContextValue', () => ({
  useAuth: () => ({
    status: 'authenticated' as const,
    user: { id: 'u1', firstName: 'Ada', lastName: 'Byron', tenantName: 'Acme' },
    login: vi.fn(),
    logout: vi.fn(),
    hasPermission: (code: string) => auth.permissions.has('*') || auth.permissions.has(code),
    hasAnyPermission: (codes: string[]) => auth.permissions.has('*') || codes.some((code) => auth.permissions.has(code)),
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
  getInvoiceIssuePreflight: vi.fn(),
  getInvoiceHistory: vi.fn(),
  getDefaultVatRate: vi.fn(),
}))
vi.mock('../api/invoicesApi', () => api)

function invoice(overrides: Partial<InvoiceDetail> = {}): InvoiceDetail {
  return {
    id: 'inv-1',
    invoiceNumber: 'FAC-2026100006',
    draftReference: 'CONCEPT-A3FC',
    displayNumber: 'FAC-2026100006',
    defaultVatRatePercent: 6,
    paymentTermDays: 45,
    customerId: 'cust-1',
    customerName: 'TEST ACCOUNTING Klant',
    customerVatNumber: 'BE0999999922',
    invoiceDate: '2026-10-17',
    dueDate: '2026-12-01',
    status: 'Draft',
    kind: 'Invoice',
    currency: 'EUR',
    subtotal: 300.83,
    vatAmount: 63.17,
    total: 364,
    notes: null,
    purchaseOrderNumber: null,
    invoicePeriodYear: 2026,
    invoicePeriodMonth: 10,
    legalEntityId: 'le-1',
    legalEntityName: 'Development Transportbedrijf',
    numberIsManual: false,
    languageCode: 'nl',
    customerVatTreatment: 'DomesticVat',
    vatLegalText: null,
    allowedTransitions: ['Sent', 'Cancelled'],
    lines: [
      {
        id: 'l1',
        sequence: 1,
        transportOrderId: null,
        orderNumber: null,
        dossierActivityId: 'act-1',
        dossierNumber: 'DOS-0059',
        description: 'Hijswerk airco-unit',
        customerDescription: null,
        quantity: 2.5,
        unitPrice: 120.33,
        unitLabel: 'uur',
        vatRatePercent: 21,
        lineTotal: 300.83,
        salesCategoryId: null,
        salesCategoryName: null,
        ledgerAccountNumber: null,
        ledgerAccountName: null,
        ledgerWarning: null,
      },
    ],
    ...overrides,
  }
}

function renderPage() {
  return render(
    <MemoryRouter initialEntries={['/invoices/inv-1']}>
      <Routes>
        <Route path="/invoices/:id" element={<InvoiceDetailPage />} />
        <Route path="/invoices" element={<p>Facturenlijst</p>} />
      </Routes>
    </MemoryRouter>,
  )
}

describe('InvoiceDetailPage — accounting remediation', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    auth.permissions = new Set(['*'])
    api.getInvoice.mockResolvedValue(invoice())
    api.getInvoiceHistory.mockResolvedValue([])
    api.getInvoiceIssuePreflight.mockResolvedValue({ canIssue: true, issues: [] })
    api.getDefaultVatRate.mockResolvedValue({ vatRatePercent: 6 })
  })

  it('ACC-M4: the send dialog lists the preflight blockers in words and disables the confirm', async () => {
    const user = userEvent.setup()
    api.getInvoiceIssuePreflight.mockResolvedValue({
      canIssue: false,
      issues: [
        { code: 'po.required', message: 'Deze klant vereist een PO-nummer.', lineIds: [] },
        { code: 'lines.category_missing', message: 'Lijn zonder verkoopcategorie.', lineIds: ['l1'] },
      ],
    })
    renderPage()
    await user.click(await screen.findByRole('button', { name: 'Verzenden' }))
    const dialog = await screen.findByRole('dialog')
    const list = await within(dialog).findByTestId('invoice-issue-preflight')
    expect(list).toHaveTextContent('PO-nummer ontbreekt: deze klant vereist een PO-nummer')
    expect(list).toHaveTextContent('Verkoopcategorie ontbreekt op een lijn')
    expect(list).toHaveTextContent('lijn 1')
    expect(within(dialog).getByTestId('invoice-send-confirm')).toBeDisabled()
    expect(api.changeInvoiceStatus).not.toHaveBeenCalled()
  })

  it('ACC-M4: a refused status change shows the server reason, not the generic sentence', async () => {
    const user = userEvent.setup()
    api.changeInvoiceStatus.mockRejectedValue(new ApiError('Deze klant vereist een PO-nummer. Voeg een geldig PO-nummer toe voor verzending.', 400))
    renderPage()
    await user.click(await screen.findByRole('button', { name: 'Verzenden' }))
    const dialog = await screen.findByRole('dialog')
    await within(dialog).findByText(/Alle controles zijn in orde/)
    await user.click(within(dialog).getByTestId('invoice-send-confirm'))
    await waitFor(() => expect(toast.showError).toHaveBeenCalledWith('Deze klant vereist een PO-nummer. Voeg een geldig PO-nummer toe voor verzending.'))
    expect(toast.showError).not.toHaveBeenCalledWith('De status kon niet worden gewijzigd.')
  })

  it('ACC-M5: leaving edit mode with changes asks first; untouched edits do not', async () => {
    const user = userEvent.setup()
    const router = createMemoryRouter(
      [
        { path: '/invoices/:id', element: <InvoiceDetailPage /> },
        { path: '/invoices', element: <p>Facturenlijst</p> },
      ],
      { initialEntries: ['/invoices/inv-1'] },
    )
    render(<RouterProvider router={router} />)
    await user.click(await screen.findByRole('button', { name: 'Bewerken' }))
    // Nothing changed yet: navigating away is silent.
    await user.click(screen.getByRole('link', { name: 'Facturen' }))
    expect(screen.queryByText('Niet-opgeslagen wijzigingen')).not.toBeInTheDocument()
    expect(await screen.findByText('Facturenlijst')).toBeInTheDocument()

    await router.navigate('/invoices/inv-1')
    await user.click(await screen.findByRole('button', { name: 'Bewerken' }))
    const quantity = screen.getByLabelText('Hoeveelheid')
    await user.clear(quantity)
    await user.type(quantity, '3')
    await user.click(screen.getByRole('link', { name: 'Facturen' }))
    expect(await screen.findByText('Niet-opgeslagen wijzigingen')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Blijven bewerken' }))
    expect(screen.queryByText('Facturenlijst')).not.toBeInTheDocument()
  })

  it('ACC-M8: a read-only role sees no mutating actions', async () => {
    auth.permissions = new Set(['invoices.view', 'peppol.view', 'accounting.view', 'invoice_attachments.view'])
    api.getInvoice.mockResolvedValue(invoice({ status: 'Sent', allowedTransitions: ['Paid'], lines: [{ ...invoice().lines[0], ledgerAccountNumber: null }] }))
    renderPage()
    await screen.findByText(/Hijswerk airco-unit/)
    for (const name of ['Markeer als betaald', 'Verzenden', 'Factuur annuleren', 'Creditnota maken', 'Bewerken', 'Verwijderen', 'Grootboekgegevens aanvullen', 'Nummer corrigeren']) {
      expect(screen.queryByRole('button', { name })).not.toBeInTheDocument()
    }
    expect(screen.getByRole('button', { name: 'Voorbeeld (pdf)' })).toBeInTheDocument()
  })

  it('ACC-M8: an unknown invoice says "niet gevonden" with the way back', async () => {
    api.getInvoice.mockRejectedValue(new ApiError('Not found', 404))
    renderPage()
    expect(await screen.findByText('Factuur niet gevonden.')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: /terug/i })).toHaveAttribute('href', '/invoices')
    expect(screen.queryByRole('button', { name: /opnieuw/i })).not.toBeInTheDocument()
  })

  it('ACC-M11: a credit note reads as a credit document', async () => {
    api.getInvoice.mockResolvedValue(invoice({
      kind: 'CreditNote', invoiceNumber: 'FAC-CN2026100010', displayNumber: 'FAC-CN2026100010', status: 'Sent',
      creditedInvoiceId: 'inv-0', creditedInvoiceNumber: 'FAC-2026100006', creditReason: 'Dubbel gefactureerd', allowedTransitions: ['Paid'],
    }))
    renderPage()
    expect(await screen.findByRole('heading', { name: /Creditnota FAC-CN2026100010/ })).toBeInTheDocument()
    expect(screen.getByText(/crediteert FAC-2026100006/)).toBeInTheDocument()
    expect(screen.queryByText(/vervalt/)).not.toBeInTheDocument()
    expect(screen.getByTestId('invoice-total')).toHaveTextContent('− € 364,00')
    expect(screen.getByText('Te crediteren (incl. btw)')).toBeInTheDocument()
    expect(screen.getByTestId('invoice-credit-reason')).toHaveTextContent('Dubbel gefactureerd')
    expect(screen.queryByRole('button', { name: 'Markeer als betaald' })).not.toBeInTheDocument()
  })

  it('ACC-M2: unit prices keep their decimals and the unit is shown; inputs accept 4 decimals', async () => {
    const user = userEvent.setup()
    api.getInvoice.mockResolvedValue(invoice({ lines: [{ ...invoice().lines[0], quantity: 3, unitPrice: 0.335, lineTotal: 1.01 }] }))
    renderPage()
    expect(await screen.findByText('€ 0,335')).toBeInTheDocument()
    expect(screen.getByText('uur')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Bewerken' }))
    expect(screen.getByLabelText('Hoeveelheid')).toHaveAttribute('step', '0.001')
    expect(screen.getByLabelText('Eenheidsprijs')).toHaveAttribute('step', '0.0001')
    expect(screen.getByLabelText('Eenheidsprijs')).toHaveValue(0.335)
  })

  it('ACC-H1: a new manual line defaults its VAT to the invoice default, never 0', async () => {
    const user = userEvent.setup()
    renderPage()
    await user.click(await screen.findByRole('button', { name: 'Bewerken' }))
    await user.click(screen.getByRole('button', { name: '+ Lijn toevoegen' }))
    const vatInputs = screen.getAllByLabelText('Btw %')
    expect(vatInputs[vatInputs.length - 1]).toHaveValue(6)
    expect(screen.getByText('Standaard btw-tarief van deze factuur: 6%.')).toBeInTheDocument()
  })

  it('ACC-H5: changing the invoice date recalculates the due date with the payment term', async () => {
    const user = userEvent.setup()
    renderPage()
    await user.click(await screen.findByRole('button', { name: 'Bewerken' }))
    const invoiceDate = screen.getByLabelText('Factuurdatum')
    const dueDate = screen.getByLabelText('Vervaldag')
    expect(dueDate).toHaveValue('2026-12-01')
    await user.clear(invoiceDate)
    await user.type(invoiceDate, '2026-10-31')
    // 31/10 + 45 days = 15/12 (month boundary crossed twice).
    expect(dueDate).toHaveValue('2026-12-15')
    expect(screen.getByTestId('due-date-recalculated')).toHaveTextContent('45 dagen')
    // A hand-edited due date is respected afterwards.
    await user.clear(dueDate)
    await user.type(dueDate, '2026-12-20')
    await user.clear(invoiceDate)
    await user.type(invoiceDate, '2026-10-20')
    expect(dueDate).toHaveValue('2026-12-20')
  })

  it('ACC-H3: a draft without a legal number shows its draft reference and the issue hint', async () => {
    api.getInvoice.mockResolvedValue(invoice({ invoiceNumber: null, displayNumber: 'CONCEPT-A3FC' }))
    renderPage()
    expect(await screen.findByRole('heading', { name: /CONCEPT-A3FC — TEST ACCOUNTING Klant/ })).toBeInTheDocument()
    expect(screen.getByText(/Het factuurnummer wordt toegekend bij het verzenden/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Handmatig nummer bij verzenden' })).toBeInTheDocument()
  })

  it('shows manual payment registration details on a paid invoice', async () => {
    api.getInvoice.mockResolvedValue(invoice({ status: 'Paid', allowedTransitions: [], paidAt: '2026-10-17T15:05:36Z', paidByName: 'Bea Boekhouding' }))
    renderPage()
    const info = await screen.findByTestId('invoice-paid-info')
    expect(info).toHaveTextContent('Betaald gemarkeerd op')
    expect(info).toHaveTextContent('door Bea Boekhouding')
    expect(info).toHaveTextContent('Handmatig geregistreerd')
  })
})
