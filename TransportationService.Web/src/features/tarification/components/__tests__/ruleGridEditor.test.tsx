import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { RuleGridEditor } from '../RuleGridEditor'
import type { PriceRule, PricingZone, UnitTypeSettings } from '../../api/pricingApi'

vi.mock('../../../../components/ui/toastContext', () => ({
  useToast: () => ({ showSuccess: vi.fn(), showError: vi.fn() }),
}))

const state = vi.hoisted(() => ({
  rules: [] as PriceRule[],
  units: [] as UnitTypeSettings[],
  zones: [] as PricingZone[],
  updateRule: vi.fn(),
  createRule: vi.fn(),
  deleteRule: vi.fn(),
}))

vi.mock('../../api/pricingApi', async (importOriginal) => {
  const original = await importOriginal<typeof import('../../api/pricingApi')>()
  return {
    ...original,
    listPriceRulesByAgreement: () => Promise.resolve(state.rules),
    listUnitTypeSettings: () => Promise.resolve(state.units),
    listPricingZones: () => Promise.resolve(state.zones),
    updatePriceRule: state.updateRule,
    createPriceRule: state.createRule,
    deletePriceRule: state.deleteRule,
  }
})

function makeRule(overrides: Partial<PriceRule> = {}): PriceRule {
  return {
    id: 'rule-1',
    customerId: null,
    customerName: null,
    unitTypeId: 'unit-pallet',
    unitTypeName: 'Europallet',
    basis: 'PerUnit',
    zoneId: null,
    zoneName: null,
    name: 'Basisregel',
    currency: 'EUR',
    effectiveFrom: '2026-01-01',
    effectiveUntil: null,
    isActive: true,
    unitPrice: 10,
    minimumAmount: null,
    brackets: [],
    agreementId: 'agr-1',
    agreementName: 'Distributie 2026',
    priority: 0,
    baseAmount: null,
    oversizeLengthCm: null,
    oversizeWidthCm: null,
    oversizeBillableFactor: null,
    minimumQuantity: null,
    quantityRoundingStep: null,
    maximumAmount: null,
    bracketMode: 'Absolute',
    ...overrides,
  }
}

function makeBracketRule(): PriceRule {
  return makeRule({
    id: 'rule-2',
    name: 'Palletstaffel',
    basis: 'QuantityBracket',
    unitPrice: null,
    brackets: [
      { id: 'b1', fromQuantity: 1, toQuantity: 1, price: 45, pricePerExtraUnit: null, weightToKg: null, volumeToM3: null, loadingMetersTo: null },
      { id: 'b2', fromQuantity: 2, toQuantity: null, price: 70, pricePerExtraUnit: 20, weightToKg: null, volumeToM3: null, loadingMetersTo: null },
    ],
  })
}

beforeEach(() => {
  vi.clearAllMocks()
  state.rules = [makeRule(), makeBracketRule()]
  state.units = [
    { id: 'unit-pallet', code: 'EUROPALLET', name: 'Europallet', isActive: true, sortOrder: 0, allowForOrderEntry: true, allowForPricing: true },
  ]
  state.zones = []
  state.updateRule.mockImplementation((id: string, input: unknown) =>
    Promise.resolve({ ...state.rules.find((r) => r.id === id), ...(input as object) }),
  )
  state.createRule.mockResolvedValue(makeRule({ id: 'rule-new', name: 'Nieuwe regel' }))
  state.deleteRule.mockResolvedValue(undefined)
})

describe('RuleGridEditor', () => {
  it('renders rule rows and expanded bracket sub-rows', async () => {
    render(<RuleGridEditor agreementId="agr-1" agreementCustomerId={null} canManage />)

    expect(await screen.findByLabelText('Naam voor Basisregel')).toHaveValue('Basisregel')
    expect(screen.getByLabelText('Naam voor Palletstaffel')).toBeInTheDocument()
    // Bracket rows render by default (expanded), with their own price cells.
    expect(screen.getByLabelText('Staffel 1 van Palletstaffel prijs')).toHaveValue(45)
    expect(screen.getByLabelText('Staffel 2 van Palletstaffel prijs')).toHaveValue(70)
  })

  it('saves a cell edit on blur via the rule PUT endpoint', async () => {
    const user = userEvent.setup()
    render(<RuleGridEditor agreementId="agr-1" agreementCustomerId={null} canManage />)

    const nameInput = await screen.findByLabelText('Naam voor Basisregel')
    await user.clear(nameInput)
    await user.type(nameInput, 'Hernoemde regel')
    await user.tab()

    await waitFor(() => expect(state.updateRule).toHaveBeenCalledWith('rule-1', expect.objectContaining({ name: 'Hernoemde regel' })))
  })

  it('adds, duplicates and deletes rules via the toolbar/row actions', async () => {
    const user = userEvent.setup()
    render(<RuleGridEditor agreementId="agr-1" agreementCustomerId={null} canManage />)

    await user.click(await screen.findByRole('button', { name: '+ Regel' }))
    await waitFor(() => expect(state.createRule).toHaveBeenCalledWith(expect.objectContaining({ agreementId: 'agr-1', name: 'Nieuwe regel' })))

    state.createRule.mockClear()
    const duplicateButtons = screen.getAllByRole('button', { name: 'Dupliceren' })
    await user.click(duplicateButtons[0])
    await waitFor(() => expect(state.createRule).toHaveBeenCalledWith(expect.objectContaining({ name: 'Basisregel (kopie)' })))

    const deleteButtons = screen.getAllByRole('button', { name: 'Verwijderen' })
    await user.click(deleteButtons[0])
    const dialog = await screen.findByRole('dialog')
    await user.click(within(dialog).getByRole('button', { name: 'Verwijderen' }))
    await waitFor(() => expect(state.deleteRule).toHaveBeenCalledWith('rule-1'))
  })

  it('disables every input without tariffs.manage', async () => {
    render(<RuleGridEditor agreementId="agr-1" agreementCustomerId={null} canManage={false} />)

    expect(await screen.findByLabelText('Naam voor Basisregel')).toBeDisabled()
    expect(screen.queryByRole('button', { name: '+ Regel' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Dupliceren' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Verwijderen' })).not.toBeInTheDocument()
  })
})
