import { describe, expect, it, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { CustomerRateCardsPanel } from '../CustomerRateCardsPanel'
import type { RateCard } from '../../../tarification/types'

const auth = vi.hoisted(() => ({ permissions: new Set<string>() }))

vi.mock('../../../auth/authContextValue', () => ({
  useAuth: () => ({ hasPermission: (code: string) => auth.permissions.has(code) }),
}))
vi.mock('../../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }),
}))

const cards = vi.hoisted(() => ({ value: [] as RateCard[] }))

vi.mock('../../../tarification/api/rateCardsApi', () => ({
  listRateCards: () => Promise.resolve(cards.value),
  createRateCard: vi.fn(),
  updateRateCard: vi.fn(),
  deleteRateCard: vi.fn(),
}))

function makeCard(): RateCard {
  return {
    id: 'rc-1',
    customerId: 'cust-1',
    customerName: 'Acme',
    name: 'Standaard 2026',
    currency: 'EUR',
    effectiveFrom: '2026-01-01',
    effectiveUntil: null,
    baseAmount: 50,
    perKmRate: 1.2,
    perPalletRate: null,
    perTonRate: null,
    minimumAmount: 75,
    notes: null,
    surcharges: [{ id: 's-1', name: 'Brandstof', kind: 'Percent', value: 8 }],
  }
}

function renderPanel() {
  return render(
    <MemoryRouter>
      <CustomerRateCardsPanel customerId="cust-1" />
    </MemoryRouter>,
  )
}

describe('CustomerRateCardsPanel', () => {
  beforeEach(() => {
    cards.value = [makeCard()]
  })

  it('lists the customer rate cards with a link to the full tariff page', async () => {
    auth.permissions = new Set(['tariffs.view'])
    renderPanel()

    await waitFor(() => expect(screen.getByText('Standaard 2026')).toBeInTheDocument())
    expect(screen.getByRole('link', { name: 'Alle tarievenkaarten' })).toHaveAttribute(
      'href',
      '/rate-cards?customerId=cust-1',
    )
    // View-only: no mutations offered.
    expect(screen.queryByRole('button', { name: '+ Tarievenkaart' })).not.toBeInTheDocument()
    expect(screen.queryByText('Bewerken')).not.toBeInTheDocument()
  })

  it('offers create/edit/delete with tariffs.manage', async () => {
    auth.permissions = new Set(['tariffs.view', 'tariffs.manage'])
    renderPanel()

    await waitFor(() => expect(screen.getByText('Standaard 2026')).toBeInTheDocument())
    expect(screen.getByRole('button', { name: '+ Tarievenkaart' })).toBeInTheDocument()
    expect(screen.getByText('Bewerken')).toBeInTheDocument()
  })

  it('hides everything without tariffs.view', () => {
    auth.permissions = new Set()
    renderPanel()
    expect(screen.getByText(/geen rechten om verkooptarieven/)).toBeInTheDocument()
  })
})
