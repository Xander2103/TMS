import { describe, expect, it, vi, beforeEach } from 'vitest'
import { render, screen, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { OverviewTab } from '../OverviewTab'
import type { PeppolOverview } from '../../api/peppolApi'

const api = vi.hoisted(() => ({
  getPeppolOverview: vi.fn(),
}))
vi.mock('../../api/peppolApi', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../api/peppolApi')>()),
  getPeppolOverview: api.getPeppolOverview,
}))

function overview(overrides: Partial<PeppolOverview> = {}): PeppolOverview {
  return {
    legalEntities: [
      {
        legalEntityId: 'le-1',
        legalEntityName: 'Transport BV',
        hasPeppolIdentity: true,
        hasVatNumber: true,
        hasIban: false,
        enabled: true,
        environment: 'Sandbox',
        providerKey: 'sandbox',
        isComplete: false,
      },
    ],
    counts: { queued: 3, delivered: 12, failed: 2, receivedIncoming: 5 },
    customersEnabledWithoutPeppolId: 4,
    activeCustomersMissingPeppolData: 7,
    ...overrides,
  }
}

function renderTab() {
  return render(
    <MemoryRouter>
      <OverviewTab />
    </MemoryRouter>,
  )
}

describe('OverviewTab', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    api.getPeppolOverview.mockResolvedValue(overview())
  })

  it('renders the checklist per legal entity with checkmarks and badges', async () => {
    renderTab()

    const name = await screen.findByText('Transport BV')
    const row = within(name.closest('li')!)
    expect(row.getByText(/Peppol-ID/)).toBeInTheDocument()
    expect(row.getByText(/BTW-nummer/)).toBeInTheDocument()
    expect(row.getByText(/IBAN/)).toBeInTheDocument()
    expect(row.getByText('Actief')).toBeInTheDocument()
    expect(row.getByText('Sandbox')).toBeInTheDocument()
  })

  it('renders the counts as stat tiles', async () => {
    renderTab()

    expect(await screen.findByText('In wachtrij')).toBeInTheDocument()
    expect(screen.getByText('3')).toBeInTheDocument()
    expect(screen.getByText('Afgeleverd')).toBeInTheDocument()
    expect(screen.getByText('12')).toBeInTheDocument()
    expect(screen.getByText('Mislukt')).toBeInTheDocument()
    expect(screen.getByText('2')).toBeInTheDocument()
    expect(screen.getByText('Inkomend ontvangen')).toBeInTheDocument()
    expect(screen.getByText('5')).toBeInTheDocument()
  })

  it('renders the customer warnings', async () => {
    renderTab()

    expect(await screen.findByText(/4 klanten met Peppol aan maar zonder Peppol-ID/)).toBeInTheDocument()
    expect(screen.getByText(/7 actieve klanten zonder Peppol-gegevens/)).toBeInTheDocument()
  })

  it('hides the warnings when the counts are zero', async () => {
    api.getPeppolOverview.mockResolvedValue(
      overview({ customersEnabledWithoutPeppolId: 0, activeCustomersMissingPeppolData: 0 }),
    )
    renderTab()

    await screen.findByText('Transport BV')
    expect(screen.queryByText(/zonder Peppol-ID/)).not.toBeInTheDocument()
    expect(screen.queryByText(/zonder Peppol-gegevens/)).not.toBeInTheDocument()
  })
})
