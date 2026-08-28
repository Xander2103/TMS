import { describe, expect, it, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { TransportOrderForm } from '../TransportOrderForm'

const auth = vi.hoisted(() => ({ permissions: new Set<string>(['locations.create']) }))

vi.mock('../../../auth/authContextValue', () => ({
  useAuth: () => ({ hasPermission: (code: string) => auth.permissions.has(code) }),
}))
vi.mock('../../../customers/api/customersApi', () => ({
  searchCustomers: () =>
    Promise.resolve({ items: [{ id: 'cust-1', name: 'Klant X', customerNumber: 'KL-1' }], total: 1 }),
  getCustomer: () => Promise.resolve({ id: 'cust-1', isBlocked: false }),
}))
vi.mock('../../../legal-entities/api/legalEntitiesApi', () => ({
  getLegalEntityOptions: () => Promise.resolve([]),
}))
vi.mock('../../../master-data/hooks/useLookupOptions', () => ({
  useLookupOptions: () => ({
    options: [
      { id: 'u-pallet', code: 'EUROPALLET', name: 'Europallet' },
      { id: 'u-colli', code: 'COLLI', name: 'Colli' },
      { id: 'u-kg', code: 'KG', name: 'Kilogram' },
    ],
    isLoading: false,
    error: null,
  }),
}))
vi.mock('../../../master-data/components/LookupSelect', () => ({
  LookupSelect: ({ id }: { id?: string }) => <input id={id} aria-label="lookup" />,
}))
vi.mock('../../../locations/components/LocationSelect', () => ({
  LocationSelect: ({ id }: { id?: string }) => <input id={id} aria-label="locatie" />,
}))
vi.mock('../../../reference/components/CountryCombobox', () => ({
  CountryCombobox: ({ id }: { id?: string }) => <input id={id} aria-label="Land" />,
}))

const previewSpy = vi.hoisted(() => vi.fn())
// Default previewPrice() response, restored in beforeEach (via mockReset + mockResolvedValue)
// so a test that installs its own persistent `previewSpy.mockResolvedValue(...)` can never leak
// into a later test — only `mockResolvedValueOnce` is safe to leave unconsumed across tests.
const defaultPreviewResult = vi.hoisted(() => ({
  lines: [
    { label: '3 × Europallet (zone Z3)', amount: 145, source: 'Pallets klant X', informational: false },
    { label: 'Dieseltoeslag 8%', amount: 11.6, source: 'Dieseltoeslag', informational: true },
  ],
  total: 145,
  totalWithInformational: 156.6,
  currency: 'EUR',
  zoneCode: 'Z3',
  zoneName: 'Zone 3',
  requiresManualPrice: false,
  serviceLines: [],
}))

vi.mock('../../../tarification/api/pricingApi', async () => {
  const actual = await vi.importActual<typeof import('../../../tarification/api/pricingApi')>(
    '../../../tarification/api/pricingApi',
  )
  return {
    ...actual,
    listServiceOptions: () =>
      Promise.resolve([
        {
          id: 'opt-8', code: 'VOOR8', name: 'Levering vóór 08:00', kind: 'Fixed', defaultValue: 25,
          isActive: true, sortOrder: 0, description: null, invoiceDescription: null, selectableInOrders: true,
        },
        {
          id: 'opt-wacht', code: 'WACHT', name: 'Wachttijd', kind: 'PerHour', defaultValue: 45,
          isActive: true, sortOrder: 1, description: null, invoiceDescription: null, selectableInOrders: true,
        },
        {
          id: 'opt-opslag', code: 'OPSLAG', name: 'Opslag', kind: 'PerDay', defaultValue: 0.25,
          isActive: true, sortOrder: 2, description: null, invoiceDescription: null, selectableInOrders: true,
        },
        {
          id: 'opt-paldag', code: 'PALDAG', name: 'Palletopslag', kind: 'PerPalletDay', defaultValue: 0.2,
          isActive: true, sortOrder: 3, description: null, invoiceDescription: null, selectableInOrders: true,
        },
      ]),
    getCustomerPricingConfig: () =>
      Promise.resolve({
        preferredUnits: [
          {
            unitTypeId: 'u-pallet', code: 'EUROPALLET', name: 'Europallet', sortOrder: 0,
            customerLabel: 'EURO PAL', ediCode: 'EPAL', excelCode: null, isFavourite: true,
          },
        ],
        serviceOptions: [],
      }),
    listUnitTypeMaster: () =>
      Promise.resolve([
        {
          id: 'u-pallet', code: 'EUROPALLET', name: 'Europallet', description: null, isActive: true, sortOrder: 0,
          allowForOrderEntry: true, allowForPricing: true, category: 'Packaging', decimals: 0, symbol: null,
          dimensionBehavior: 'DefaultButOverridable',
          defaultLengthCm: 120, defaultWidthCm: 80, defaultHeightCm: null,
          defaultWeightKg: null, maxWeightKg: null, defaultVolumeM3: null,
          defaultLoadingMeters: null, defaultPalletPlaces: null,
        },
      ]),
    previewPrice: previewSpy.mockResolvedValue(defaultPreviewResult),
  }
})

function renderForm(onSubmit = vi.fn().mockResolvedValue(undefined)) {
  render(
    <MemoryRouter>
      <TransportOrderForm submitLabel="Opdracht aanmaken" onSubmit={onSubmit} />
    </MemoryRouter>,
  )
  return { onSubmit }
}

/** Fills customer + both stop cities so submission only depends on the goods-description rule. */
async function fillMinimalRouteAndCustomer() {
  await waitFor(() => expect(screen.getByLabelText('Klant *')).toBeInTheDocument())
  await userEvent.selectOptions(screen.getByLabelText('Klant *'), 'cust-1')

  await userEvent.click(screen.getByRole('tab', { name: /Route & stops/ }))
  for (const cityInput of screen.getAllByLabelText('Plaats *')) {
    await userEvent.type(cityInput, 'Gent')
  }
}

describe('TransportOrderForm sections + pricing', () => {
  beforeEach(() => {
    auth.permissions = new Set(['locations.create'])
    previewSpy.mockReset()
    previewSpy.mockResolvedValue(defaultPreviewResult)
  })

  it('renders the seven tabs and preserves values across switches', async () => {
    renderForm()
    for (const label of ['Algemeen', 'Route & stops', 'Goederen', 'Services & toeslagen', 'Documenten', 'Prijs', 'Samenvatting']) {
      expect(screen.getByRole('tab', { name: new RegExp(label) })).toBeInTheDocument()
    }

    await waitFor(() => expect(screen.getByLabelText('Klant *')).toBeInTheDocument())
    await userEvent.selectOptions(screen.getByLabelText('Klant *'), 'cust-1')

    await userEvent.click(screen.getByRole('tab', { name: /Goederen/ }))
    await userEvent.type(screen.getByLabelText('Aantal'), '3')

    await userEvent.click(screen.getByRole('tab', { name: /Algemeen/ }))
    expect(screen.getByLabelText('Klant *')).toHaveValue('cust-1')
    await userEvent.click(screen.getByRole('tab', { name: /Goederen/ }))
    expect(screen.getByLabelText('Aantal')).toHaveValue(3)
  })

  it('lists customer-preferred units first with a toggle for the rest', async () => {
    renderForm()
    await waitFor(() => expect(screen.getByLabelText('Klant *')).toBeInTheDocument())
    await userEvent.selectOptions(screen.getByLabelText('Klant *'), 'cust-1')
    await userEvent.click(screen.getByRole('tab', { name: /Goederen/ }))

    const unitSelect = screen.getByLabelText('Eenheid')
    await waitFor(() => expect(unitSelect.querySelector('optgroup[label="Eenheden van deze klant"]')).toBeTruthy())
    expect(unitSelect.querySelectorAll('option')).toHaveLength(2) // placeholder + preferred only

    await userEvent.click(screen.getByRole('button', { name: 'Andere eenheden tonen' }))
    expect(unitSelect.querySelector('optgroup[label="Andere eenheden"]')).toBeTruthy()
    expect(unitSelect.querySelectorAll('option').length).toBeGreaterThan(2)
  })

  it('renders the four service group headings and the add-service button', async () => {
    renderForm()
    await userEvent.click(screen.getByRole('tab', { name: /Services & toeslagen/ }))
    expect(screen.getByRole('heading', { name: 'Automatisch toegepast' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Handmatig geselecteerd' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Niet toegepast' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: '+ Dienst of toeslag toevoegen' })).toBeInTheDocument()
  })

  it('renders the "Laad- en lostijd" section with the effective value and source from the preview', async () => {
    previewSpy.mockResolvedValueOnce({
      lines: [{ label: '3 × Europallet (zone Z3)', amount: 145, source: 'Pallets klant X', informational: false }],
      total: 145,
      totalWithInformational: 145,
      currency: 'EUR',
      zoneCode: 'Z3',
      zoneName: 'Zone 3',
      requiresManualPrice: false,
      serviceLines: [],
      includedTimeInfo: {
        includedLoadingMinutes: 30, includedUnloadingMinutes: null, includedCombinedMinutes: null,
        extraHourlyRate: 75, source: 'Contract',
      },
    })
    renderForm()
    await waitFor(() => expect(screen.getByLabelText('Klant *')).toBeInTheDocument())
    await userEvent.selectOptions(screen.getByLabelText('Klant *'), 'cust-1')
    await userEvent.click(screen.getByRole('tab', { name: /Goederen/ }))
    await userEvent.type(screen.getByLabelText('Aantal'), '3')
    await userEvent.selectOptions(screen.getByLabelText('Eenheid'), 'EUROPALLET')

    await userEvent.click(screen.getByRole('tab', { name: /Services & toeslagen/ }))
    expect(screen.getByRole('heading', { name: 'Laad- en lostijd' })).toBeInTheDocument()
    await waitFor(() => expect(screen.getByText(/Inbegrepen laadtijd: 30 minuten/)).toBeInTheDocument())
    expect(screen.getByText('Bron: Klantcontract')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Afwijken voor deze order' })).toBeInTheDocument()
    expect(screen.queryByLabelText('Inbegrepen laadtijd (minuten)')).not.toBeInTheDocument()
  })

  it('reveals the five override inputs, marks the source as an order override, and resets on "Terugzetten"', async () => {
    previewSpy.mockResolvedValue({
      lines: [{ label: '3 × Europallet (zone Z3)', amount: 145, source: 'Pallets klant X', informational: false }],
      total: 145,
      totalWithInformational: 145,
      currency: 'EUR',
      zoneCode: 'Z3',
      zoneName: 'Zone 3',
      requiresManualPrice: false,
      serviceLines: [],
      includedTimeInfo: {
        includedLoadingMinutes: 30, includedUnloadingMinutes: null, includedCombinedMinutes: null,
        extraHourlyRate: 75, source: 'Contract',
      },
    })
    renderForm()
    await waitFor(() => expect(screen.getByLabelText('Klant *')).toBeInTheDocument())
    await userEvent.selectOptions(screen.getByLabelText('Klant *'), 'cust-1')
    await userEvent.click(screen.getByRole('tab', { name: /Goederen/ }))
    await userEvent.type(screen.getByLabelText('Aantal'), '3')
    await userEvent.selectOptions(screen.getByLabelText('Eenheid'), 'EUROPALLET')

    await userEvent.click(screen.getByRole('tab', { name: /Services & toeslagen/ }))
    await waitFor(() => expect(screen.getByText('Bron: Klantcontract')).toBeInTheDocument())

    await userEvent.click(screen.getByRole('button', { name: 'Afwijken voor deze order' }))
    expect(screen.getByLabelText('Inbegrepen laadtijd (minuten)')).toBeInTheDocument()
    expect(screen.getByLabelText('Inbegrepen lostijd (minuten)')).toBeInTheDocument()
    expect(screen.getByLabelText('Uurtarief extra tijd (€)')).toBeInTheDocument()
    expect(screen.getByLabelText('Afronding (minuten)')).toBeInTheDocument()
    expect(screen.getByLabelText('Minimum extra tijd (minuten)')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Terugzetten naar contractwaarde' })).not.toBeInTheDocument()

    await userEvent.type(screen.getByLabelText('Inbegrepen laadtijd (minuten)'), '45')
    expect(screen.getByText('Bron: Afwijking op order')).toBeInTheDocument()
    const resetButton = screen.getByRole('button', { name: 'Terugzetten naar contractwaarde' })

    await userEvent.click(resetButton)
    expect(screen.getByLabelText('Inbegrepen laadtijd (minuten)')).toHaveValue(null)
    expect(screen.getByText('Bron: Klantcontract')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Terugzetten naar contractwaarde' })).not.toBeInTheDocument()
  })

  it('shows "Bron: Geen contractwaarde" with a dash when nothing is configured and no override is set', async () => {
    previewSpy.mockResolvedValueOnce({
      lines: [{ label: '3 × Europallet', amount: 90, source: 'Pallets klant X', informational: false }],
      total: 90,
      totalWithInformational: 90,
      currency: 'EUR',
      zoneCode: null,
      zoneName: null,
      requiresManualPrice: false,
      serviceLines: [],
      includedTimeInfo: {
        includedLoadingMinutes: null, includedUnloadingMinutes: null, includedCombinedMinutes: null,
        extraHourlyRate: null, source: 'Geen',
      },
    })
    renderForm()
    await waitFor(() => expect(screen.getByLabelText('Klant *')).toBeInTheDocument())
    await userEvent.selectOptions(screen.getByLabelText('Klant *'), 'cust-1')
    await userEvent.click(screen.getByRole('tab', { name: /Goederen/ }))
    await userEvent.type(screen.getByLabelText('Aantal'), '3')
    await userEvent.selectOptions(screen.getByLabelText('Eenheid'), 'EUROPALLET')

    await userEvent.click(screen.getByRole('tab', { name: /Services & toeslagen/ }))
    await waitFor(() => expect(screen.getByText('Bron: Geen contractwaarde')).toBeInTheDocument())
    expect(screen.getByText(/Inbegrepen laadtijd: —/)).toBeInTheDocument()
    expect(screen.getByText(/Inbegrepen lostijd: —/)).toBeInTheDocument()
  })

  it('shows a single combined row when the contract configures a combined included time', async () => {
    previewSpy.mockResolvedValueOnce({
      lines: [{ label: '3 × Europallet', amount: 90, source: 'Pallets klant X', informational: false }],
      total: 90,
      totalWithInformational: 90,
      currency: 'EUR',
      zoneCode: null,
      zoneName: null,
      requiresManualPrice: false,
      serviceLines: [],
      includedTimeInfo: {
        includedLoadingMinutes: null, includedUnloadingMinutes: null, includedCombinedMinutes: 60,
        extraHourlyRate: 75, source: 'Contract',
      },
    })
    renderForm()
    await waitFor(() => expect(screen.getByLabelText('Klant *')).toBeInTheDocument())
    await userEvent.selectOptions(screen.getByLabelText('Klant *'), 'cust-1')
    await userEvent.click(screen.getByRole('tab', { name: /Goederen/ }))
    await userEvent.type(screen.getByLabelText('Aantal'), '3')
    await userEvent.selectOptions(screen.getByLabelText('Eenheid'), 'EUROPALLET')

    await userEvent.click(screen.getByRole('tab', { name: /Services & toeslagen/ }))
    await waitFor(() => expect(screen.getByText(/Inbegrepen laad- en lostijd \(gecombineerd\): 60 minuten/)).toBeInTheDocument(), { timeout: 3000 })
    expect(screen.queryByText(/^Inbegrepen laadtijd:/)).not.toBeInTheDocument()
    expect(screen.queryByText(/^Inbegrepen lostijd:/)).not.toBeInTheDocument()
  })

  it('keeps showing the combined row when only the rate is overridden — a minutes-only override switches the mode', async () => {
    previewSpy.mockResolvedValueOnce({
      lines: [{ label: '3 × Europallet', amount: 90, source: 'Pallets klant X', informational: false }],
      total: 90,
      totalWithInformational: 90,
      currency: 'EUR',
      zoneCode: null,
      zoneName: null,
      requiresManualPrice: false,
      serviceLines: [],
      includedTimeInfo: {
        includedLoadingMinutes: null, includedUnloadingMinutes: null, includedCombinedMinutes: 60,
        extraHourlyRate: 75, source: 'Contract',
      },
    })
    renderForm()
    await waitFor(() => expect(screen.getByLabelText('Klant *')).toBeInTheDocument())
    await userEvent.selectOptions(screen.getByLabelText('Klant *'), 'cust-1')
    await userEvent.click(screen.getByRole('tab', { name: /Goederen/ }))
    await userEvent.type(screen.getByLabelText('Aantal'), '3')
    await userEvent.selectOptions(screen.getByLabelText('Eenheid'), 'EUROPALLET')

    await userEvent.click(screen.getByRole('tab', { name: /Services & toeslagen/ }))
    await waitFor(() => expect(screen.getByText(/Inbegrepen laad- en lostijd \(gecombineerd\): 60 minuten/)).toBeInTheDocument(), { timeout: 3000 })

    await userEvent.click(screen.getByRole('button', { name: 'Afwijken voor deze order' }))
    await userEvent.type(screen.getByLabelText('Uurtarief extra tijd (€)'), '90')

    // Rate-only override: still combined mode on the backend (ResolveIncludedTime only switches
    // to per-activity when a minutes override is set) — the combined row must keep showing, now
    // sourced from the order.
    expect(screen.getByText(/Inbegrepen laad- en lostijd \(gecombineerd\): 60 minuten/)).toBeInTheDocument()
    expect(screen.getByText('Bron: Afwijking op order')).toBeInTheDocument()
    expect(screen.queryByText(/^Inbegrepen laadtijd:/)).not.toBeInTheDocument()
    expect(screen.queryByText(/^Inbegrepen lostijd:/)).not.toBeInTheDocument()
  })

  it('disables the "Laad- en lostijd" section with a hint when pricing is a one-off agreement', async () => {
    renderForm()
    await waitFor(() => expect(screen.getByLabelText('Klant *')).toBeInTheDocument())
    await userEvent.selectOptions(screen.getByLabelText('Klant *'), 'cust-1')
    await userEvent.click(screen.getByRole('tab', { name: /^Prijs$/ }))
    await userEvent.click(screen.getByRole('radio', { name: 'Eenmalige prijsafspraak' }))

    await userEvent.click(screen.getByRole('tab', { name: /Services & toeslagen/ }))
    expect(screen.getByRole('heading', { name: 'Laad- en lostijd' })).toBeInTheDocument()
    expect(screen.getByText('Bij een eenmalige prijsafspraak gebruik je de eenmalige laad- en lostijdvelden.')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Afwijken voor deze order' })).not.toBeInTheDocument()
  })

  it('includes the included-time override fields in the submit payload, mapping empty strings to null', async () => {
    const { onSubmit } = renderForm()
    await fillMinimalRouteAndCustomer()

    await userEvent.click(screen.getByRole('tab', { name: /Goederen/ }))
    await userEvent.click(screen.getByRole('button', { name: '+ Goederenlijn' }))
    await userEvent.type(screen.getByLabelText('Omschrijving'), '2 europallets onderdelen')

    await userEvent.click(screen.getByRole('tab', { name: /Services & toeslagen/ }))
    await userEvent.click(screen.getByRole('button', { name: 'Afwijken voor deze order' }))
    await userEvent.type(screen.getByLabelText('Inbegrepen laadtijd (minuten)'), '45')
    await userEvent.type(screen.getByLabelText('Uurtarief extra tijd (€)'), '80')
    await userEvent.type(screen.getByLabelText('Afronding (minuten)'), '15')

    await userEvent.click(screen.getByRole('button', { name: 'Opdracht aanmaken' }))

    await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(1))
    expect(onSubmit).toHaveBeenCalledWith(expect.objectContaining({
      includedLoadingMinutesOverride: 45,
      includedUnloadingMinutesOverride: null,
      extraTimeHourlyRateOverride: 80,
      extraTimeRoundingStepMinutes: 15,
      extraTimeMinimumBillableMinutes: null,
    }))
  }, 15000)

  it('lists an informational not-applied service line from the preview under "Niet toegepast"', async () => {
    previewSpy.mockResolvedValueOnce({
      lines: [
        { label: '3 × Europallet (zone Z3)', amount: 145, source: 'Pallets klant X', informational: false },
        { label: 'Wachttijd: geef het aantal uur op', amount: 0, source: 'Algemene standaard', informational: true },
        { label: 'Dieseltoeslag 8%', amount: 11.6, source: 'Dieseltoeslag', informational: true },
      ],
      total: 145,
      totalWithInformational: 156.6,
      currency: 'EUR',
      zoneCode: 'Z3',
      zoneName: 'Zone 3',
      requiresManualPrice: false,
      serviceLines: [],
    })
    renderForm()
    await waitFor(() => expect(screen.getByLabelText('Klant *')).toBeInTheDocument())
    await userEvent.selectOptions(screen.getByLabelText('Klant *'), 'cust-1')
    await userEvent.click(screen.getByRole('tab', { name: /Goederen/ }))
    await userEvent.type(screen.getByLabelText('Aantal'), '3')
    await userEvent.selectOptions(screen.getByLabelText('Eenheid'), 'EUROPALLET')

    await userEvent.click(screen.getByRole('tab', { name: /Services & toeslagen/ }))
    await waitFor(() => expect(screen.getByText('Wachttijd: geef het aantal uur op')).toBeInTheDocument())
    // Non-service informational lines (e.g. the diesel surcharge) never show up in this list.
    expect(screen.queryByText(/Dieseltoeslag 8%/)).not.toBeInTheDocument()
  })

  it('the add panel shows the calculation method, a live price indication and asks a quantity for hourly services', async () => {
    renderForm()
    await waitFor(() => expect(screen.getByLabelText('Klant *')).toBeInTheDocument())
    await userEvent.selectOptions(screen.getByLabelText('Klant *'), 'cust-1')
    await userEvent.click(screen.getByRole('tab', { name: /Services/ }))

    await userEvent.click(screen.getByRole('button', { name: '+ Dienst of toeslag toevoegen' }))
    await userEvent.selectOptions(screen.getByLabelText('Dienst of toeslag'), 'opt-wacht')

    // Berekeningswijze + prices come from the API (global default / customer tariff) — never
    // hardcoded here.
    expect(screen.getByLabelText('Berekeningswijze')).toHaveValue('Per uur')
    const quantityInput = await screen.findByLabelText('Aantal uur — Wachttijd')
    await userEvent.type(quantityInput, '3')
    expect(screen.getByText(/Prijsindicatie: € 135,00/)).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Toevoegen' }))

    // Moved into "Handmatig geselecteerd": MANUEEL badge, Verwijderen control, quantity retained.
    const row = screen.getByText('Wachttijd').closest('.tof-service-option') as HTMLElement
    expect(within(row).getByText('MANUEEL')).toBeInTheDocument()
    expect(within(row).getByRole('button', { name: 'Verwijderen' })).toBeInTheDocument()
    expect(screen.getByLabelText('Aantal uur — Wachttijd')).toHaveValue(3)

    await waitFor(() => expect(previewSpy).toHaveBeenCalledWith(expect.objectContaining({
      services: expect.arrayContaining([
        expect.objectContaining({ serviceOptionId: 'opt-wacht', quantity: 3 }),
      ]),
    })), { timeout: 3000 })
  })

  it('asks a day count for per-day services and derives pallet-days for per-pallet-day services', async () => {
    renderForm()
    await waitFor(() => expect(screen.getByLabelText('Klant *')).toBeInTheDocument())
    await userEvent.selectOptions(screen.getByLabelText('Klant *'), 'cust-1')
    await userEvent.click(screen.getByRole('tab', { name: /Services/ }))

    // Per-day: a lone day-count input; sent as the billable quantity + dayCount.
    await userEvent.click(screen.getByRole('button', { name: '+ Dienst of toeslag toevoegen' }))
    await userEvent.selectOptions(screen.getByLabelText('Dienst of toeslag'), 'opt-opslag')
    await userEvent.type(await screen.findByLabelText('Aantal dagen — Opslag'), '12')
    await userEvent.click(screen.getByRole('button', { name: 'Toevoegen' }))
    await waitFor(() => expect(previewSpy).toHaveBeenCalledWith(expect.objectContaining({
      services: expect.arrayContaining([
        expect.objectContaining({ serviceOptionId: 'opt-opslag', quantity: 12, dayCount: 12 }),
      ]),
    })), { timeout: 3000 })

    // Per-pallet-day: pallets × days auto-fills the (still editable) pallet-days quantity.
    await userEvent.click(screen.getByRole('button', { name: '+ Dienst of toeslag toevoegen' }))
    await userEvent.selectOptions(screen.getByLabelText('Dienst of toeslag'), 'opt-paldag')
    await userEvent.type(await screen.findByLabelText('Pallets — Palletopslag'), '4')
    await userEvent.type(screen.getByLabelText('Dagen — Palletopslag'), '12')
    expect(screen.getByLabelText('Pallet-dagen — Palletopslag')).toHaveValue(48)
    await userEvent.click(screen.getByRole('button', { name: 'Toevoegen' }))
    await waitFor(() => expect(previewSpy).toHaveBeenCalledWith(expect.objectContaining({
      services: expect.arrayContaining([
        expect.objectContaining({ serviceOptionId: 'opt-paldag', quantity: 48, palletCount: 4, dayCount: 12 }),
      ]),
    })), { timeout: 3000 })

    // Manual correction of the derived quantity wins.
    const palletDays = screen.getByLabelText('Pallet-dagen — Palletopslag')
    await userEvent.clear(palletDays)
    await userEvent.type(palletDays, '50')
    await waitFor(() => expect(previewSpy).toHaveBeenCalledWith(expect.objectContaining({
      services: expect.arrayContaining([
        expect.objectContaining({ serviceOptionId: 'opt-paldag', quantity: 50 }),
      ]),
    })), { timeout: 3000 })
  })

  it('removing a manually selected service unticks it and clears its entered inputs', async () => {
    renderForm()
    await waitFor(() => expect(screen.getByLabelText('Klant *')).toBeInTheDocument())
    await userEvent.selectOptions(screen.getByLabelText('Klant *'), 'cust-1')
    await userEvent.click(screen.getByRole('tab', { name: /Services/ }))

    await userEvent.click(screen.getByRole('button', { name: '+ Dienst of toeslag toevoegen' }))
    await userEvent.selectOptions(screen.getByLabelText('Dienst of toeslag'), 'opt-8')
    await userEvent.click(screen.getByRole('button', { name: 'Toevoegen' }))
    expect(screen.getByText('MANUEEL')).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Verwijderen' }))
    expect(screen.queryByText('MANUEEL')).not.toBeInTheDocument()

    // Back in the add panel's dropdown, offered again.
    await userEvent.click(screen.getByRole('button', { name: '+ Dienst of toeslag toevoegen' }))
    const select = screen.getByLabelText('Dienst of toeslag') as HTMLSelectElement
    expect(Array.from(select.options).map((o) => o.textContent)).toContain('Levering vóór 08:00')
  })

  it('carries a manually entered Notitie through promotion to the manual row and into the submitted payload', async () => {
    const { onSubmit } = renderForm()
    await fillMinimalRouteAndCustomer()

    // Satisfy the goods-description rule so submission isn't blocked by an unrelated validation.
    await userEvent.click(screen.getByRole('tab', { name: /Goederen/ }))
    await userEvent.click(screen.getByRole('button', { name: '+ Goederenlijn' }))
    await userEvent.type(screen.getByLabelText('Omschrijving'), '2 europallets onderdelen')

    await userEvent.click(screen.getByRole('tab', { name: /Services & toeslagen/ }))
    await userEvent.click(screen.getByRole('button', { name: '+ Dienst of toeslag toevoegen' }))
    await userEvent.selectOptions(screen.getByLabelText('Dienst of toeslag'), 'opt-wacht')
    await userEvent.type(await screen.findByLabelText('Aantal uur — Wachttijd'), '3')
    await userEvent.type(screen.getByLabelText('Notitie — Wachttijd'), 'Afgesproken met klant')
    await userEvent.click(screen.getByRole('button', { name: 'Toevoegen' }))

    // Promoted to the manual row: the panel closes and the same note is still visible there.
    expect(screen.getByText('MANUEEL')).toBeInTheDocument()
    expect(screen.getByLabelText('Notitie — Wachttijd')).toHaveValue('Afgesproken met klant')

    await userEvent.click(screen.getByRole('button', { name: 'Opdracht aanmaken' }))

    await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(1))
    expect(onSubmit).toHaveBeenCalledWith(expect.objectContaining({
      services: expect.arrayContaining([
        expect.objectContaining({ serviceOptionId: 'opt-wacht', note: 'Afgesproken met klant' }),
      ]),
    }))
  }, 15000)

  it('shows the customer label with a favourite star in the unit selector', async () => {
    renderForm()
    await waitFor(() => expect(screen.getByLabelText('Klant *')).toBeInTheDocument())
    await userEvent.selectOptions(screen.getByLabelText('Klant *'), 'cust-1')
    await userEvent.click(screen.getByRole('tab', { name: /Goederen/ }))

    const unitSelect = screen.getByLabelText('Eenheid')
    await waitFor(() => expect(unitSelect.querySelector('optgroup[label="Eenheden van deze klant"]')).toBeTruthy())
    const preferredOption = unitSelect.querySelector('option[value="EUROPALLET"]')
    expect(preferredOption?.textContent).toBe('★ EURO PAL')
  })

  it('autofills cargo dimensions from the unit master defaults', async () => {
    renderForm()
    await waitFor(() => expect(screen.getByLabelText('Klant *')).toBeInTheDocument())
    await userEvent.selectOptions(screen.getByLabelText('Klant *'), 'cust-1')
    await userEvent.click(screen.getByRole('tab', { name: /Goederen/ }))

    await userEvent.click(screen.getByRole('button', { name: '+ Goederenlijn' }))
    // Once a line exists the order-level "Eenheid" disappears (summary derives from the lines),
    // so the cargo-line unit select is the only "Eenheid" field left.
    await userEvent.selectOptions(screen.getByLabelText('Eenheid'), 'EUROPALLET')

    // 120 × 80 cm from master data arrives as 1.2 × 0.8 m; empty fields only (overridable).
    await waitFor(() => expect(screen.getByLabelText('Lengte (m)')).toHaveValue(1.2))
    expect(screen.getByLabelText('Breedte (m)')).toHaveValue(0.8)
    expect(screen.getByLabelText('Lengte (m)')).not.toBeDisabled()
  })

  it('shows the plain "Goederenlijnen" heading with the commercial-vs-scanable hint, and a Paletten field per line', async () => {
    renderForm()
    await waitFor(() => expect(screen.getByLabelText('Klant *')).toBeInTheDocument())
    await userEvent.click(screen.getByRole('tab', { name: /Goederen/ }))

    expect(screen.getByRole('heading', { name: 'Goederenlijnen' })).toBeInTheDocument()
    expect(screen.queryByText('Goederenlijnen (scanbaar)')).not.toBeInTheDocument()
    expect(
      screen.getByText(
        'Commerciële hoeveelheden voor inhoud en prijs. Scanbare colli worden bij bevestiging per lijn gegenereerd en zijn een apart begrip.',
      ),
    ).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: '+ Goederenlijn' }))
    // "Paletten" also labels the order-level field (Algemeen tab); the cargo-line one carries a cg- id.
    const palletInputs = screen.getAllByLabelText('Paletten') as HTMLInputElement[]
    expect(palletInputs.some((el) => el.id.startsWith('cg-pallets-'))).toBe(true)
  })

  it('shows the no-tariff diagnostics when nothing could be priced', async () => {
    previewSpy.mockResolvedValueOnce({
      lines: [{ label: 'Geen geldig tarief gevonden voor deze order.', amount: 0, source: 'Ontbrekend', informational: false }],
      total: 0,
      totalWithInformational: 0,
      currency: 'EUR',
      zoneCode: null,
      zoneName: null,
      requiresManualPrice: true,
      serviceLines: [],
      tariffDate: '2026-07-25',
      configurationError: null,
      diagnostics: ['Klant: Klant X', 'Eenheid: 3 × Europallet'],
    })
    renderForm()
    await waitFor(() => expect(screen.getByLabelText('Klant *')).toBeInTheDocument())
    await userEvent.selectOptions(screen.getByLabelText('Klant *'), 'cust-1')
    await userEvent.click(screen.getByRole('tab', { name: /Goederen/ }))
    await userEvent.type(screen.getByLabelText('Aantal'), '3')
    await userEvent.selectOptions(screen.getByLabelText('Eenheid'), 'EUROPALLET')

    await userEvent.click(screen.getByRole('tab', { name: /^Prijs$/ }))
    await waitFor(() => expect(previewSpy).toHaveBeenCalled(), { timeout: 3000 })
    await waitFor(() =>
      expect(screen.getByText('Geen geldig tarief gevonden voor deze order — vul een handmatige prijs in of configureer tarieven.')).toBeInTheDocument(),
    )
    expect(screen.getByText('Klant: Klant X')).toBeInTheDocument()
    expect(screen.getByText('Eenheid: 3 × Europallet')).toBeInTheDocument()
  })

  it('shows the calculated breakdown on the Prijs tab', async () => {
    renderForm()
    await waitFor(() => expect(screen.getByLabelText('Klant *')).toBeInTheDocument())
    await userEvent.selectOptions(screen.getByLabelText('Klant *'), 'cust-1')
    await userEvent.click(screen.getByRole('tab', { name: /Goederen/ }))
    await userEvent.type(screen.getByLabelText('Aantal'), '3')
    await userEvent.selectOptions(screen.getByLabelText('Eenheid'), 'EUROPALLET')

    await userEvent.click(screen.getByRole('tab', { name: /^Prijs$/ }))
    await waitFor(() => expect(previewSpy).toHaveBeenCalled(), { timeout: 3000 })
    await waitFor(() => expect(screen.getByText('3 × Europallet (zone Z3)')).toBeInTheDocument())
    expect(screen.getByText(/^Totaal/)).toBeInTheDocument()
    expect(screen.getAllByText('€ 145,00').length).toBeGreaterThan(0)
    // Without the permission there is no manual-override checkbox.
    expect(screen.queryByLabelText(/Handmatige prijs/)).not.toBeInTheDocument()
  })

  it('offers the manual override only with orders.override_price', async () => {
    auth.permissions = new Set(['orders.override_price'])
    renderForm()
    await userEvent.click(screen.getByRole('tab', { name: /^Prijs$/ }))
    expect(screen.getByLabelText(/Handmatige prijs \(overschrijft/)).toBeInTheDocument()
  })

  it('the add panel shows the calculation method and a price indication for the selected service option', async () => {
    renderForm()
    await userEvent.click(screen.getByRole('tab', { name: /Services & toeslagen/ }))
    await userEvent.click(screen.getByRole('button', { name: '+ Dienst of toeslag toevoegen' }))
    await waitFor(() => expect(screen.getByLabelText('Dienst of toeslag')).toBeInTheDocument())
    await userEvent.selectOptions(screen.getByLabelText('Dienst of toeslag'), 'opt-8')

    expect(screen.getByLabelText('Berekeningswijze')).toHaveValue('Vast bedrag')
    expect(screen.getByText(/Prijsindicatie: € 25,00/)).toBeInTheDocument()
  })

  it('shows an auto-applied (contract) service as a read-only checked row with an AUTO badge', async () => {
    previewSpy.mockResolvedValueOnce({
      lines: [
        { label: '3 × Europallet (zone Z3)', amount: 145, source: 'Pallets klant X', informational: false },
        { label: 'Picking (3 colli)', amount: 3.75, source: 'Automatisch (contract)', informational: false },
      ],
      total: 148.75,
      totalWithInformational: 148.75,
      currency: 'EUR',
      zoneCode: 'Z3',
      zoneName: 'Zone 3',
      requiresManualPrice: false,
      serviceLines: [
        {
          serviceOptionId: 'opt-pick', name: 'Picking', kind: 'PerUnit', value: 1.25, amount: 3.75,
          quantity: 3, autoApplied: true,
        },
      ],
    })
    renderForm()
    await waitFor(() => expect(screen.getByLabelText('Klant *')).toBeInTheDocument())
    await userEvent.selectOptions(screen.getByLabelText('Klant *'), 'cust-1')
    await userEvent.click(screen.getByRole('tab', { name: /Goederen/ }))
    await userEvent.type(screen.getByLabelText('Aantal'), '3')
    await userEvent.selectOptions(screen.getByLabelText('Eenheid'), 'EUROPALLET')

    await userEvent.click(screen.getByRole('tab', { name: /Services & toeslagen/ }))
    await waitFor(() => expect(screen.getByText('Picking')).toBeInTheDocument())
    expect(screen.getByText('AUTO')).toBeInTheDocument()
    const row = screen.getByText('Picking').closest('.tof-service-option') as HTMLElement
    const checkbox = within(row).getByRole('checkbox') as HTMLInputElement
    expect(checkbox.checked).toBe(true)
    expect(checkbox).toBeDisabled()
  })

  it('does not offer a service option in the add panel when it is already auto-applied', async () => {
    // "Levering vóór 08:00" (opt-8) is a normal selectable option in listServiceOptions, but the
    // preview reports it as auto-applied for this customer/order — it must render only once, as
    // the read-only "AUTO" row, never again as an addable option.
    previewSpy.mockResolvedValueOnce({
      lines: [
        { label: '3 × Europallet (zone Z3)', amount: 145, source: 'Pallets klant X', informational: false },
        { label: 'Levering vóór 08:00', amount: 25, source: 'Automatisch (contract)', informational: false },
      ],
      total: 170,
      totalWithInformational: 170,
      currency: 'EUR',
      zoneCode: 'Z3',
      zoneName: 'Zone 3',
      requiresManualPrice: false,
      serviceLines: [
        {
          serviceOptionId: 'opt-8', name: 'Levering vóór 08:00', kind: 'Fixed', value: 25, amount: 25,
          quantity: null, autoApplied: true,
        },
      ],
    })
    renderForm()
    await waitFor(() => expect(screen.getByLabelText('Klant *')).toBeInTheDocument())
    await userEvent.selectOptions(screen.getByLabelText('Klant *'), 'cust-1')
    await userEvent.click(screen.getByRole('tab', { name: /Goederen/ }))
    await userEvent.type(screen.getByLabelText('Aantal'), '3')
    await userEvent.selectOptions(screen.getByLabelText('Eenheid'), 'EUROPALLET')

    await userEvent.click(screen.getByRole('tab', { name: /Services & toeslagen/ }))
    // Wait for the debounced preview to resolve and mark the option as auto-applied — until then
    // the plain (pre-preview) row is still showing, same text but not yet deduped.
    await waitFor(() => expect(screen.getByText('AUTO')).toBeInTheDocument(), { timeout: 3000 })
    expect(screen.getAllByText(/Levering vóór 08:00/)).toHaveLength(1)
    const row = screen.getByText(/Levering vóór 08:00/).closest('.tof-service-option') as HTMLElement
    expect(within(row).getByText('AUTO')).toBeInTheDocument()
    const checkbox = within(row).getByRole('checkbox') as HTMLInputElement
    expect(checkbox.checked).toBe(true)
    expect(checkbox).toBeDisabled()

    // The add panel must not offer the auto-applied option again — the still-manual option
    // (Wachttijd) keeps being offered alongside it.
    await userEvent.click(screen.getByRole('button', { name: '+ Dienst of toeslag toevoegen' }))
    const select = screen.getByLabelText('Dienst of toeslag') as HTMLSelectElement
    const optionTexts = Array.from(select.options).map((o) => o.textContent)
    expect(optionTexts).not.toContain('Levering vóór 08:00')
    expect(optionTexts).toContain('Wachttijd')
  })

  it('switches to the one-off fieldset and includes the one-off fields in the preview payload', async () => {
    renderForm()
    await waitFor(() => expect(screen.getByLabelText('Klant *')).toBeInTheDocument())
    await userEvent.selectOptions(screen.getByLabelText('Klant *'), 'cust-1')
    await userEvent.click(screen.getByRole('tab', { name: /^Prijs$/ }))

    expect(screen.queryByLabelText('Vast bedrag (€) *')).not.toBeInTheDocument()
    await userEvent.click(screen.getByRole('radio', { name: 'Eenmalige prijsafspraak' }))
    expect(screen.getByLabelText('Vast bedrag (€) *')).toBeInTheDocument()

    await userEvent.type(screen.getByLabelText('Vast bedrag (€) *'), '850')
    await userEvent.click(screen.getByRole('radio', { name: 'Per activiteit' }))
    await userEvent.type(screen.getByLabelText('Laden (min)'), '30')
    await userEvent.type(screen.getByLabelText('Uurtarief extra tijd (€/u)'), '75')

    await waitFor(() => expect(previewSpy).toHaveBeenCalledWith(expect.objectContaining({
      oneOff: expect.objectContaining({
        fixedAmount: 850, includedLoadingMinutes: 30, includedUnloadingMinutes: null,
        includedCombinedMinutes: null, extraHourlyRate: 75,
      }),
    })), { timeout: 3000 })

    // Switching back to Klantcontract hides the fieldset again.
    await userEvent.click(screen.getByRole('radio', { name: 'Klantcontract' }))
    expect(screen.queryByLabelText('Vast bedrag (€) *')).not.toBeInTheDocument()
  })

  it('shows a VOORSTEL badge and the proposed subtotal for a proposed extra-time line', async () => {
    previewSpy.mockResolvedValueOnce({
      lines: [
        { label: 'Eenmalige prijsafspraak', amount: 450, source: 'Eenmalig', informational: false },
        {
          label: 'Extra laadtijd: 60 min (inbegrepen 30 min)', amount: 37.5, source: 'Extra tijd',
          informational: false, proposed: true,
        },
      ],
      total: 450,
      totalWithInformational: 450,
      totalWithProposed: 487.5,
      currency: 'EUR',
      zoneCode: null,
      zoneName: null,
      requiresManualPrice: false,
      serviceLines: [],
    })
    renderForm()
    await waitFor(() => expect(screen.getByLabelText('Klant *')).toBeInTheDocument())
    await userEvent.selectOptions(screen.getByLabelText('Klant *'), 'cust-1')
    await userEvent.click(screen.getByRole('tab', { name: /^Prijs$/ }))
    await userEvent.click(screen.getByRole('radio', { name: 'Eenmalige prijsafspraak' }))
    await userEvent.type(screen.getByLabelText('Vast bedrag (€) *'), '450')

    await waitFor(() => expect(screen.getByText(/Extra laadtijd: 60 min/)).toBeInTheDocument())
    expect(screen.getByText('VOORSTEL')).toBeInTheDocument()
    expect(screen.getByText('Totaal incl. voorstellen')).toBeInTheDocument()
    expect(screen.getByText('€ 487,50')).toBeInTheDocument()
  })
})

describe('TransportOrderForm minimal-cargo rule (wave 2026-08-04 §3)', () => {
  beforeEach(() => {
    auth.permissions = new Set(['locations.create'])
  })

  it('submits with an empty general description when a cargo line exists (descriptions optional)', async () => {
    const { onSubmit } = renderForm()
    await fillMinimalRouteAndCustomer()

    await userEvent.click(screen.getByRole('tab', { name: /Goederen/ }))
    await userEvent.click(screen.getByRole('button', { name: '+ Goederenlijn' }))

    await userEvent.click(screen.getByRole('button', { name: 'Opdracht aanmaken' }))

    await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(1))
    expect(onSubmit).toHaveBeenCalledWith(expect.objectContaining({ goodsDescription: null }))
  })

  it('submits with only a quantity and unit, no description anywhere', async () => {
    const { onSubmit } = renderForm()
    await fillMinimalRouteAndCustomer()

    await userEvent.click(screen.getByRole('tab', { name: /Goederen/ }))
    await userEvent.type(screen.getByLabelText('Aantal'), '2')
    await userEvent.selectOptions(screen.getByLabelText('Eenheid'), 'EUROPALLET')

    await userEvent.click(screen.getByRole('button', { name: 'Opdracht aanmaken' }))

    await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(1))
    expect(onSubmit).toHaveBeenCalledWith(
      expect.objectContaining({ quantity: 2, quantityUnitCode: 'EUROPALLET', goodsDescription: null }),
    )
  })

  it('rejects submission when there is no quantity, no goods line and no description at all', async () => {
    const { onSubmit } = renderForm()
    await fillMinimalRouteAndCustomer()

    await userEvent.click(screen.getByRole('button', { name: 'Opdracht aanmaken' }))

    // Wave 1 §12: the message shows in the validation summary AND inline at the field.
    expect((await screen.findAllByText(
      'Vul minstens een hoeveelheid en eenheid in, voeg een goederenlijn toe of beschrijf de goederen.',
    )).length).toBeGreaterThan(0)
    expect(onSubmit).not.toHaveBeenCalled()
  })
})

describe('TransportOrderForm derived goods summary (wave 2026-08-04 §2)', () => {
  beforeEach(() => {
    auth.permissions = new Set(['locations.create'])
  })

  it('replaces the header quantity inputs with a derived Lading summary once a line exists', async () => {
    renderForm()
    await fillMinimalRouteAndCustomer()

    await userEvent.click(screen.getByRole('tab', { name: /Goederen/ }))
    expect(screen.getByLabelText('Aantal')).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: '+ Goederenlijn' }))
    await userEvent.selectOptions(screen.getByLabelText('Eenheid'), 'EUROPALLET')
    await userEvent.clear(screen.getByLabelText('Verwacht aantal *'))
    await userEvent.type(screen.getByLabelText('Verwacht aantal *'), '2')

    // Header inputs are gone; the derived summary shows the aggregated lading.
    expect(screen.queryByLabelText('Aantal')).not.toBeInTheDocument()
    expect(screen.getByText('Lading')).toBeInTheDocument()
    expect(screen.getByText(/2 Europallet/)).toBeInTheDocument()
    expect(
      screen.getByText('De samenvatting wordt automatisch afgeleid van de goederenlijnen hieronder.'),
    ).toBeInTheDocument()
  })

  it('offers a migration action that converts the header summary into the first goods line', async () => {
    renderForm()
    await fillMinimalRouteAndCustomer()

    await userEvent.click(screen.getByRole('tab', { name: /Goederen/ }))
    await userEvent.type(screen.getByLabelText('Aantal'), '5')
    await userEvent.selectOptions(screen.getByLabelText('Eenheid'), 'EUROPALLET')
    await userEvent.type(screen.getByLabelText('Gewicht (kg)'), '120')

    await userEvent.click(screen.getByRole('button', { name: 'Zet samenvatting om naar goederenlijn' }))

    // A line seeded from the summary now exists and drives the derived summary.
    expect(screen.getByText(/5 Europallet/)).toBeInTheDocument()
    expect(screen.getByText(/Totaal gewicht: 120 kg/)).toBeInTheDocument()
    expect(screen.queryByLabelText('Aantal')).not.toBeInTheDocument()
    expect(screen.getByDisplayValue('120')).toBeInTheDocument()
  })
})

describe('TransportOrderForm stops layout & simple time input (wave 2026-08-04 §13-15)', () => {
  beforeEach(() => {
    auth.permissions = new Set(['locations.create'])
  })

  it('renders the two default stops in a two-column grid with simple date/from/to fields', async () => {
    renderForm()
    await waitFor(() => expect(screen.getByLabelText('Klant *')).toBeInTheDocument())
    await userEvent.click(screen.getByRole('tab', { name: /Route & stops/ }))

    expect(document.querySelector('.tof-stops-grid')).toBeTruthy()
    expect(screen.getByLabelText('Laaddatum')).toBeInTheDocument()
    expect(screen.getByLabelText('Losdatum')).toBeInTheDocument()
    expect(screen.getAllByLabelText('Van')).toHaveLength(2)
    expect(screen.getAllByLabelText('Tot')).toHaveLength(2)
    // Advanced fields stay available but collapsed by default (Wave 1 §12: "Geavanceerd").
    expect(screen.getAllByText('Geavanceerd')).toHaveLength(2)
    const advanced = document.querySelectorAll('details.tof-stop-extended')
    expect(advanced).toHaveLength(2)
    for (const details of advanced) {
      expect(details.hasAttribute('open')).toBe(false)
    }
  })

  it('offers delivery-friendly time requirement labels and shows the badge', async () => {
    renderForm()
    await fillMinimalRouteAndCustomer()

    const selects = screen.getAllByLabelText('Tijdseis')
    // Loading stop labels.
    expect(within(selects[0]).getByRole('option', { name: 'Laden vóór' })).toBeInTheDocument()
    // Unloading stop labels.
    expect(within(selects[1]).getByRole('option', { name: 'Leveren vóór' })).toBeInTheDocument()
    expect(within(selects[1]).getByRole('option', { name: 'Niet leveren vóór' })).toBeInTheDocument()
    expect(within(selects[1]).getByRole('option', { name: 'Exact levervenster' })).toBeInTheDocument()

    await userEvent.selectOptions(selects[1], 'Before')
    await userEvent.type(screen.getByLabelText('Vóór'), '10:00')

    expect(screen.getByText('Vóór 10:00')).toBeInTheDocument()
  })

  it('submits the simple date/time fields and the time requirement in the payload', async () => {
    const { onSubmit } = renderForm()
    await fillMinimalRouteAndCustomer()
    await userEvent.click(screen.getByRole('tab', { name: /Goederen/ }))
    await userEvent.type(screen.getByLabelText('Omschrijving goederen'), 'Goederen')
    await userEvent.click(screen.getByRole('tab', { name: /Route & stops/ }))

    await userEvent.type(screen.getByLabelText('Losdatum'), '2026-08-12')
    const unloadingVan = screen.getAllByLabelText('Van')[1]
    const unloadingTot = screen.getAllByLabelText('Tot')[1]
    await userEvent.type(unloadingVan, '08:00')
    await userEvent.type(unloadingTot, '10:00')
    const selects = screen.getAllByLabelText('Tijdseis')
    await userEvent.selectOptions(selects[1], 'Before')
    await userEvent.type(screen.getByLabelText('Vóór'), '10:00')

    await userEvent.click(screen.getByRole('button', { name: 'Opdracht aanmaken' }))
    await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(1))

    const input = onSubmit.mock.calls[0][0]
    const unloading = input.stops[1]
    expect(unloading.plannedFrom).toBe('2026-08-12T08:00:00Z')
    expect(unloading.plannedTo).toBe('2026-08-12T10:00:00Z')
    expect(unloading.timeRequirement).toBe('Before')
    expect(unloading.timeRequirementTo).toBe('10:00')
    expect(unloading.timeRequirementFrom).toBeNull()
    expect(input.stops[0].timeRequirement).toBe('None')
  })

  it('rejects a Before requirement without a time', async () => {
    const { onSubmit } = renderForm()
    await fillMinimalRouteAndCustomer()
    await userEvent.click(screen.getByRole('tab', { name: /Goederen/ }))
    await userEvent.type(screen.getByLabelText('Omschrijving goederen'), 'Goederen')
    await userEvent.click(screen.getByRole('tab', { name: /Route & stops/ }))

    const selects = screen.getAllByLabelText('Tijdseis')
    await userEvent.selectOptions(selects[1], 'Before')
    await userEvent.click(screen.getByRole('button', { name: 'Opdracht aanmaken' }))

    // Wave 1 §12: the message shows in the validation summary AND inline at the field.
    expect((await screen.findAllByText('Geef het uur op waarvóór deze stop moet gebeuren.')).length).toBeGreaterThan(0)
    expect(onSubmit).not.toHaveBeenCalled()
  })

  it('sends the stop time requirements to the price preview (§16 live surcharges)', async () => {
    renderForm()
    await fillMinimalRouteAndCustomer()
    await userEvent.click(screen.getByRole('tab', { name: /Goederen/ }))
    await userEvent.type(screen.getByLabelText('Aantal'), '3')
    await userEvent.selectOptions(screen.getByLabelText('Eenheid'), 'EUROPALLET')
    await userEvent.click(screen.getByRole('tab', { name: /Route & stops/ }))

    const selects = screen.getAllByLabelText('Tijdseis')
    await userEvent.selectOptions(selects[1], 'Before')
    await userEvent.type(screen.getByLabelText('Vóór'), '09:30')

    await waitFor(() =>
      expect(previewSpy).toHaveBeenCalledWith(
        expect.objectContaining({
          stopTimes: expect.arrayContaining([
            expect.objectContaining({ isUnloading: true, requirementKind: 'Before', requirementTo: '09:30' }),
          ]),
        }),
      ),
    )
  })

  it('collapses a stop to a compact summary card and expands it again', async () => {
    renderForm()
    await fillMinimalRouteAndCustomer()
    await userEvent.type(screen.getByLabelText('Losdatum'), '2026-08-12')

    const collapseButtons = screen.getAllByRole('button', { name: 'Inklappen' })
    await userEvent.click(collapseButtons[1])

    expect(screen.queryByLabelText('Losdatum')).not.toBeInTheDocument()
    expect(screen.getByText(/Gent · 2026-08-12/)).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Uitklappen' }))
    expect(screen.getByLabelText('Losdatum')).toBeInTheDocument()
  })
})
