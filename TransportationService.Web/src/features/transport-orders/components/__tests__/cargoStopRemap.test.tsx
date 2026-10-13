import { beforeEach, describe, expect, it, vi } from 'vitest'
import { act, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, RouterProvider, createMemoryRouter } from 'react-router-dom'
import { TransportOrderForm } from '../TransportOrderForm'
import { RouteDrawer } from '../../../dossiers/components/RouteDrawer'
import {
  cargoFromOrder,
  emptyCargoRow,
  emptyStop,
  remapCargoStopIndices,
  stopsFromOrder,
  type CargoFormRow,
  type StopFormRow,
} from '../sections/orderFormState'
import type { CargoItem, TransportOrderDetail, TransportOrderStop } from '../../types'

/**
 * Wave 1 fix A (A1a) — a goods line points at its stops by INDEX into the submitted stop list.
 * Moving or removing a stop renumbered that list without touching the indexes, so after a pure
 * reorder every line silently addressed a different stop: the server re-pinned the cargo, the
 * colli kept the pin they were generated with and every delivery scan raised "hoort bij een andere
 * losstop". In the dossier route drawer the same staleness produced a hard dead end — an
 * out-of-range index came back as a 400 about a GOODS field the drawer does not even show.
 *
 * One shared helper now remaps every cargo row whenever the stop list changes, and both editors
 * (the full order form and the route drawer) route their stop mutations through it.
 */

const auth = vi.hoisted(() => ({ permissions: new Set<string>() }))
const updateOrderSpy = vi.hoisted(() => vi.fn())

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
vi.mock('../../../locations/api/locationsApi', () => ({
  getLocation: () => Promise.resolve({ openingIntervals: [] }),
  getLocationOptions: () => Promise.resolve([]),
  createLocation: vi.fn(),
}))
vi.mock('../../api/transportOrdersApi', () => ({
  updateTransportOrder: updateOrderSpy,
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

function stop(id: string, stopType: TransportOrderStop['stopType'], city: string, sequence: number): TransportOrderStop {
  return {
    id, sequence, stopType,
    locationId: null, locationCode: null, locationName: city,
    address: null, postalCode: null, city, countryCode: 'BE',
    plannedFrom: null, plannedTo: null,
    requestedFrom: null, requestedTo: null,
    confirmedFrom: null, confirmedTo: null,
    earliestAllowed: null, latestAllowed: null,
    appointmentRequired: false, appointmentReference: null,
    reference: null, instructions: null,
    accessInstructions: null, loadingInstructions: null, unloadingInstructions: null,
    timeRequirement: 'None', timeRequirementFrom: null, timeRequirementTo: null,
    includedTimeMinutesOverride: null,
  }
}

function cargo(id: string, loadingStopId: string, unloadingStopId: string): CargoItem {
  return {
    id, sequence: 1, description: 'Onderdelen', barcode: null, expectedQuantity: 2,
    quantityUnit: null, quantityUnitCode: 'EUROPALLET', notes: null,
    unitType: null, unitTypeLabel: null, totalWeightKg: null, weightPerUnitKg: null,
    lengthMeters: null, widthMeters: null, heightMeters: null, volumeM3: null, volumeIsManual: false,
    adrRequired: false, adrDetails: null, stackable: true, reference: null,
    loadingStopId, unloadingStopId, palletCount: null,
  }
}

/** Laden Antwerpen → lossen Gent → lossen Brugge, with the goods line pinned to BRUGGE. */
function makeOrder(): TransportOrderDetail {
  return {
    id: 'order-1', orderNumber: 'ORD-1', orderDate: '2026-07-20',
    customerId: 'cust-1', customerName: 'Klant X', customerReference: null,
    status: 'Draft', goodsDescription: 'Paletten',
    quantity: null, quantityUnit: null, quantityUnitCode: null,
    weightKg: null, volumeM3: null, palletCount: null,
    distanceKm: null, loadingMeters: null,
    adrRequired: false, craneRequired: false, plateauRequired: false, moffettRequired: false,
    isReturnMovement: false, agreedPrice: null, notes: null, cancellationReason: null,
    stops: [
      stop('stop-1', 'Loading', 'Antwerpen', 1),
      stop('stop-2', 'Unloading', 'Gent', 2),
      stop('stop-3', 'Unloading', 'Brugge', 3),
    ],
    cargoItems: [cargo('cargo-1', 'stop-1', 'stop-3')],
    allowedTransitions: [], allowedCorrections: [], canCancel: false, priority: 'Normal',
    legalEntityId: null,
    dieselSurchargeOverride: false, dieselSurchargePercentOverride: null, dieselSurchargeOverrideReason: null,
    calculatedPrice: null, priceIsManual: false, priceOverrideReason: null,
    pricingLines: null, serviceLines: null, pricingSnapshot: null, pricingSource: 'Contract',
    oneOffFixedAmount: null, oneOffIncludedLoadingMinutes: null, oneOffIncludedUnloadingMinutes: null,
    oneOffIncludedCombinedMinutes: null, oneOffExtraHourlyRate: null, oneOffNotes: null,
    totalWithProposed: null,
    includedLoadingMinutesOverride: null, includedUnloadingMinutesOverride: null,
    extraTimeHourlyRateOverride: null, extraTimeRoundingStepMinutes: null, extraTimeMinimumBillableMinutes: null,
    version: 'v1',
  } as unknown as TransportOrderDetail
}

beforeEach(() => {
  auth.permissions = new Set()
  updateOrderSpy.mockReset().mockResolvedValue(makeOrder())
})

describe('remapCargoStopIndices', () => {
  const order = makeOrder()
  const stops = (): StopFormRow[] => stopsFromOrder(order)
  const rows = (): CargoFormRow[] => cargoFromOrder(order)

  it('follows a reordered stop instead of keeping its old position', () => {
    const before = stops()
    const cargoRows = rows()
    expect(cargoRows[0].unloadingStopIndex).toBe('2') // Brugge

    const after = [before[0], before[2], before[1]] // swap Gent and Brugge
    const remapped = remapCargoStopIndices(cargoRows, before, after)

    expect(remapped[0].unloadingStopIndex).toBe('1')
    expect(after[Number(remapped[0].unloadingStopIndex)].id).toBe('stop-3') // still Brugge
    expect(remapped[0].loadingStopIndex).toBe('0')
  })

  it('shifts the index down when an earlier stop is removed', () => {
    const before = stops()
    const after = [before[0], before[2]] // Gent dropped
    const remapped = remapCargoStopIndices(rows(), before, after)

    expect(remapped[0].unloadingStopIndex).toBe('1')
    expect(after[1].id).toBe('stop-3')
  })

  it('clears the link when the stop it points at is the one removed', () => {
    const before = stops()
    const after = [before[0], before[1]] // Brugge dropped
    const remapped = remapCargoStopIndices(rows(), before, after)

    expect(remapped[0].unloadingStopIndex).toBe('')
    expect(remapped[0].loadingStopIndex).toBe('0')
  })

  it('leaves the indexes alone when a stop is appended', () => {
    const before = stops()
    const after = [...before, emptyStop('Unloading')]
    const remapped = remapCargoStopIndices(rows(), before, after)

    expect(remapped[0].loadingStopIndex).toBe('0')
    expect(remapped[0].unloadingStopIndex).toBe('2')
  })

  it('keeps an automatic ("") link automatic', () => {
    const before = stops()
    const automatic: CargoFormRow[] = [{ ...emptyCargoRow(), loadingStopIndex: '', unloadingStopIndex: '' }]
    const remapped = remapCargoStopIndices(automatic, before, [before[0], before[2], before[1]])

    expect(remapped[0].loadingStopIndex).toBe('')
    expect(remapped[0].unloadingStopIndex).toBe('')
  })
})

describe('TransportOrderForm — goods lines follow a reordered stop', () => {
  it('submits the goods line against the stop it was linked to, not its old position', async () => {
    const onSubmit = vi.fn().mockResolvedValue(undefined)
    render(
      <MemoryRouter>
        <TransportOrderForm mode="edit" order={makeOrder()} submitLabel="Opslaan" onSubmit={onSubmit} />
      </MemoryRouter>,
    )
    await userEvent.click(screen.getByRole('tab', { name: /Route & stops/ }))
    // Move Brugge (the third stop) up, so it swaps places with Gent.
    await userEvent.click(screen.getAllByRole('button', { name: '↑' })[2])
    await userEvent.click(screen.getByRole('button', { name: 'Opslaan' }))

    await waitFor(() => expect(onSubmit).toHaveBeenCalled())
    const payload = onSubmit.mock.calls[0][0]
    expect(payload.stops.map((s: { id: string | null }) => s.id)).toEqual(['stop-1', 'stop-3', 'stop-2'])
    // Index 1 is now Brugge — the stop the line was linked to all along.
    expect(payload.cargoItems[0].unloadingStopIndex).toBe(1)
    expect(payload.cargoItems[0].loadingStopIndex).toBe(0)
  })

  /**
   * Re-review N-1: `mutateStops` read `stops` from the RENDER CLOSURE, so two stop mutations
   * dispatched before React re-rendered both started from the pre-batch list and the first was
   * silently discarded — together with the cargo remap computed against that same stale list.
   * Two removals inside one `act` reproduce it: the buggy version keeps two stops (last write
   * wins), the fixed one applies both.
   */
  it('applies two stop mutations issued in the same tick', async () => {
    const onSubmit = vi.fn().mockResolvedValue(undefined)
    render(
      <MemoryRouter>
        <TransportOrderForm mode="edit" order={makeOrder()} submitLabel="Opslaan" onSubmit={onSubmit} />
      </MemoryRouter>,
    )
    await userEvent.click(screen.getByRole('tab', { name: /Route & stops/ }))

    const remove = screen.getAllByRole('button', { name: 'Verwijderen' })
    await act(async () => {
      remove[2].click() // Brugge
      remove[1].click() // Gent
    })
    await userEvent.click(screen.getByRole('button', { name: 'Opslaan' }))

    await waitFor(() => expect(onSubmit).toHaveBeenCalled())
    const payload = onSubmit.mock.calls[0][0]
    expect(payload.stops.map((s: { id: string | null }) => s.id)).toEqual(['stop-1'])
    // Both unloading stops are gone, so the line's link falls back to automatic rather than to a
    // position that no longer exists.
    expect(payload.cargoItems[0].unloadingStopIndex).toBeNull()
    expect(payload.cargoItems[0].loadingStopIndex).toBe(0)
  })
})

/** SectionDrawer guards unsaved changes with `useBlocker`, which needs a DATA router. */
function renderDrawer() {
  const router = createMemoryRouter(
    [{ path: '/', element: <RouteDrawer order={makeOrder()} onClose={vi.fn()} onSaved={vi.fn()} /> }],
    { initialEntries: ['/'] },
  )
  return render(<RouterProvider router={router} />)
}

describe('RouteDrawer — the stop-only editor keeps the goods links coherent', () => {
  it('remaps the goods line when a stop is reordered', async () => {
    renderDrawer()
    await userEvent.click(screen.getAllByRole('button', { name: '↑' })[2])
    await userEvent.click(screen.getByRole('button', { name: /Opslaan/ }))

    await waitFor(() => expect(updateOrderSpy).toHaveBeenCalled())
    const payload = updateOrderSpy.mock.calls[0][1]
    expect(payload.stops.map((s: { id: string | null }) => s.id)).toEqual(['stop-1', 'stop-3', 'stop-2'])
    expect(payload.cargoItems[0].unloadingStopIndex).toBe(1)
  })

  it('drops the goods link — instead of sending an out-of-range index — when its stop is removed', async () => {
    renderDrawer()
    // Remove Brugge: the stale index 2 used to survive and come back as a 400 about a goods
    // field this drawer does not render — a dead end for the user.
    await userEvent.click(screen.getAllByRole('button', { name: 'Verwijderen' })[2])
    await userEvent.click(screen.getByRole('button', { name: /Opslaan/ }))

    await waitFor(() => expect(updateOrderSpy).toHaveBeenCalled())
    const payload = updateOrderSpy.mock.calls[0][1]
    expect(payload.stops).toHaveLength(2)
    expect(payload.cargoItems[0].unloadingStopIndex).toBeNull()
  })

  /** Re-review N-1, drawer side — same lost-update window, same reproduction. */
  it('applies two stop mutations issued in the same tick', async () => {
    renderDrawer()

    const remove = screen.getAllByRole('button', { name: 'Verwijderen' })
    await act(async () => {
      remove[2].click() // Brugge
      remove[1].click() // Gent
    })
    await userEvent.click(screen.getByRole('button', { name: /Opslaan/ }))

    await waitFor(() => expect(updateOrderSpy).toHaveBeenCalled())
    const payload = updateOrderSpy.mock.calls[0][1]
    expect(payload.stops.map((s: { id: string | null }) => s.id)).toEqual(['stop-1'])
    expect(payload.cargoItems[0].unloadingStopIndex).toBeNull()
  })
})
