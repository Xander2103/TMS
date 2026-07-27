import { describe, expect, it, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { CombinedDiscountsPanel } from '../CombinedDiscountsPanel'
import type { CombinedUnitDiscount, UnitTypeSettings } from '../../api/pricingApi'

const auth = vi.hoisted(() => ({ permissions: new Set<string>() }))

vi.mock('../../../auth/authContextValue', () => ({
  useAuth: () => ({ hasPermission: (code: string) => auth.permissions.has(code) }),
}))
vi.mock('../../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }),
}))

const state = vi.hoisted(() => ({
  discounts: [] as CombinedUnitDiscount[],
  units: [] as UnitTypeSettings[],
  create: vi.fn(),
  update: vi.fn(),
  remove: vi.fn(),
}))

vi.mock('../../api/pricingApi', async (importOriginal) => {
  const original = await importOriginal<typeof import('../../api/pricingApi')>()
  return {
    ...original,
    listCombinedDiscounts: () => Promise.resolve(state.discounts),
    listUnitTypeSettings: () => Promise.resolve(state.units),
    createCombinedDiscount: state.create,
    updateCombinedDiscount: state.update,
    deleteCombinedDiscount: state.remove,
  }
})

function makeDiscount(overrides: Partial<CombinedUnitDiscount> = {}): CombinedUnitDiscount {
  return {
    id: 'disc-1',
    customerId: 'cust-1',
    customerName: 'Klant X',
    agreementId: null,
    agreementName: null,
    name: 'Combikorting',
    scope: 'DeliveryAddress',
    effectiveFrom: '2026-01-01',
    effectiveUntil: null,
    isActive: true,
    units: [
      { id: 'u1', unitTypeId: 'unit-euro', unitTypeName: 'Europallet', equivalentFactor: 1 },
      { id: 'u2', unitTypeId: 'unit-colli', unitTypeName: 'Colli', equivalentFactor: 0.25 },
    ],
    tiers: [
      { id: 't1', fromCount: 2, toCount: 3, percent: 5 },
      { id: 't2', fromCount: 4, toCount: null, percent: 8 },
    ],
    ...overrides,
  }
}

describe('CombinedDiscountsPanel — combined-unit degression (spec §29-31)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    auth.permissions = new Set(['tariffs.view', 'tariffs.manage'])
    state.discounts = [makeDiscount()]
    state.units = [
      { id: 'unit-euro', code: 'EUROPALLET', name: 'Europallet', isActive: true, sortOrder: 0, allowForOrderEntry: true, allowForPricing: true },
      { id: 'unit-colli', code: 'COLLI', name: 'Colli', isActive: true, sortOrder: 1, allowForOrderEntry: true, allowForPricing: true },
    ]
    state.create.mockResolvedValue(makeDiscount({ id: 'disc-new', name: 'Nieuwe korting' }))
  })

  it('renders the list of discounts with scope, units and tier count', async () => {
    render(<CombinedDiscountsPanel customerId="cust-1" />)

    expect(await screen.findByText('Combikorting')).toBeInTheDocument()
    expect(screen.getByText('Per leveradres')).toBeInTheDocument()
    expect(screen.getByText('Europallet, Colli')).toBeInTheDocument()
    expect(screen.getByText('2')).toBeInTheDocument() // tier count
  })

  it('creates a discount with units and tiers, scoped to the customer prop', async () => {
    const user = userEvent.setup()
    render(<CombinedDiscountsPanel customerId="cust-1" />)

    await user.click(await screen.findByRole('button', { name: '+ Combinatiekorting' }))
    const dialog = await screen.findByRole('dialog')

    await user.type(within(dialog).getByLabelText(/Naam/), 'Nieuwe korting')
    await user.selectOptions(within(dialog).getByLabelText('Eenheid 1'), 'unit-euro')
    await user.clear(within(dialog).getByLabelText('Eenheid 1 factor'))
    await user.type(within(dialog).getByLabelText('Eenheid 1 factor'), '1')
    await user.type(within(dialog).getByLabelText('Staffel 1 van'), '2')
    await user.type(within(dialog).getByLabelText('Staffel 1 korting %'), '5')
    await user.click(within(dialog).getByRole('button', { name: 'Opslaan' }))

    await waitFor(() => expect(state.create).toHaveBeenCalled())
    const payload = state.create.mock.calls[0][0]
    expect(payload).toEqual(
      expect.objectContaining({
        customerId: 'cust-1',
        agreementId: null,
        name: 'Nieuwe korting',
        units: [{ unitTypeId: 'unit-euro', equivalentFactor: 1 }],
        tiers: [{ fromCount: 2, toCount: null, percent: 5 }],
      }),
    )
  })

  it('surfaces a validation message when no unit is chosen', async () => {
    const user = userEvent.setup()
    render(<CombinedDiscountsPanel customerId="cust-1" />)

    await user.click(await screen.findByRole('button', { name: '+ Combinatiekorting' }))
    const dialog = await screen.findByRole('dialog')

    await user.type(within(dialog).getByLabelText(/Naam/), 'Korting zonder eenheid')
    await user.click(within(dialog).getByRole('button', { name: 'Opslaan' }))

    expect(await within(dialog).findByRole('alert')).toHaveTextContent('Kies minstens één eenheid.')
    expect(state.create).not.toHaveBeenCalled()
  })
})
