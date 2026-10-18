import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { ActivityCard } from '../components/ActivityCard'
import { dossierActivity, dossierDetail } from './fixtures'

const prices = vi.hoisted(() => ({ visible: true }))
vi.mock('../../auth/commercialVisibility', () => ({ useCanViewPrices: () => prices.visible }))

// ACC-M12: the reverse link — an invoiced unit links to the invoice that carries it; a unit that
// is not invoiced shows no invoice row at all (never an empty label).

function renderCard(overrides: Parameters<typeof dossierActivity>[0]) {
  render(
    <MemoryRouter>
      <ul>
        <ActivityCard activity={dossierActivity(overrides)} activities={[]} dossier={dossierDetail({ dossierNumber: 'DOS-0059' })} onOpen={vi.fn()} />
      </ul>
    </MemoryRouter>,
  )
  return within(screen.getByRole('listitem'))
}

beforeEach(() => {
  prices.visible = true
})

describe('ActivityCard invoice link', () => {
  it('links to the invoice with its number and status once the unit is invoiced', () => {
    const card = renderCard({ agreedPrice: 400, isPriced: true, invoiceId: 'inv-6', invoiceNumber: 'FAC-2026100006', invoiceStatus: 'Sent' })
    expect(card.getByText('Factuur')).toBeInTheDocument()
    expect(card.getByRole('link', { name: 'FAC-2026100006' })).toHaveAttribute('href', '/invoices/inv-6')
    expect(card.getByText(/— Verzonden/)).toBeInTheDocument()
  })

  it('names a draft invoice "Concept" when it has no legal number yet', () => {
    const card = renderCard({ agreedPrice: 400, isPriced: true, invoiceId: 'inv-7', invoiceNumber: null, invoiceStatus: 'Draft' })
    expect(card.getByRole('link', { name: 'Concept' })).toHaveAttribute('href', '/invoices/inv-7')
  })

  it('shows no invoice row for a unit that is not invoiced', () => {
    const card = renderCard({ agreedPrice: 400, isPriced: true })
    expect(card.queryByText('Factuur')).not.toBeInTheDocument()
  })
})

describe('ActivityCard without a price right (SEC-M1)', () => {
  it('leaves out price, price status and invoice — no "Niet geprijsd" for a stripped price', () => {
    prices.visible = false
    // What the API sends a Dispatcher: amounts and invoice fields stripped, operational data kept.
    const card = renderCard({ agreedPrice: null, priceLines: [], invoiceId: null, invoiceNumber: null, invoiceStatus: null })
    expect(card.queryByText('Prijs')).not.toBeInTheDocument()
    expect(card.queryByText('Prijsstatus')).not.toBeInTheDocument()
    expect(card.queryByText('Factuur')).not.toBeInTheDocument()
    expect(card.queryByText(/Niet geprijsd/)).not.toBeInTheDocument()
    expect(card.queryByText(/€/)).not.toBeInTheDocument()
    // The operational facts stay.
    expect(card.getByText('Chauffeur')).toBeInTheDocument()
  })
})
