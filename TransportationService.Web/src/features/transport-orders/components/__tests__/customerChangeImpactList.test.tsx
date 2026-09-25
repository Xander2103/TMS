import { describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { CustomerChangeImpactList } from '../CustomerChangeImpactList'
import type { OrderCustomerChangeImpact } from '../../api/transportOrdersApi'

function impact(overrides: Partial<OrderCustomerChangeImpact> = {}): OrderCustomerChangeImpact {
  return {
    orderId: 'order-1', orderNumber: 'ORD-1',
    currentCustomerId: 'cust-tmp', currentCustomerName: 'VCB tijdelijk',
    newCustomerId: 'cust-real', newCustomerName: 'Client SA',
    blockedReason: null,
    automaticLinesInvalidated: 0, manualLinesKept: 0, adjustedLinesFlaggedForReview: 0, needsPricingReview: false,
    newLegalEntityId: 'ent-b', legalEntityChanges: false, newInvoiceLanguage: null, newVatTreatment: null,
    stopsKept: 1, goodsKept: 1, documentsKept: 2, draftInvoiceLinesReleased: 0,
    owningDossierId: null, owningDossierNumber: null,
    documentsPublicationWithdrawn: 0,
    ...overrides,
  }
}

/** Closure sprint P0: the preview must say that published documents go back to internal. */
describe('CustomerChangeImpactList — document publication', () => {
  it('warns how many published documents are withdrawn from the customer portal', () => {
    render(<MemoryRouter><CustomerChangeImpactList impact={impact({ documentsPublicationWithdrawn: 2 })} /></MemoryRouter>)

    const line = screen.getByText(/2 klantzichtbare document/i)
    expect(line).toHaveClass('to-impact-warning')
    expect(line.textContent).toMatch(/opnieuw/i)
  })

  it('stays silent when nothing was published', () => {
    render(<MemoryRouter><CustomerChangeImpactList impact={impact()} /></MemoryRouter>)

    expect(screen.queryByText(/klantzichtbare document/i)).toBeNull()
  })
})
