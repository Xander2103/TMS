import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { PricingTablesPage } from '../PricingTablesPage'
import type { PricingAgreement, PricingZone, UnitTypeMaster } from '../../api/pricingApi'

const auth = vi.hoisted(() => ({ permissions: new Set<string>(['tariffs.view', 'tariffs.manage']) }))
const navigateMock = vi.hoisted(() => vi.fn())

vi.mock('../../../auth/authContextValue', () => ({
  useAuth: () => ({ hasPermission: (code: string) => auth.permissions.has(code) }),
}))
vi.mock('../../../../components/ui/toastContext', () => ({
  useToast: () => ({ showSuccess: vi.fn(), showError: vi.fn() }),
}))
vi.mock('react-router-dom', async (importOriginal) => {
  const actual = await importOriginal<typeof import('react-router-dom')>()
  return { ...actual, useNavigate: () => navigateMock }
})

const state = vi.hoisted(() => ({
  agreements: [] as PricingAgreement[],
  units: [] as UnitTypeMaster[],
  zones: [] as PricingZone[],
  customers: [] as { id: string; name: string }[],
  createAgreement: vi.fn(),
  createRule: vi.fn(),
}))

vi.mock('../../api/pricingApi', async (importOriginal) => {
  const original = await importOriginal<typeof import('../../api/pricingApi')>()
  return {
    ...original,
    listAllPricingAgreements: () => Promise.resolve(state.agreements),
    listUnitTypeMaster: () => Promise.resolve(state.units),
    listPricingZones: () => Promise.resolve(state.zones),
    createPricingAgreement: state.createAgreement,
    createPriceRule: state.createRule,
  }
})
vi.mock('../../../customers/api/customersApi', () => ({
  searchCustomers: () => Promise.resolve({ items: state.customers, total: state.customers.length, page: 1, pageSize: 200 }),
}))

function makeAgreement(overrides: Partial<PricingAgreement> = {}): PricingAgreement {
  return {
    id: 'agr-1',
    customerId: null,
    customerName: null,
    name: 'Distributie België 2026',
    currency: 'EUR',
    effectiveFrom: '2026-01-01',
    effectiveUntil: null,
    isActive: true,
    minimumAmount: null,
    notes: null,
    surcharges: [],
    isShared: true,
    maximumAmount: null,
    customerCount: 3,
    customerNames: ['Acme', 'Klant B', 'Klant C'],
    baseAgreementId: null,
    baseAgreementName: null,
    modifiers: [],
    ...overrides,
  }
}

function makeUnit(overrides: Partial<UnitTypeMaster> = {}): UnitTypeMaster {
  return {
    id: 'unit-uur',
    code: 'UUR',
    name: 'Uur',
    description: null,
    isActive: true,
    sortOrder: 0,
    allowForOrderEntry: true,
    allowForPricing: true,
    allowForInventory: false,
    category: 'Time',
    decimals: 2,
    symbol: null,
    dimensionBehavior: 'Variable',
    defaultLengthCm: null,
    defaultWidthCm: null,
    defaultHeightCm: null,
    defaultWeightKg: null,
    maxWeightKg: null,
    defaultVolumeM3: null,
    defaultLoadingMeters: null,
    defaultPalletPlaces: null,
    ...overrides,
  }
}

function renderPage() {
  return render(
    <MemoryRouter>
      <PricingTablesPage />
    </MemoryRouter>,
  )
}

describe('PricingTablesPage', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    auth.permissions = new Set(['tariffs.view', 'tariffs.manage'])
    state.agreements = [
      makeAgreement(),
      makeAgreement({
        id: 'agr-2',
        name: 'Klant A tarieven',
        customerId: 'cust-a',
        customerName: 'Klant A',
        isShared: false,
        customerCount: 0,
        customerNames: null,
      }),
    ]
    state.units = [makeUnit()]
    state.zones = []
    state.customers = []
    state.createAgreement.mockResolvedValue(makeAgreement({ id: 'new-agr', name: 'Uurtarief', customerId: null, isShared: false }))
    state.createRule.mockResolvedValue({})
  })

  it('renders the list with status/shared badges and the used-by count', async () => {
    renderPage()

    expect(await screen.findByText('Distributie België 2026')).toBeInTheDocument()
    expect(screen.getByText('Gedeeld')).toBeInTheDocument()
    expect(screen.getByText('Klant: Klant A')).toBeInTheDocument()
    expect(screen.getAllByText('Actief')).toHaveLength(2)
    expect(screen.getByText('Gebruikt door 3 klanten')).toBeInTheDocument()
    expect(screen.getByText('Gebruikt door 0 klanten')).toBeInTheDocument()
  })

  it('wizard: picking a template creates the agreement + a skeleton rule, then navigates', async () => {
    const user = userEvent.setup()
    renderPage()

    await user.click(await screen.findByRole('button', { name: '+ Nieuwe tarieventabel' }))
    const dialog = await screen.findByRole('dialog')
    await user.click(within(dialog).getByRole('button', { name: /Uurtarief/ }))

    // Step 2: name is pre-filled from the template, set the required effective date.
    // Required fields render an asterisk after the label text, hence the regex match.
    expect(within(dialog).getByLabelText(/^Naam/)).toHaveValue('Uurtarief')
    const effectiveFromInput = within(dialog).getByLabelText(/Geldig vanaf/)
    await user.clear(effectiveFromInput)
    await user.type(effectiveFromInput, '2027-01-01')
    await user.click(within(dialog).getByRole('button', { name: 'Aanmaken' }))

    await waitFor(() => expect(state.createAgreement).toHaveBeenCalled())
    expect(state.createAgreement).toHaveBeenCalledWith(expect.objectContaining({ name: 'Uurtarief', isShared: false }))

    await waitFor(() => expect(state.createRule).toHaveBeenCalled())
    expect(state.createRule).toHaveBeenCalledWith(
      expect.objectContaining({ basis: 'Hourly', name: 'Uurtarief', unitTypeId: 'unit-uur', agreementId: 'new-agr' }),
    )

    await waitFor(() => expect(navigateMock).toHaveBeenCalledWith('/pricing/tables/new-agr'))
  })
})
