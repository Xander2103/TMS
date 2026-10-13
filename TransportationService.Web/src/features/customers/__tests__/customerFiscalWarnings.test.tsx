import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { CustomerFiscalWarnings } from '../components/CustomerFiscalWarnings'

const api = vi.hoisted(() => ({ getCustomerFiscalWarnings: vi.fn() }))
vi.mock('../api/customersApi', async (orig) => ({
  ...(await orig<typeof import('../api/customersApi')>()),
  getCustomerFiscalWarnings: api.getCustomerFiscalWarnings,
}))

beforeEach(() => {
  api.getCustomerFiscalWarnings.mockReset()
})

describe('CustomerFiscalWarnings', () => {
  it('renders the advisory notices in operator language, with the customer country filled in', async () => {
    api.getCustomerFiscalWarnings.mockResolvedValue([
      { code: 'domestic-vat-foreign-customer', message: 'backend text' },
      { code: 'vat-number-missing', message: 'backend text' },
      { code: 'some-future-code', message: 'Onbekende melding uit de backend.' },
    ])
    render(<CustomerFiscalWarnings customerId="cust-1" countryCode="DE" />)

    await screen.findByText('Deze klant staat in DE maar wordt met binnenlandse btw gefactureerd. Controleer of dat klopt.')
    expect(screen.getByText('De gekozen btw-behandeling vereist een btw-nummer. Zonder nummer kan een factuur niet worden verzonden.')).toBeInTheDocument()
    // Unknown codes fall back to the backend wording instead of a raw key.
    expect(screen.getByText('Onbekende melding uit de backend.')).toBeInTheDocument()
    expect(screen.getByText('Deze meldingen wijzigen niets aan de btw-behandeling; ze wijzen op een instelling die het nazien waard is.')).toBeInTheDocument()
  })

  it('says so when there is nothing to review', async () => {
    api.getCustomerFiscalWarnings.mockResolvedValue([])
    render(<CustomerFiscalWarnings customerId="cust-1" countryCode="BE" />)
    await screen.findByText('Geen aandachtspunten.')
  })
})
