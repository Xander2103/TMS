import { describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import { InvoiceFiscalSummary, InvoiceLineFiscalBadge } from '../components/InvoiceFiscalSummary'
import { isLineFiscalException } from '../utils/invoiceFiscal'
import type { InvoiceDetail, InvoiceLine } from '../types'

function line(overrides: Partial<InvoiceLine> = {}): InvoiceLine {
  return {
    id: 'l1', sequence: 1, transportOrderId: null, orderNumber: null, description: 'Transport', quantity: 1, unitPrice: 100,
    vatRatePercent: 21, lineTotal: 100, salesCategoryId: null, salesCategoryName: null, ledgerAccountNumber: null,
    ledgerAccountName: null, ledgerWarning: null, vatTreatment: 'DomesticVat', vatTreatmentSource: 'Customer',
    vatLegalText: null, salesCode: null, ...overrides,
  }
}

function invoice(overrides: Partial<InvoiceDetail> = {}): InvoiceDetail {
  return {
    id: 'inv-1', invoiceNumber: 'FAC-1', invoiceDate: '2026-08-20', dueDate: '2026-09-20', customerId: 'c', customerName: 'Klant',
    customerVatNumber: null, status: 'Sent', currency: 'EUR', notes: null, purchaseOrderNumber: null, lines: [line()],
    subtotal: 100, vatAmount: 21, total: 121, allowedTransitions: [], legalEntityId: null, legalEntityName: null,
    invoicePeriodYear: 2026, invoicePeriodMonth: 8, numberIsManual: false,
    customerVatTreatment: 'DomesticVat', languageCode: 'fr', vatLegalText: null, ...overrides,
  }
}

describe('invoice fiscal source', () => {
  it('summarises treatment, language and frozen state at invoice level, counting the line exceptions', () => {
    const exception = line({ id: 'l2', vatTreatment: 'VatExempt', vatTreatmentSource: 'SalesCode', salesCode: 'SC-EX', vatLegalText: 'Vrijgesteld van btw.' })
    render(<InvoiceFiscalSummary invoice={invoice({ lines: [line(), exception] })} />)

    const summary = screen.getByTestId('invoice-fiscal-summary')
    expect(summary).toHaveTextContent('Btw-behandeling:')
    expect(summary).toHaveTextContent('(bron: klant)')
    expect(summary).toHaveTextContent('Factuurtaal: Frans')
    expect(summary).toHaveTextContent('Vastgelegd bij verzenden.')
    expect(summary).toHaveTextContent('1')
  })

  it('a draft says it is a preview that freezes on send, and prints the statutory text', () => {
    render(<InvoiceFiscalSummary invoice={invoice({ status: 'Draft', customerVatTreatment: 'ReverseCharge', vatLegalText: 'Btw verlegd.' })} />)
    const summary = screen.getByTestId('invoice-fiscal-summary')
    expect(summary).toHaveTextContent('Voorbeeld — wordt vastgelegd bij verzenden.')
    expect(summary).toHaveTextContent('Wettelijke vermelding: Btw verlegd.')
  })

  it('marks only lines whose treatment did not come from the customer', () => {
    const fromCustomer = line()
    const fromCode = line({ vatTreatment: 'VatExempt', vatTreatmentSource: 'SalesCode', salesCode: 'SC-EX' })
    const fromOverride = line({ vatTreatment: 'ReverseCharge', vatTreatmentSource: 'LineOverride' })
    expect(isLineFiscalException(fromCustomer)).toBe(false)
    expect(isLineFiscalException(fromCode)).toBe(true)
    expect(isLineFiscalException(fromOverride)).toBe(true)

    const { container } = render(<InvoiceLineFiscalBadge line={fromCustomer} />)
    expect(container).toBeEmptyDOMElement()

    render(<InvoiceLineFiscalBadge line={fromCode} />)
    expect(screen.getByText(/Afwijkend: .* via verkoopcode SC-EX/)).toBeInTheDocument()
  })
})
