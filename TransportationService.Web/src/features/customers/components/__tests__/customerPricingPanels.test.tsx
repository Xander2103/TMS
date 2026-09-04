import { describe, expect, it, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { CustomerUnitsPanel } from '../CustomerUnitsPanel'
import { CustomerUnitPricingPanel } from '../CustomerUnitPricingPanel'
import { CustomerPriceAdjustmentsPanel } from '../CustomerPriceAdjustmentsPanel'
import type {
  CustomerAgreementLink,
  CustomerBracketOverrideRow,
  CustomerPricingConfig,
  PriceRule,
  PricingAgreement,
  PricingAssignment,
  ScheduledPriceAdjustment,
  ServiceOption,
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
  links: [] as CustomerAgreementLink[],
  bracketOverrides: [] as CustomerBracketOverrideRow[],
  serviceMeta: [] as ServiceOption[],
  adjustments: [] as ScheduledPriceAdjustment[],
  units: [] as UnitTypeSettings[],
  assignmentsByAgreement: {} as Record<string, PricingAssignment[]>,
  saveConfig: vi.fn(),
  saveAssignments: vi.fn(),
  previewAdjustment: vi.fn(),
  createAdjustment: vi.fn(),
  cancelAdjustment: vi.fn(),
  updateAgreement: vi.fn(),
  createRule: vi.fn(),
}))

vi.mock('../../api/customerBillingConfigApi', () => ({
  getDieselSurcharge: () => Promise.reject(new Error('no rights in test')),
}))

vi.mock('../../../tarification/api/pricingApi', async (importOriginal) => {
  const original = await importOriginal<typeof import('../../../tarification/api/pricingApi')>()
  return {
    ...original,
    getCustomerPricingConfig: () => Promise.resolve(state.config),
    listPriceRules: () => Promise.resolve(state.rules),
    listCustomerAgreements: () => Promise.resolve(state.links),
    listCustomerBracketOverrides: () => Promise.resolve(state.bracketOverrides),
    listServiceOptions: () => Promise.resolve(state.serviceMeta),
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
    createPriceRule: state.createRule,
    updatePriceRule: vi.fn(),
    deletePriceRule: vi.fn(),
    createPricingAgreement: vi.fn(),
    updatePricingAgreement: state.updateAgreement,
    deletePricingAgreement: vi.fn(),
    saveAgreementAssignments: state.saveAssignments,
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
    includedLoadingMinutes: null,
    includedUnloadingMinutes: null,
    includedCombinedMinutes: null,
    extraHourlyRate: null,
    ...overrides,
  }
}

function makeLink(overrides: Partial<CustomerAgreementLink> = {}): CustomerAgreementLink {
  return {
    agreementId: 'agr-1',
    name: 'Distributie 2026',
    isShared: false,
    effectiveFrom: '2026-01-01',
    effectiveUntil: null,
    isActive: true,
    minimumAmount: 60,
    maximumAmount: null,
    baseAgreementId: null,
    baseAgreementName: null,
    assignmentId: null,
    assignmentPercentAdjustment: null,
    assignmentFixedAdjustment: null,
    assignmentEffectiveFrom: null,
    assignmentEffectiveUntil: null,
    plannedAdjustmentDate: null,
    plannedAdjustmentPercent: null,
    plannedAdjustmentAmountDelta: null,
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
      { id: 'b1', fromQuantity: 1, toQuantity: 1, price: 45, pricePerExtraUnit: null, weightToKg: null, volumeToM3: null, loadingMetersTo: null },
      { id: 'b2', fromQuantity: 2, toQuantity: null, price: 70, pricePerExtraUnit: 20, weightToKg: null, volumeToM3: null, loadingMetersTo: null },
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
    maximumAmount: null,
    bracketMode: 'Absolute',
    ...overrides,
  }
}

/** Picks an option in a SearchableSelect combobox by clicking it open and choosing the row. */
async function pickOption(user: ReturnType<typeof userEvent.setup>, combobox: HTMLElement, optionName: string | RegExp) {
  await user.click(combobox)
  await user.click(await screen.findByRole('option', { name: optionName }))
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

  it('renders a read-only overview — values as text, never permanent inputs (no blur-save)', async () => {
    render(<CustomerUnitsPanel customerId="cust-1" />)

    expect(await screen.findByText('Eenheden & externe codes')).toBeInTheDocument()
    expect(screen.getByText('Europallet')).toBeInTheDocument()
    expect(screen.getByText('EURO PAL')).toBeInTheDocument()
    expect(screen.getByText('EPAL')).toBeInTheDocument()
    expect(screen.getByText('EURO')).toBeInTheDocument()
    expect(screen.getAllByText(/Favoriet/).length).toBeGreaterThan(0)
    // The overview holds NO editable inputs — editing goes through the modal.
    expect(screen.queryByLabelText('Klantbenaming voor Europallet')).not.toBeInTheDocument()
  })

  it('edits a mapping in a modal and saves the full unit list with NO option prices', async () => {
    const user = userEvent.setup()
    render(<CustomerUnitsPanel customerId="cust-1" />)

    const row = (await screen.findByText('Europallet')).closest('tr')!
    await user.click(within(row).getByRole('button', { name: 'Bewerken' }))
    const dialog = await screen.findByRole('dialog')

    const edi = within(dialog).getByLabelText(/EDI-code/)
    expect(edi).toHaveValue('EPAL')
    await user.clear(edi)
    await user.type(edi, 'EPAL-2')
    await user.click(within(dialog).getByRole('button', { name: 'Opslaan' }))

    await waitFor(() => expect(state.saveConfig).toHaveBeenCalled())
    const payload = state.saveConfig.mock.calls[0][1]
    expect(payload.units).toEqual([
      expect.objectContaining({ unitTypeId: 'unit-pallet', customerLabel: 'EURO PAL', ediCode: 'EPAL-2', isFavourite: true }),
    ])
    // Regression: echoing option prices back with only `value` used to wipe every other
    // override field server-side — a units save must not mention option prices at all.
    expect(payload.optionPrices).toEqual([])
  })

  it('cancelling the modal changes nothing', async () => {
    const user = userEvent.setup()
    render(<CustomerUnitsPanel customerId="cust-1" />)

    const row = (await screen.findByText('Europallet')).closest('tr')!
    await user.click(within(row).getByRole('button', { name: 'Bewerken' }))
    const dialog = await screen.findByRole('dialog')
    await user.clear(within(dialog).getByLabelText(/EDI-code/))
    await user.click(within(dialog).getByRole('button', { name: 'Annuleren' }))

    expect(state.saveConfig).not.toHaveBeenCalled()
    expect(screen.getByText('EPAL')).toBeInTheDocument()
  })

  it('links a new unit through the modal', async () => {
    const user = userEvent.setup()
    render(<CustomerUnitsPanel customerId="cust-1" />)

    await screen.findByText('Europallet')
    await user.click(screen.getByRole('button', { name: '+ Eenheid koppelen' }))
    const dialog = await screen.findByRole('dialog')
    await user.click(within(dialog).getByLabelText(/^Eenheid/))
    await user.click(await screen.findByRole('option', { name: 'Colli' }))
    await user.type(within(dialog).getByLabelText(/Excel-code/), 'COLLI_EXT')
    await user.click(within(dialog).getByRole('button', { name: 'Opslaan' }))

    await waitFor(() => expect(state.saveConfig).toHaveBeenCalled())
    const payload = state.saveConfig.mock.calls[0][1]
    expect(payload.units).toHaveLength(2)
    expect(payload.units[1]).toEqual(
      expect.objectContaining({ unitTypeId: 'unit-colli', excelCode: 'COLLI_EXT', isFavourite: true }),
    )
    expect(payload.optionPrices).toEqual([])
  })

  it('removes a mapping only after confirmation', async () => {
    const user = userEvent.setup()
    render(<CustomerUnitsPanel customerId="cust-1" />)

    const row = (await screen.findByText('Europallet')).closest('tr')!
    await user.click(within(row).getByRole('button', { name: 'Verwijderen' }))
    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByText(/Europallet/)).toBeInTheDocument()
    await user.click(within(dialog).getByRole('button', { name: 'Verwijderen' }))

    await waitFor(() => expect(state.saveConfig).toHaveBeenCalled())
    expect(state.saveConfig.mock.calls[0][1].units).toEqual([])
  })

  it('is read-only without tariffs.manage', async () => {
    auth.permissions = new Set(['tariffs.view'])
    render(<CustomerUnitsPanel customerId="cust-1" />)

    expect(await screen.findByText('Europallet')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Bewerken' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: '+ Eenheid koppelen' })).not.toBeInTheDocument()
  })
})

describe('CustomerUnitPricingPanel — toeslagen & diensten', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    auth.permissions = new Set(['tariffs.view', 'tariffs.manage'])
    state.rules = []
    state.agreements = []
    state.links = []
    state.bracketOverrides = []
    state.units = []
    state.serviceMeta = [
      {
        id: 'svc-10',
        code: 'VOOR10',
        name: 'Levering vóór 10:00',
        kind: 'Fixed',
        defaultValue: 15,
        isActive: true,
        sortOrder: 0,
        description: null,
        invoiceDescription: null,
        selectableInOrders: true,
        unitTypeId: null,
        unitTypeName: null,
        autoApply: true,
        onlyForAdr: false,
        timeConditions: [
          { kind: 'StopTimeBefore', stopScope: 'Unloading', timeOfDay: '10:00:00', priority: 0, allowStacking: false },
        ],
      },
      {
        id: 'svc-pick',
        code: 'PICK',
        name: 'Picking',
        kind: 'PerUnit',
        defaultValue: 1.25,
        isActive: true,
        sortOrder: 1,
        description: null,
        invoiceDescription: null,
        selectableInOrders: true,
        unitTypeId: 'unit-colli',
        unitTypeName: 'Colli',
        autoApply: true,
        onlyForAdr: false,
      },
    ]
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
          autoApplyOverride: null,
          effectiveAutoApply: true,
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
          autoApplyOverride: null,
          effectiveAutoApply: false,
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
          autoApplyOverride: null,
          effectiveAutoApply: false,
        },
        {
          serviceOptionId: 'svc-pick',
          name: 'Picking',
          kind: 'PerUnit',
          defaultValue: 1.25,
          customerValue: null,
          disabled: false,
          minimumAmount: null,
          invoiceDescription: null,
          effectiveFrom: null,
          effectiveUntil: null,
          effectiveValue: 1.25,
          source: 'Algemene standaard',
          autoApplyOverride: null,
          effectiveAutoApply: true,
        },
      ],
    }
    state.saveConfig.mockResolvedValue(state.config)
  })

  function renderPanel() {
    return render(
      <MemoryRouter>
        <CustomerUnitPricingPanel customerId="cust-1" />
      </MemoryRouter>,
    )
  }

  it('shows deviations + auto-applied services by default, with badges, standard value and condition chips', async () => {
    renderPanel()

    expect(await screen.findByText('Toeslagen & diensten')).toBeInTheDocument()
    // Deviating service: effective €10, "Afwijkend" badge, muted standard value.
    expect(screen.getByText('€ 10,00')).toBeInTheDocument()
    // Both the overridden and the disabled service carry the deviation badge.
    expect(screen.getAllByText('Afwijkend').length).toBeGreaterThan(0)
    expect(screen.getByText('Standaard: € 15,00')).toBeInTheDocument()
    // Time condition of the service surfaces as a readable chip.
    expect(screen.getByText('vóór 10:00 (lossen)')).toBeInTheDocument()
    // Disabled service is labelled, not silently priced.
    expect(screen.getByText('Uitgeschakeld')).toBeInTheDocument()
    // Auto-applied contract service is visible by default (planner needs it), source-neutral.
    expect(screen.getByText('Picking')).toBeInTheDocument()
    // A plain standard service without deviation stays behind the toggle (Sprint 4A).
    expect(screen.queryByText('Wachttijd')).not.toBeInTheDocument()
    await userEvent.click(screen.getByLabelText('Toon alle standaarddiensten'))
    expect(screen.getByText('Wachttijd')).toBeInTheDocument()
    expect(screen.getByText('€ 45,00/uur')).toBeInTheDocument()
  })

  it('edits an override in a modal and saves only that row', async () => {
    const user = userEvent.setup()
    renderPanel()

    await screen.findByText('Toeslagen & diensten')
    const row = screen.getByText('Levering vóór 10:00').closest('tr')!
    await user.click(within(row).getByRole('button', { name: 'Bewerken' }))
    const dialog = await screen.findByRole('dialog')

    const valueInput = within(dialog).getByLabelText(/Prijs voor deze klant/)
    expect(valueInput).toHaveValue(10)
    await user.clear(valueInput)
    await user.type(valueInput, '12.5')
    await user.click(within(dialog).getByRole('button', { name: 'Opslaan' }))

    await waitFor(() => expect(state.saveConfig).toHaveBeenCalled())
    const payload = state.saveConfig.mock.calls[0][1]
    // Single-row save: the backend leaves absent rows untouched.
    expect(payload.optionPrices).toHaveLength(1)
    expect(payload.optionPrices[0]).toEqual(expect.objectContaining({ serviceOptionId: 'svc-10', value: 12.5 }))
  })

  it('resets an override back to the global value after confirmation', async () => {
    const user = userEvent.setup()
    renderPanel()

    await screen.findByText('Toeslagen & diensten')
    const row = screen.getByText('Levering vóór 10:00').closest('tr')!
    await user.click(within(row).getByRole('button', { name: 'Algemene waarde opnieuw gebruiken' }))
    // Destructive in the business sense: an explicit confirmation replaces blur-autosave.
    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByText(/€ 15,00/)).toBeInTheDocument()
    await user.click(within(dialog).getByRole('button', { name: 'Algemene waarde opnieuw gebruiken' }))

    await waitFor(() => expect(state.saveConfig).toHaveBeenCalled())
    const payload = state.saveConfig.mock.calls[0][1]
    expect(payload.optionPrices).toHaveLength(1)
    expect(payload.optionPrices[0]).toEqual(
      expect.objectContaining({ serviceOptionId: 'svc-10', value: null, disabled: false, autoApplyOverride: null }),
    )
  })

  it('saves an auto-apply override through the modal tri-state', async () => {
    const user = userEvent.setup()
    renderPanel()

    await screen.findByText('Toeslagen & diensten')
    const row = screen.getByText('Picking').closest('tr')!
    await user.click(within(row).getByRole('button', { name: 'Bewerken' }))
    const dialog = await screen.findByRole('dialog')

    // Inherits the global AutoApply (true) with no override yet.
    expect(within(dialog).getByLabelText('Automatisch toepassen')).toHaveValue('Standaard (aan)')
    await user.click(within(dialog).getByLabelText('Automatisch toepassen'))
    await user.click(await within(dialog).findByRole('option', { name: 'Uit' }))
    await user.click(within(dialog).getByRole('button', { name: 'Opslaan' }))

    await waitFor(() => expect(state.saveConfig).toHaveBeenCalled())
    const payload = state.saveConfig.mock.calls[0][1]
    expect(payload.optionPrices[0]).toEqual(
      expect.objectContaining({ serviceOptionId: 'svc-pick', autoApplyOverride: false }),
    )
  })

  it('adds an override for a service that has none yet', async () => {
    const user = userEvent.setup()
    renderPanel()

    await screen.findByText('Toeslagen & diensten')
    await user.click(screen.getByRole('button', { name: '+ Toeslag toevoegen' }))
    const dialog = await screen.findByRole('dialog')

    await user.click(within(dialog).getByLabelText(/^Dienst/))
    await user.click(await screen.findByRole('option', { name: /Wachttijd/ }))
    await user.type(within(dialog).getByLabelText(/Prijs voor deze klant/), '55')
    await user.click(within(dialog).getByRole('button', { name: 'Opslaan' }))

    await waitFor(() => expect(state.saveConfig).toHaveBeenCalled())
    const payload = state.saveConfig.mock.calls[0][1]
    expect(payload.optionPrices[0]).toEqual(expect.objectContaining({ serviceOptionId: 'svc-wacht', value: 55 }))
  })
})

describe('CustomerUnitPricingPanel', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    auth.permissions = new Set(['tariffs.view', 'tariffs.manage'])
    state.config = makeConfig()
    state.units = []
    state.serviceMeta = []
    state.assignmentsByAgreement = {}
    state.agreements = [makeAgreement()]
    state.links = [makeLink()]
    state.bracketOverrides = []
    state.rules = [
      makeRule(),
      makeRule({ id: 'rule-2', name: 'Europallet Brussel (+4%)', effectiveFrom: '2099-10-01' }),
      makeRule({ id: 'rule-3', name: 'Europallet Brussel 2025', effectiveFrom: '2025-01-01', effectiveUntil: '2025-12-31' }),
    ]
    state.createRule.mockResolvedValue(makeRule())
  })

  function renderPanel() {
    return render(
      <MemoryRouter>
        <CustomerUnitPricingPanel customerId="cust-1" />
      </MemoryRouter>,
    )
  }

  it('merges current and planned rules with a status badge and keeps history behind a disclosure', async () => {
    renderPanel()

    expect(await screen.findByText('Afwijkende prijzen')).toBeInTheDocument()
    // Current + planned live in ONE table, distinguished by status badges.
    expect(screen.getByText('Actief')).toBeInTheDocument()
    expect(screen.getByText(/Gepland vanaf/)).toBeInTheDocument()
    expect(screen.getByText('Europallet Brussel (+4%)')).toBeInTheDocument()
    expect(screen.getByText('Prijshistoriek (1)')).toBeInTheDocument()
    // The tariff base table shows the commercial identity.
    expect(screen.getAllByText('Distributie 2026').length).toBeGreaterThan(0)
  })

  it('shows a shared table assigned to this customer with a badge, its adjustment and a planned change', async () => {
    state.links = [
      makeLink(),
      makeLink({
        agreementId: 'agr-shared',
        name: 'Distributie België 2026',
        isShared: true,
        minimumAmount: null,
        assignmentId: 'assign-1',
        assignmentPercentAdjustment: -5,
        plannedAdjustmentDate: '2027-01-01',
        plannedAdjustmentPercent: 3,
      }),
    ]

    renderPanel()

    expect(await screen.findByText('Distributie België 2026')).toBeInTheDocument()
    expect(screen.getByText('Gedeelde tabel')).toBeInTheDocument()
    expect(screen.getByText('-5%')).toBeInTheDocument()
    expect(screen.getByText(/Gepland: \+3% vanaf/)).toBeInTheDocument()
    // A shared table is never fully editable from a customer page — only its assignment.
    const sharedRow = screen.getByText('Distributie België 2026').closest('tr')!
    expect(within(sharedRow).queryByRole('button', { name: 'Bewerken' })).not.toBeInTheDocument()
    expect(within(sharedRow).getByRole('button', { name: 'Aanpassing wijzigen' })).toBeInTheDocument()
  })

  it('edits the customer assignment of a shared table without touching other customers', async () => {
    const user = userEvent.setup()
    state.links = [
      makeLink({
        agreementId: 'agr-shared',
        name: 'Distributie België 2026',
        isShared: true,
        assignmentId: 'assign-1',
        assignmentPercentAdjustment: -5,
      }),
    ]
    state.assignmentsByAgreement['agr-shared'] = [
      { id: 'assign-1', customerId: 'cust-1', customerName: 'Acme', percentAdjustment: -5, fixedAdjustment: null, effectiveFrom: null, effectiveUntil: null, notes: null },
      { id: 'assign-2', customerId: 'cust-2', customerName: 'Bevo', percentAdjustment: 2, fixedAdjustment: null, effectiveFrom: null, effectiveUntil: null, notes: 'apart' },
    ]
    state.saveAssignments.mockResolvedValue([])

    renderPanel()

    const sharedRow = (await screen.findByText('Distributie België 2026')).closest('tr')!
    await user.click(within(sharedRow).getByRole('button', { name: 'Aanpassing wijzigen' }))
    const dialog = await screen.findByRole('dialog')
    const percent = within(dialog).getByLabelText(/Percentage/)
    await user.clear(percent)
    await user.type(percent, '-7.5')
    await user.click(within(dialog).getByRole('button', { name: 'Opslaan' }))

    await waitFor(() => expect(state.saveAssignments).toHaveBeenCalled())
    const [agreementId, rows] = state.saveAssignments.mock.calls[0]
    expect(agreementId).toBe('agr-shared')
    expect(rows).toEqual([
      expect.objectContaining({ customerId: 'cust-1', percentAdjustment: -7.5 }),
      // The other customer's assignment is echoed back untouched.
      expect.objectContaining({ customerId: 'cust-2', percentAdjustment: 2, notes: 'apart' }),
    ])
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
    state.links = [
      makeLink({ agreementId: 'agr-nl', name: 'NL Distributie', baseAgreementId: 'agr-base', baseAgreementName: 'Distributie België' }),
    ]
    state.updateAgreement.mockResolvedValue(state.agreements[1])

    renderPanel()

    expect(await screen.findByText('Afgeleid van Distributie België')).toBeInTheDocument()

    const nlRow = screen.getByText('NL Distributie').closest('tr')!
    await user.click(within(nlRow).getByRole('button', { name: 'Bewerken' }))
    const dialog = await screen.findByRole('dialog')

    expect(within(dialog).getByText(/Deze tabel gebruikt de prijsregels van Distributie België/)).toBeInTheDocument()
    expect(within(dialog).getByLabelText('Basistabel')).toHaveValue('Distributie België')
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
    renderPanel()

    await user.click(await screen.findByRole('button', { name: '+ Prijsregel' }))
    const dialog = await screen.findByRole('dialog')

    // Default: Per eenheid with staffels — unit select + brackets, no hourly fields.
    expect(within(dialog).getByLabelText(/Prijsbasis/)).toHaveValue('Per eenheid')
    expect(within(dialog).getByLabelText(/^Eenheid/)).toBeInTheDocument()
    expect(within(dialog).getByText('Staffels (aantal)')).toBeInTheDocument()
    expect(within(dialog).queryByLabelText(/Minimum aantal uur/)).not.toBeInTheDocument()

    // Per uur: hourly fields appear, staffels disappear, unit select stays (tijd-eenheid).
    await pickOption(user, within(dialog).getByLabelText(/Prijsbasis/), 'Per uur')
    expect(within(dialog).getByLabelText(/Minimum aantal uur/)).toBeInTheDocument()
    expect(within(dialog).getByLabelText(/Afrondingsstap/)).toBeInTheDocument()
    expect(within(dialog).queryByText('Staffels (aantal)')).not.toBeInTheDocument()

    // Forfait: fixed price only — no unit, no staffels, no hourly fields.
    await pickOption(user, within(dialog).getByLabelText(/Prijsbasis/), 'Forfait / vaste prijs')
    expect(within(dialog).getByLabelText(/Vaste prijs/)).toBeInTheDocument()
    expect(within(dialog).queryByLabelText(/^Eenheid/)).not.toBeInTheDocument()
    expect(within(dialog).queryByLabelText(/Minimum aantal uur/)).not.toBeInTheDocument()

    // Per kilometer: km price + basisbedrag.
    await pickOption(user, within(dialog).getByLabelText(/Prijsbasis/), 'Per kilometer')
    expect(within(dialog).getByLabelText(/Prijs per km/)).toBeInTheDocument()
    expect(within(dialog).getByLabelText(/Basisbedrag/)).toBeInTheDocument()
  })

  it('bracket mode select renders and round-trips; dimension columns appear when toggled', async () => {
    const user = userEvent.setup()
    state.units = [
      { id: 'unit-pallet', code: 'EUROPALLET', name: 'Europallet', isActive: true, sortOrder: 0, allowForOrderEntry: true, allowForPricing: true },
    ]
    renderPanel()

    await user.click(await screen.findByRole('button', { name: '+ Prijsregel' }))
    const dialog = await screen.findByRole('dialog')

    // Default QuantityBracket basis shows the calculation-mode select, defaulting to Absoluut.
    const modeSelect = within(dialog).getByLabelText('Berekeningswijze staffel')
    expect(modeSelect).toHaveValue('Absoluut (prijs van de staffel)')

    // Dimension columns are hidden until toggled.
    expect(within(dialog).queryByLabelText('Staffel 1 gewicht tot (kg)')).not.toBeInTheDocument()
    await user.click(within(dialog).getByLabelText('Extra dimensies (gewicht/volume/ldm per staffel)'))
    expect(within(dialog).getByLabelText('Staffel 1 gewicht tot (kg)')).toBeInTheDocument()
    expect(within(dialog).getByLabelText('Staffel 1 volume tot (m³)')).toBeInTheDocument()
    expect(within(dialog).getByLabelText('Staffel 1 ldm tot')).toBeInTheDocument()

    // Round-trip: selecting PerNextUnit and saving sends it through in the payload.
    await pickOption(user, modeSelect, /Per volgende eenheid/)
    expect(modeSelect).toHaveValue('Per volgende eenheid (som per stuk)')

    await user.type(within(dialog).getByLabelText(/^Naam/), 'Progressief')
    await user.type(within(dialog).getByLabelText('Staffel 1 van'), '1')
    await user.type(within(dialog).getByLabelText('Staffel 1 prijs'), '60')
    await user.click(within(dialog).getByRole('button', { name: 'Opslaan' }))

    await waitFor(() => expect(state.createRule).toHaveBeenCalled())
    const payload = state.createRule.mock.calls[0][0]
    expect(payload).toEqual(expect.objectContaining({ bracketMode: 'PerNextUnit' }))
  })

  it('surfaces bracket deviations with standard vs customer price and a rate-table deep link', async () => {
    state.bracketOverrides = [
      {
        id: 'ovr-1',
        priceRuleId: 'rule-1',
        ruleName: 'Pallets gedeeld',
        agreementId: 'agr-shared',
        agreementName: 'Distributie België 2026',
        unitTypeName: 'Europallet',
        fromQuantity: 2,
        toQuantity: 2,
        weightToKg: null,
        volumeToM3: null,
        loadingMetersTo: null,
        standardPrice: 80,
        standardPricePerExtraUnit: null,
        price: 72,
        pricePerExtraUnit: null,
        effectiveFrom: null,
        effectiveUntil: null,
        orphaned: false,
      },
      {
        id: 'ovr-2',
        priceRuleId: 'rule-1',
        ruleName: 'Pallets gedeeld',
        agreementId: 'agr-shared',
        agreementName: 'Distributie België 2026',
        unitTypeName: 'Europallet',
        fromQuantity: 3,
        toQuantity: null,
        weightToKg: null,
        volumeToM3: null,
        loadingMetersTo: null,
        standardPrice: null,
        standardPricePerExtraUnit: null,
        price: 99,
        pricePerExtraUnit: null,
        effectiveFrom: null,
        effectiveUntil: null,
        orphaned: true,
      },
    ]
    renderPanel()

    // Count in the summary, rows behind the disclosure (details content renders in jsdom).
    expect(await screen.findByText('Staffelafwijkingen (2)')).toBeInTheDocument()
    expect(screen.getByText('2 Europallet')).toBeInTheDocument()
    expect(screen.getByText('€ 80,00')).toBeInTheDocument()
    expect(screen.getByText('€ 72,00')).toBeInTheDocument()
    // An override whose bracket row disappeared is flagged, never silently shown as current.
    expect(screen.getByText('Verweesd')).toBeInTheDocument()
    // Editing lives on the rate-table detail — the customer page deep-links.
    const links2 = screen.getAllByRole('link', { name: 'Bekijken in tarieventabel' })
    expect(links2[0]).toHaveAttribute('href', '/pricing/tables/agr-shared')
  })

  it('renders no bracket-deviation block for a customer without overrides', async () => {
    renderPanel()

    await screen.findByText('Afwijkende prijzen')
    expect(screen.queryByText(/Staffelafwijkingen/)).not.toBeInTheDocument()
  })

  it('shows a helpful empty state per section instead of bare placeholder text', async () => {
    state.links = []
    state.rules = []
    renderPanel()

    expect(await screen.findByText(/Zonder tarieventabel of prijsafspraak kan er geen prijs berekend worden/)).toBeInTheDocument()
    expect(screen.getByText(/deze klant volgt de tariefbasis hierboven/)).toBeInTheDocument()
    // Empty states offer the relevant action for managers.
    expect(screen.getAllByRole('button', { name: '+ Prijsafspraak' }).length).toBeGreaterThan(0)
  })

  it('hides management actions without tariffs.manage', async () => {
    auth.permissions = new Set(['tariffs.view'])
    renderPanel()

    await screen.findByText('Afwijkende prijzen')
    expect(screen.queryByRole('button', { name: '+ Prijsregel' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: '+ Prijsafspraak' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Bewerken' })).not.toBeInTheDocument()
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
        agreementId: null,
        effectiveDate: '2099-10-01',
        percent: 4,
        amountDelta: null,
        roundingStep: null,
        basisFilter: null,
        unitTypeIdFilter: null,
        status: 'Gepland',
        statusCode: 'Planned' as const,
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
    state.cancelAdjustment.mockResolvedValue({ ...state.adjustments[0], status: 'Geannuleerd', statusCode: 'Cancelled' as const })
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
    expect(await within(dialog).findByText('€ 45,00 → € 46,80')).toBeInTheDocument()
    expect(within(dialog).getByText('€ 70,00 → € 72,80')).toBeInTheDocument()
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
