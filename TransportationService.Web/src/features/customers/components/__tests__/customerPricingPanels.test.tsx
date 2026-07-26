import { describe, expect, it, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { CustomerUnitsPanel } from '../CustomerUnitsPanel'
import { CustomerUnitPricingPanel } from '../CustomerUnitPricingPanel'
import { CustomerPriceAdjustmentsPanel } from '../CustomerPriceAdjustmentsPanel'
import type {
  CustomerPricingConfig,
  PriceRule,
  PricingAgreement,
  PricingAssignment,
  ScheduledPriceAdjustment,
  UnitTypeSettings,
} from '../../../tarification/api/pricingApi'

const auth = vi.hoisted(() => ({ permissions: new Set<string>() }))

vi.mock('../../../auth/authContextValue', () => ({
  useAuth: () => ({ hasPermission: (code: string) => auth.permissions.has(code) }),
}))
vi.mock('../../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }),
}))

const state = vi.hoisted(() => ({
  config: null as CustomerPricingConfig | null,
  rules: [] as PriceRule[],
  agreements: [] as PricingAgreement[],
  adjustments: [] as ScheduledPriceAdjustment[],
  units: [] as UnitTypeSettings[],
  assignmentsByAgreement: {} as Record<string, PricingAssignment[]>,
  saveConfig: vi.fn(),
  previewAdjustment: vi.fn(),
  createAdjustment: vi.fn(),
  cancelAdjustment: vi.fn(),
  updateAgreement: vi.fn(),
}))

vi.mock('../../../tarification/api/pricingApi', async (importOriginal) => {
  const original = await importOriginal<typeof import('../../../tarification/api/pricingApi')>()
  return {
    ...original,
    getCustomerPricingConfig: () => Promise.resolve(state.config),
    listPriceRules: () => Promise.resolve(state.rules),
    // Mirrors the backend filter: with a customerId, only that customer's own agreements;
    // without one, the company-wide + shared (CustomerId null) agreements.
    listPricingAgreements: (customerId?: string) =>
      Promise.resolve(
        customerId
          ? state.agreements.filter((a) => a.customerId === customerId)
          : state.agreements.filter((a) => a.customerId === null),
      ),
    getAgreementAssignments: (agreementId: string) => Promise.resolve(state.assignmentsByAgreement[agreementId] ?? []),
    listPriceAdjustments: () => Promise.resolve(state.adjustments),
    listUnitTypeSettings: () => Promise.resolve(state.units),
    listPricingZones: () => Promise.resolve([]),
    saveCustomerPricingConfig: state.saveConfig,
    previewPriceAdjustment: state.previewAdjustment,
    createPriceAdjustment: state.createAdjustment,
    cancelPriceAdjustment: state.cancelAdjustment,
    createPriceRule: vi.fn(),
    updatePriceRule: vi.fn(),
    deletePriceRule: vi.fn(),
    createPricingAgreement: vi.fn(),
    updatePricingAgreement: state.updateAgreement,
    deletePricingAgreement: vi.fn(),
    saveAgreementAssignments: vi.fn(),
  }
})

function makeConfig(): CustomerPricingConfig {
  return {
    preferredUnits: [
      {
        unitTypeId: 'unit-pallet',
        code: 'EUROPALLET',
        name: 'Europallet',
        sortOrder: 0,
        customerLabel: 'EURO PAL',
        ediCode: 'EPAL',
        excelCode: 'EURO',
        isFavourite: true,
      },
    ],
    serviceOptions: [],
  }
}

function makeAgreement(overrides: Partial<PricingAgreement> = {}): PricingAgreement {
  return {
    id: 'agr-1',
    customerId: 'cust-1',
    customerName: 'Acme',
    name: 'Distributie 2026',
    currency: 'EUR',
    effectiveFrom: '2026-01-01',
    effectiveUntil: null,
    isActive: true,
    minimumAmount: 60,
    notes: 'Historisch gunstig contract',
    surcharges: [{ id: 's1', name: 'Duurtoeslag', kind: 'Percent', value: 5 }],
    isShared: false,
    maximumAmount: null,
    customerCount: 0,
    customerNames: null,
    baseAgreementId: null,
    baseAgreementName: null,
    modifiers: [],
    ...overrides,
  }
}

function makeRule(overrides: Partial<PriceRule> = {}): PriceRule {
  return {
    id: 'rule-1',
    customerId: 'cust-1',
    customerName: 'Acme',
    unitTypeId: 'unit-pallet',
    unitTypeName: 'Europallet',
    basis: 'QuantityBracket',
    zoneId: null,
    zoneName: null,
    name: 'Europallet Brussel',
    currency: 'EUR',
    effectiveFrom: '2026-01-01',
    effectiveUntil: null,
    isActive: true,
    unitPrice: null,
    minimumAmount: null,
    brackets: [
      { id: 'b1', fromQuantity: 1, toQuantity: 1, price: 45, pricePerExtraUnit: null },
      { id: 'b2', fromQuantity: 2, toQuantity: null, price: 70, pricePerExtraUnit: 20 },
    ],
    agreementId: 'agr-1',
    agreementName: 'Distributie 2026',
    priority: 0,
    baseAmount: null,
    oversizeLengthCm: null,
    oversizeWidthCm: null,
    oversizeBillableFactor: null,
    minimumQuantity: null,
    quantityRoundingStep: null,
    ...overrides,
  }
}

describe('CustomerUnitsPanel', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    auth.permissions = new Set(['tariffs.view', 'tariffs.manage'])
    state.config = makeConfig()
    state.units = [
      { id: 'unit-pallet', code: 'EUROPALLET', name: 'Europallet', isActive: true, sortOrder: 0, allowForOrderEntry: true, allowForPricing: true },
      { id: 'unit-colli', code: 'COLLI', name: 'Colli', isActive: true, sortOrder: 1, allowForOrderEntry: true, allowForPricing: true },
    ]
    state.saveConfig.mockResolvedValue(makeConfig())
  })

  it('shows the configured units with label, EDI and Excel codes and favourite state', async () => {
    render(<CustomerUnitsPanel customerId="cust-1" />)

    expect(await screen.findByText('Europallet')).toBeInTheDocument()
    expect(screen.getByLabelText('Klantbenaming voor Europallet')).toHaveValue('EURO PAL')
    expect(screen.getByLabelText('EDI-code voor Europallet')).toHaveValue('EPAL')
    expect(screen.getByLabelText('Excel-code voor Europallet')).toHaveValue('EURO')
    expect(screen.getByLabelText('Favoriet Europallet')).toHaveAttribute('aria-pressed', 'true')
  })

  it('saves an edited customer label with the full unit configuration', async () => {
    const user = userEvent.setup()
    render(<CustomerUnitsPanel customerId="cust-1" />)

    const labelInput = await screen.findByLabelText('Klantbenaming voor Europallet')
    await user.clear(labelInput)
    await user.type(labelInput, 'EP-A')
    await user.tab()

    await waitFor(() => expect(state.saveConfig).toHaveBeenCalled())
    const payload = state.saveConfig.mock.calls[0][1]
    expect(payload.units).toEqual([
      expect.objectContaining({ unitTypeId: 'unit-pallet', customerLabel: 'EP-A', ediCode: 'EPAL', isFavourite: true }),
    ])
  })

  it('can add another global unit to the customer configuration', async () => {
    const user = userEvent.setup()
    render(<CustomerUnitsPanel customerId="cust-1" />)

    await screen.findByText('Europallet')
    await user.selectOptions(screen.getByLabelText('Eenheid toevoegen'), 'unit-colli')
    await user.click(screen.getByRole('button', { name: '+ Eenheid toevoegen' }))

    await waitFor(() => expect(state.saveConfig).toHaveBeenCalled())
    const payload = state.saveConfig.mock.calls[0][1]
    expect(payload.units).toHaveLength(2)
    expect(payload.units[1]).toEqual(expect.objectContaining({ unitTypeId: 'unit-colli', isFavourite: true }))
  })

  it('is read-only without tariffs.manage', async () => {
    auth.permissions = new Set(['tariffs.view'])
    render(<CustomerUnitsPanel customerId="cust-1" />)

    expect(await screen.findByLabelText('Klantbenaming voor Europallet')).toBeDisabled()
    expect(screen.queryByRole('button', { name: '+ Eenheid toevoegen' })).not.toBeInTheDocument()
  })
})

describe('CustomerUnitPricingPanel — service overrides', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    auth.permissions = new Set(['tariffs.view', 'tariffs.manage'])
    state.rules = []
    state.agreements = []
    state.units = []
    state.config = {
      preferredUnits: [],
      serviceOptions: [
        {
          serviceOptionId: 'svc-10',
          name: 'Levering vóór 10:00',
          kind: 'Fixed',
          defaultValue: 15,
          customerValue: 10,
          disabled: false,
          minimumAmount: null,
          invoiceDescription: null,
          effectiveFrom: null,
          effectiveUntil: null,
          effectiveValue: 10,
          source: 'Klanttarief',
        },
        {
          serviceOptionId: 'svc-klep',
          name: 'Laadklep',
          kind: 'Fixed',
          defaultValue: 20,
          customerValue: null,
          disabled: true,
          minimumAmount: null,
          invoiceDescription: null,
          effectiveFrom: null,
          effectiveUntil: null,
          effectiveValue: 20,
          source: 'Klanttarief',
        },
        {
          serviceOptionId: 'svc-wacht',
          name: 'Wachttijd',
          kind: 'PerHour',
          defaultValue: 45,
          customerValue: null,
          disabled: false,
          minimumAmount: null,
          invoiceDescription: null,
          effectiveFrom: null,
          effectiveUntil: null,
          effectiveValue: 45,
          source: 'Algemene standaard',
        },
      ],
    }
    state.saveConfig.mockResolvedValue(state.config)
  })

  it('shows global vs override vs effective price with source and visible warnings', async () => {
    render(<CustomerUnitPricingPanel customerId="cust-1" />)

    await screen.findByText('Diensten & toeslagen')
    // Table-wide warning is always visible, not a tooltip.
    expect(screen.getByText('Let op: wanneer u hier een waarde invult, wordt de algemene standaardregel voor deze klant overschreven.')).toBeInTheDocument()
    // Overridden service: global €15, override input 10, effective €10, source Klanttarief.
    expect(screen.getByText('€ 15.00')).toBeInTheDocument()
    expect(screen.getByLabelText('Klantoverride voor Levering vóór 10:00')).toHaveValue(10)
    expect(screen.getByText('€ 10.00')).toBeInTheDocument()
    expect(screen.getAllByText('Klanttarief').length).toBeGreaterThan(0)
    expect(screen.getByText(/deze klantprijs overschrijft.*€ 15\.00/)).toBeInTheDocument()
    // Disabled service warning + effective state.
    expect(screen.getByText('Let op: deze service is algemeen beschikbaar, maar wordt voor deze klant uitgeschakeld.')).toBeInTheDocument()
    expect(screen.getByText('Uitgeschakeld')).toBeInTheDocument()
    // Inherited hourly service shows the global value and source.
    expect(screen.getAllByText('€ 45.00/uur').length).toBeGreaterThan(0)
    expect(screen.getAllByText('Algemene standaard').length).toBeGreaterThan(0)
  })

  it('resets an override back to the global value', async () => {
    const user = userEvent.setup()
    render(<CustomerUnitPricingPanel customerId="cust-1" />)

    await screen.findByText('Diensten & toeslagen')
    const resetButtons = screen.getAllByRole('button', { name: 'Algemene waarde opnieuw gebruiken' })
    await user.click(resetButtons[0])

    await waitFor(() => expect(state.saveConfig).toHaveBeenCalled())
    const payload = state.saveConfig.mock.calls[0][1]
    const reset = payload.optionPrices.find((o: { serviceOptionId: string }) => o.serviceOptionId === 'svc-10')
    expect(reset).toEqual(expect.objectContaining({ value: null, disabled: false }))
    // Other services keep their configuration untouched.
    const klep = payload.optionPrices.find((o: { serviceOptionId: string }) => o.serviceOptionId === 'svc-klep')
    expect(klep).toEqual(expect.objectContaining({ disabled: true }))
  })
})

describe('CustomerUnitPricingPanel', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    auth.permissions = new Set(['tariffs.view', 'tariffs.manage'])
    state.config = makeConfig()
    state.units = []
    state.assignmentsByAgreement = {}
    state.agreements = [makeAgreement()]
    state.rules = [
      makeRule(),
      makeRule({ id: 'rule-2', name: 'Europallet Brussel (+4%)', effectiveFrom: '2099-10-01' }),
      makeRule({ id: 'rule-3', name: 'Europallet Brussel 2025', effectiveFrom: '2025-01-01', effectiveUntil: '2025-12-31' }),
    ]
  })

  it('groups rules into current, future and history and shows the agreement', async () => {
    render(<CustomerUnitPricingPanel customerId="cust-1" />)

    expect(await screen.findByText('Actuele prijzen')).toBeInTheDocument()
    expect(screen.getByText('Toekomstige prijzen')).toBeInTheDocument()
    expect(screen.getByText('Prijshistoriek (1)')).toBeInTheDocument()
    expect(screen.getByText('Europallet Brussel (+4%)')).toBeInTheDocument()
    // Agreement table shows the commercial identity incl. minimum and surcharge.
    expect(screen.getAllByText('Distributie 2026').length).toBeGreaterThan(0)
    expect(screen.getByText('€ 60.00')).toBeInTheDocument()
    expect(screen.getByText('Duurtoeslag (5%)')).toBeInTheDocument()
    expect(screen.getByText('Historisch gunstig contract')).toBeInTheDocument()
  })

  it('shows a shared table assigned to this customer with a badge and its adjustment', async () => {
    state.agreements = [
      ...state.agreements,
      makeAgreement({
        id: 'agr-shared',
        customerId: null,
        customerName: null,
        name: 'Distributie België 2026',
        minimumAmount: null,
        notes: null,
        surcharges: [],
        isShared: true,
        customerCount: 1,
        customerNames: ['Acme'],
      }),
    ]
    state.assignmentsByAgreement['agr-shared'] = [
      {
        id: 'assign-1',
        customerId: 'cust-1',
        customerName: 'Acme',
        percentAdjustment: -5,
        fixedAdjustment: null,
        effectiveFrom: null,
        effectiveUntil: null,
        notes: null,
      },
    ]

    render(<CustomerUnitPricingPanel customerId="cust-1" />)

    expect(await screen.findByText('Distributie België 2026')).toBeInTheDocument()
    expect(screen.getByText('Gedeelde tabel')).toBeInTheDocument()
    expect(screen.getByText('-5%')).toBeInTheDocument()
  })

  it('shows a derived table with its base and modifiers, and saves the derivation section', async () => {
    const user = userEvent.setup()
    state.agreements = [
      makeAgreement({
        id: 'agr-base',
        customerId: null,
        customerName: null,
        name: 'Distributie België',
        minimumAmount: null,
        notes: null,
        surcharges: [],
        isShared: true,
      }),
      makeAgreement({
        id: 'agr-nl',
        name: 'NL Distributie',
        minimumAmount: null,
        notes: null,
        surcharges: [],
        baseAgreementId: 'agr-base',
        baseAgreementName: 'Distributie België',
        modifiers: [
          { id: 'mod-1', sequence: 1, name: 'Nederland +30%', countryCode: 'NL', zoneId: null, zoneName: null, percent: 30, fixedAmount: null },
        ],
      }),
    ]
    state.updateAgreement.mockResolvedValue(state.agreements[1])

    render(<CustomerUnitPricingPanel customerId="cust-1" />)

    expect(await screen.findByText('Afgeleid van Distributie België')).toBeInTheDocument()

    const nlRow = screen.getByText('NL Distributie').closest('tr')!
    await user.click(within(nlRow).getByRole('button', { name: 'Bewerken' }))
    const dialog = await screen.findByRole('dialog')

    expect(within(dialog).getByText(/Deze tabel gebruikt de prijsregels van Distributie België/)).toBeInTheDocument()
    expect(within(dialog).getByLabelText('Basistabel')).toHaveValue('agr-base')
    expect(within(dialog).getByLabelText('Aanpassing 1 naam')).toHaveValue('Nederland +30%')
    expect(within(dialog).getByLabelText('Aanpassing 1 land')).toHaveValue('NL')

    await user.click(within(dialog).getByRole('button', { name: 'Opslaan' }))

    await waitFor(() => expect(state.updateAgreement).toHaveBeenCalled())
    const [, payload] = state.updateAgreement.mock.calls[0]
    expect(payload).toEqual(expect.objectContaining({
      baseAgreementId: 'agr-base',
      modifiers: [
        expect.objectContaining({ sequence: 1, name: 'Nederland +30%', countryCode: 'NL', zoneId: null, percent: 30, fixedAmount: null }),
      ],
    }))
  })

  it('shows only the fields of the selected primary pricing basis', async () => {
    const user = userEvent.setup()
    state.units = [
      { id: 'unit-pallet', code: 'EUROPALLET', name: 'Europallet', isActive: true, sortOrder: 0, allowForOrderEntry: true, allowForPricing: true },
    ]
    render(<CustomerUnitPricingPanel customerId="cust-1" />)

    await user.click(await screen.findByRole('button', { name: '+ Prijsregel' }))
    const dialog = await screen.findByRole('dialog')

    // Default: Per eenheid with staffels — unit select + brackets, no hourly fields.
    expect(within(dialog).getByLabelText(/Prijsbasis/)).toHaveValue('unit')
    expect(within(dialog).getByLabelText(/^Eenheid/)).toBeInTheDocument()
    expect(within(dialog).getByText('Staffels (aantal)')).toBeInTheDocument()
    expect(within(dialog).queryByLabelText(/Minimum aantal uur/)).not.toBeInTheDocument()

    // Per uur: hourly fields appear, staffels disappear, unit select stays (tijd-eenheid).
    await user.selectOptions(within(dialog).getByLabelText(/Prijsbasis/), 'Hourly')
    expect(within(dialog).getByLabelText(/Minimum aantal uur/)).toBeInTheDocument()
    expect(within(dialog).getByLabelText(/Afrondingsstap/)).toBeInTheDocument()
    expect(within(dialog).queryByText('Staffels (aantal)')).not.toBeInTheDocument()

    // Forfait: fixed price only — no unit, no staffels, no hourly fields.
    await user.selectOptions(within(dialog).getByLabelText(/Prijsbasis/), 'Fixed')
    expect(within(dialog).getByLabelText(/Vaste prijs/)).toBeInTheDocument()
    expect(within(dialog).queryByLabelText(/^Eenheid/)).not.toBeInTheDocument()
    expect(within(dialog).queryByLabelText(/Minimum aantal uur/)).not.toBeInTheDocument()

    // Per kilometer: km price + basisbedrag.
    await user.selectOptions(within(dialog).getByLabelText(/Prijsbasis/), 'PerKm')
    expect(within(dialog).getByLabelText(/Prijs per km/)).toBeInTheDocument()
    expect(within(dialog).getByLabelText(/Basisbedrag/)).toBeInTheDocument()
  })

  it('hides management actions without tariffs.manage', async () => {
    auth.permissions = new Set(['tariffs.view'])
    render(<CustomerUnitPricingPanel customerId="cust-1" />)

    await screen.findByText('Actuele prijzen')
    expect(screen.queryByRole('button', { name: '+ Prijsregel' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: '+ Prijsafspraak' })).not.toBeInTheDocument()
  })
})

describe('CustomerPriceAdjustmentsPanel', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    auth.permissions = new Set(['tariffs.view', 'tariffs.manage'])
    state.rules = [makeRule()]
    state.adjustments = [
      {
        id: 'adj-1',
        customerId: 'cust-1',
        effectiveDate: '2099-10-01',
        percent: 4,
        status: 'Gepland',
        reason: 'Indexatie',
        ruleCount: 3,
        createdAt: '2026-08-15T10:00:00Z',
      },
    ]
    state.previewAdjustment.mockResolvedValue([
      {
        priceRuleId: 'rule-1',
        ruleName: 'Europallet Brussel',
        effectiveFrom: '2026-01-01',
        effectiveUntil: null,
        changes: [
          { field: 'Staffel 1-1', oldValue: 45, newValue: 46.8 },
          { field: 'Staffel 2+', oldValue: 70, newValue: 72.8 },
        ],
      },
    ])
    state.createAdjustment.mockResolvedValue(state.adjustments[0])
    state.cancelAdjustment.mockResolvedValue({ ...state.adjustments[0], status: 'Geannuleerd' })
  })

  it('lists scheduled adjustments with status and cancel action', async () => {
    render(<CustomerPriceAdjustmentsPanel customerId="cust-1" />)

    expect(await screen.findByText('2099-10-01')).toBeInTheDocument()
    expect(screen.getByText('+4%')).toBeInTheDocument()
    expect(screen.getByText('Gepland')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Annuleren' })).toBeInTheDocument()
  })

  it('previews the calculated future values before confirming', async () => {
    const user = userEvent.setup()
    render(<CustomerPriceAdjustmentsPanel customerId="cust-1" />)

    await user.click(await screen.findByRole('button', { name: '+ Nieuwe prijsaanpassing' }))
    const dialog = await screen.findByRole('dialog')
    await user.type(within(dialog).getByLabelText(/Ingangsdatum/), '2099-10-01')
    await user.type(within(dialog).getByLabelText(/Aanpassing/), '4')
    await user.click(within(dialog).getByRole('button', { name: 'Preview' }))

    // Every affected value is shown as old → new before anything is saved.
    expect(await within(dialog).findByText('€ 45.00 → € 46.80')).toBeInTheDocument()
    expect(within(dialog).getByText('€ 70.00 → € 72.80')).toBeInTheDocument()
    expect(state.createAdjustment).not.toHaveBeenCalled()

    await user.click(within(dialog).getByRole('button', { name: 'Bevestigen' }))
    await waitFor(() => expect(state.createAdjustment).toHaveBeenCalledWith('cust-1', expect.objectContaining({
      effectiveDate: '2099-10-01',
      percent: 4,
      ruleIds: null,
    })))
  })

  it('cancels a scheduled adjustment after confirmation', async () => {
    const user = userEvent.setup()
    render(<CustomerPriceAdjustmentsPanel customerId="cust-1" />)

    await user.click(await screen.findByRole('button', { name: 'Annuleren' }))
    await user.click(await screen.findByRole('button', { name: 'Annuleren bevestigen' }))

    await waitFor(() => expect(state.cancelAdjustment).toHaveBeenCalledWith('cust-1', 'adj-1'))
  })

  it('hides the wizard without tariffs.manage', async () => {
    auth.permissions = new Set(['tariffs.view'])
    render(<CustomerPriceAdjustmentsPanel customerId="cust-1" />)

    expect(await screen.findByText('Geplande prijswijzigingen')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: '+ Nieuwe prijsaanpassing' })).not.toBeInTheDocument()
  })
})
