import { describe, expect, it, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { TransportOrderForm } from '../TransportOrderForm'
import type { TransportOrderDetail } from '../../types'

const auth = vi.hoisted(() => ({ permissions: new Set<string>() }))

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
  useLookupOptions: () => ({ options: [], isLoading: false, error: null }),
}))
vi.mock('../../../locations/components/LocationSelect', () => ({
  LocationSelect: ({ id }: { id?: string }) => <input id={id} aria-label="locatie" />,
}))
vi.mock('../../../reference/components/CountryCombobox', () => ({
  CountryCombobox: ({ id }: { id?: string }) => <input id={id} aria-label="Land" />,
}))
vi.mock('../../../warehousing/api/warehousingApi', () => ({
  listWarehouses: () => Promise.resolve([]),
}))

const getLocationSpy = vi.hoisted(() => vi.fn())
vi.mock('../../../locations/api/locationsApi', () => ({
  getLocation: getLocationSpy,
  getLocationOptions: () => Promise.resolve([]),
  createLocation: vi.fn(),
}))

vi.mock('../../../tarification/api/pricingApi', async () => {
  const actual = await vi.importActual<typeof import('../../../tarification/api/pricingApi')>(
    '../../../tarification/api/pricingApi',
  )
  return {
    ...actual,
    listServiceOptions: () => Promise.resolve([]),
    getCustomerPricingConfig: () => Promise.resolve({ preferredUnits: [], serviceOptions: [] }),
    listUnitTypeMaster: () => Promise.resolve([]),
    previewPrice: () =>
      Promise.resolve({
        lines: [], total: 0, totalWithInformational: 0, currency: 'EUR',
        zoneCode: null, zoneName: null, requiresManualPrice: false, serviceLines: [],
      }),
  }
})

/** Edit-mode order with one master-location stop (snapshot) and one free-address stop. */
function makeOrder(overrides: Partial<TransportOrderDetail> = {}): TransportOrderDetail {
  return {
    id: 'order-1',
    orderNumber: 'ORD-1',
    orderDate: '2026-07-20',
    customerId: 'cust-1',
    customerName: 'Klant X',
    customerReference: null,
    status: 'Draft',
    goodsDescription: 'Paletten',
    quantity: null,
    quantityUnit: null,
    quantityUnitCode: null,
    weightKg: null,
    volumeM3: null,
    palletCount: null,
    adrRequired: false,
    craneRequired: false,
    agreedPrice: null,
    notes: null,
    cancellationReason: null,
    stops: [
      {
        id: 'stop-1',
        sequence: 1,
        stopType: 'Loading',
        locationId: 'loc-1',
        locationCode: 'LOC-1',
        locationName: 'Magazijn Antwerpen',
        address: 'Noorderlaan 10',
        postalCode: '2030',
        city: 'Antwerpen',
        countryCode: 'BE',
        // C-03: the wire is UTC; 16:30Z IS 18:30 in Europe/Amsterdam on this summer Monday.
        plannedFrom: '2026-07-20T16:30:00Z',
        plannedTo: '2026-07-20T17:30:00Z',
        requestedFrom: null,
        requestedTo: null,
        confirmedFrom: null,
        confirmedTo: null,
        earliestAllowed: null,
        latestAllowed: null,
        appointmentRequired: false,
        appointmentReference: null,
        reference: null,
        instructions: null,
        accessInstructions: null,
        loadingInstructions: null,
        unloadingInstructions: null,
      },
      {
        id: 'stop-2',
        sequence: 2,
        stopType: 'Unloading',
        locationId: null,
        locationCode: null,
        locationName: 'Bouwwerf',
        address: 'Veldstraat 1',
        postalCode: '9000',
        city: 'Gent',
        countryCode: 'BE',
        plannedFrom: null,
        plannedTo: null,
        requestedFrom: null,
        requestedTo: null,
        confirmedFrom: null,
        confirmedTo: null,
        earliestAllowed: null,
        latestAllowed: null,
        appointmentRequired: false,
        appointmentReference: null,
        reference: null,
        instructions: null,
        accessInstructions: null,
        loadingInstructions: null,
        unloadingInstructions: null,
      },
    ],
    cargoItems: [],
    allowedTransitions: [],
    allowedCorrections: [],
    canCancel: false,
    priority: 'Normal',
    legalEntityId: null,
    dieselSurchargeOverride: false,
    dieselSurchargePercentOverride: null,
    dieselSurchargeOverrideReason: null,
    calculatedPrice: null,
    priceIsManual: false,
    priceOverrideReason: null,
    pricingLines: null,
    serviceLines: null,
    pricingSnapshot: null,
    pricingSource: 'Contract',
    oneOffFixedAmount: null,
    oneOffIncludedLoadingMinutes: null,
    oneOffIncludedUnloadingMinutes: null,
    oneOffIncludedCombinedMinutes: null,
    oneOffExtraHourlyRate: null,
    oneOffNotes: null,
    totalWithProposed: null,
    includedLoadingMinutesOverride: null,
    includedUnloadingMinutesOverride: null,
    extraTimeHourlyRateOverride: null,
    extraTimeRoundingStepMinutes: null,
    extraTimeMinimumBillableMinutes: null,
    version: 'v1',
    ...overrides,
  }
}

function renderForm(order = makeOrder(), onSubmit = vi.fn().mockResolvedValue(undefined)) {
  render(
    <MemoryRouter>
      <TransportOrderForm order={order} submitLabel="Opslaan" onSubmit={onSubmit} />
    </MemoryRouter>,
  )
  return { onSubmit }
}

beforeEach(() => {
  auth.permissions = new Set()
  getLocationSpy.mockReset().mockResolvedValue({ openingIntervals: [] })
})

describe('TransportOrderForm stop snapshot (Phase 7)', () => {
  it('shows the snapshot chip with name and address for a master-location stop', async () => {
    renderForm()
    await userEvent.click(screen.getByRole('tab', { name: /Route & stops/ }))
    expect(screen.getByText('Overgenomen van adres')).toBeInTheDocument()
    expect(screen.getByText(/Magazijn Antwerpen — Noorderlaan 10, 2030 Antwerpen/)).toBeInTheDocument()
  })

  it('collapsed summary shows the snapshot name instead of the generic label', async () => {
    renderForm()
    await userEvent.click(screen.getByRole('tab', { name: /Route & stops/ }))
    await userEvent.click(screen.getAllByRole('button', { name: 'Inklappen' })[0])
    expect(screen.getByText(/Magazijn Antwerpen — Noorderlaan 10, 2030 Antwerpen · 2026-07-20/)).toBeInTheDocument()
    expect(screen.queryByText('Adres uit het adresbestand')).not.toBeInTheDocument()
  })

  it('echoes the stop id and refreshSnapshot=false in the submit payload by default', async () => {
    const { onSubmit } = renderForm()
    await waitFor(() => expect(screen.getByLabelText('Klant *')).toBeInTheDocument())
    await userEvent.click(screen.getByRole('button', { name: 'Opslaan' }))

    await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(1))
    const input = onSubmit.mock.calls[0][0]
    expect(input.stops[0]).toEqual(expect.objectContaining({ id: 'stop-1', refreshSnapshot: false, locationId: 'loc-1' }))
    expect(input.stops[1]).toEqual(expect.objectContaining({ id: 'stop-2', refreshSnapshot: false, locationId: null }))
  })

  it('sets refreshSnapshot after confirming "Adres opnieuw overnemen" and shows the pending badge', async () => {
    const { onSubmit } = renderForm()
    await userEvent.click(screen.getByRole('tab', { name: /Route & stops/ }))

    await userEvent.click(screen.getByRole('button', { name: 'Adres opnieuw overnemen' }))
    expect(
      screen.getByText('Lokale aanpassingen op deze stop worden vervangen door de actuele gegevens van het adres.'),
    ).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Opnieuw overnemen' }))

    expect(screen.getByText('Wordt bij opslaan opnieuw overgenomen')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Adres opnieuw overnemen' })).not.toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Opslaan' }))
    await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(1))
    expect(onSubmit.mock.calls[0][0].stops[0]).toEqual(expect.objectContaining({ id: 'stop-1', refreshSnapshot: true }))
  })

  it('cancelling the confirm dialog leaves the refresh flag untouched', async () => {
    renderForm()
    await userEvent.click(screen.getByRole('tab', { name: /Route & stops/ }))
    await userEvent.click(screen.getByRole('button', { name: 'Adres opnieuw overnemen' }))
    await userEvent.click(screen.getByRole('button', { name: 'Annuleren' }))
    expect(screen.queryByText('Wordt bij opslaan opnieuw overgenomen')).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Adres opnieuw overnemen' })).toBeInTheDocument()
  })

  it('shows a client-side opening-hours hint when the planned time falls outside the fetched hours', async () => {
    // Monday 2026-07-20 open 08:00–17:00; planned 18:30–19:30 → outside.
    getLocationSpy.mockResolvedValue({
      openingIntervals: [{ dayOfWeek: 1, fromTime: '08:00', toTime: '17:00', note: null }],
    })
    renderForm()
    await userEvent.click(screen.getByRole('tab', { name: /Route & stops/ }))

    await waitFor(() => expect(getLocationSpy).toHaveBeenCalledWith('loc-1'))
    expect(
      await screen.findByText(
        /De geplande laadtijd van 18:30 valt buiten de openingsuren \(08:00–17:00\) van Magazijn Antwerpen\./,
      ),
    ).toBeInTheDocument()
  })
})
