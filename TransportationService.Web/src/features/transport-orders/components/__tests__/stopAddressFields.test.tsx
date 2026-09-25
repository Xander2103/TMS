import { useEffect, useState } from 'react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { RouteSection } from '../sections/RouteSection'
import { buildSubmitPayload } from '../sections/orderFormPayload'
import { useStopMutation } from '../sections/useStopMutation'
import {
  emptyStop,
  validateOrderForm,
  type CargoFormRow,
  type OrderFormValues,
  type StopFormRow,
} from '../sections/orderFormState'
import { applyStopPatch } from '../../utils/plannedEnd'
import type { AddressPickerOption } from '../../../locations/api/customerAddressesApi'
import type { TransportOrderInput } from '../../types'

const pickerApi = vi.hoisted(() => ({ pickAddresses: vi.fn(), checkAddressDuplicates: vi.fn() }))
vi.mock('../../../locations/api/customerAddressesApi', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../locations/api/customerAddressesApi')>()),
  pickAddresses: pickerApi.pickAddresses,
  checkAddressDuplicates: pickerApi.checkAddressDuplicates,
}))
const locationsApi = vi.hoisted(() => ({ getLocation: vi.fn() }))
vi.mock('../../../locations/api/locationsApi', () => locationsApi)
vi.mock('../../../reference/components/CountryCombobox', () => ({
  CountryCombobox: ({ id, value, onChange }: { id?: string; value: string | null; onChange: (code: string | null) => void }) => (
    <input id={id} aria-label="Land" value={value ?? ''} onChange={(e) => onChange(e.target.value || null)} />
  ),
}))

const novellini: AddressPickerOption = {
  locationId: 'loc-nov',
  code: 'NOV',
  name: 'Novellini',
  type: 'CustomerLocation',
  street: 'Avenue Sabin',
  houseNumber: '1',
  postalCode: '1300',
  city: 'Waver',
  countryCode: 'BE',
  group: 'CustomerAddress',
  customerNames: 'Van Caudenberg BV',
}

function valuesFor(stops: StopFormRow[]): OrderFormValues {
  return {
    customerId: 'cust-1', customerReference: '', orderDate: '2026-09-21', goodsDescription: 'Paletten',
    quantity: '', quantityUnit: '', quantityUnitCode: null, weightKg: '', volumeM3: '', palletCount: '',
    distanceKm: '', loadingMeters: '', adrRequired: false, craneRequired: false, plateauRequired: false,
    moffettRequired: false, isReturnMovement: false, agreedPrice: '', notes: '', legalEntityId: '',
    dieselSurchargeOverride: false, dieselSurchargePercentOverride: '', dieselSurchargeOverrideReason: '',
    stops, cargoItems: [], serviceOptions: [], selectedServiceOptionIds: [], serviceQuantities: {},
    servicePallets: {}, serviceDays: {}, serviceNotes: {}, priceIsManual: false, priceOverrideReason: '',
    pricingSource: 'Contract', oneOffFixedAmount: '', oneOffTimeMode: 'none', oneOffIncludedLoadingMinutes: '',
    oneOffIncludedUnloadingMinutes: '', oneOffIncludedCombinedMinutes: '', oneOffExtraHourlyRate: '', oneOffNotes: '',
    includedLoadingMinutesOverride: '', includedUnloadingMinutesOverride: '', extraTimeHourlyRateOverride: '',
    extraTimeRoundingStepMinutes: '', extraTimeMinimumBillableMinutes: '', version: 'v7',
  }
}

/** Last payload the harness would submit — the same builder every real editor uses. */
const latest = {} as { payload: TransportOrderInput; stops: StopFormRow[] }

function Harness({ initial, canSave = true }: { initial: StopFormRow[]; canSave?: boolean }) {
  const [stops, setStops] = useState(initial)
  const [, setCargo] = useState<CargoFormRow[]>([])
  const mutateStops = useStopMutation(stops, setStops, setCargo)
  const [errors, setErrors] = useState<Record<string, string>>({})
  // Published after every commit (an effect — render itself stays pure).
  useEffect(() => {
    latest.payload = buildSubmitPayload(valuesFor(stops))
    latest.stops = stops
  }, [stops])
  return (
    <>
      <RouteSection
        stops={stops}
        customerId="cust-1"
        saving={false}
        locationHours={{}}
        errors={errors}
        onAddStop={(type) => mutateStops((rows) => [...rows, emptyStop(type)])}
        setStop={(key, patch) => mutateStops((rows) => rows.map((row) => (row.key === key ? applyStopPatch(row, patch, null) : row)))}
        moveStop={(index, delta) =>
          mutateStops((rows) => {
            const next = [...rows]
            ;[next[index], next[index + delta]] = [next[index + delta], next[index]]
            return next
          })
        }
        onRemoveStop={(key) => mutateStops((rows) => rows.filter((row) => row.key !== key))}
        onRequestRefresh={(key) => mutateStops((rows) => rows.map((row) => (row.key === key ? { ...row, refreshSnapshot: true, addressOverridden: false } : row)))}
        canSaveToAddressBook={canSave}
        sheet
        hideHeader
      />
      <button
        type="button"
        onClick={() => setErrors(Object.fromEntries(validateOrderForm(valuesFor(stops)).map((e) => [e.field, e.message])))}
      >
        valideer
      </button>
    </>
  )
}

// (role "group" would also match every <details> disclosure inside a card.)
const cards = () => Array.from(document.querySelectorAll<HTMLElement>('fieldset.tof-stop'))
const field = (card: HTMLElement, label: string | RegExp) => within(card).getByLabelText(label) as HTMLInputElement

beforeEach(() => {
  pickerApi.pickAddresses.mockReset().mockResolvedValue([novellini])
  pickerApi.checkAddressDuplicates.mockReset().mockResolvedValue({ hasExactMatch: false, candidates: [] })
  locationsApi.getLocation.mockReset().mockRejectedValue(new Error('not needed'))
})

describe('Stop address fields — always visible, filled from the real record', () => {
  it('shows every address field for a brand-new stop AND for a stop linked to an existing address', () => {
    const linked: StopFormRow = {
      ...emptyStop('Unloading'), id: 'stop-2', locationId: 'loc-nov', locationName: 'Novellini', address: 'Avenue Sabin 1',
      postalCode: '1300', city: 'Waver', snapshotName: 'Novellini', snapshotAddress: 'Avenue Sabin 1, 1300 Waver',
    }
    render(<Harness initial={[emptyStop('Loading'), linked]} />)
    for (const card of cards()) {
      for (const label of ['Adres zoeken', 'Naam locatie', 'Straat + nr', 'Postcode', /Plaats/, 'Land']) {
        expect(field(card, label)).toBeVisible()
      }
    }
    // Reopening a dossier shows the stored snapshot at once — nothing hidden behind the link.
    expect(field(cards()[1], 'Naam locatie')).toHaveValue('Novellini')
    expect(field(cards()[1], 'Straat + nr')).toHaveValue('Avenue Sabin 1')
    expect(field(cards()[1], /Plaats/)).toHaveValue('Waver')
    expect(field(cards()[1], 'Naam locatie')).toBeEnabled()
  })

  it('selecting an existing address fills ALL fields; the name is the location NAME, not the address line', async () => {
    const user = userEvent.setup()
    render(<Harness initial={[emptyStop('Loading'), emptyStop('Unloading')]} />)
    const card = cards()[0]

    await user.type(field(card, 'Adres zoeken'), 'Novel')
    await user.click(await within(card).findByText('Avenue Sabin 1, 1300 Waver'))

    expect(field(card, 'Naam locatie')).toHaveValue('Novellini')
    expect(field(card, 'Straat + nr')).toHaveValue('Avenue Sabin 1')
    expect(field(card, 'Postcode')).toHaveValue('1300')
    expect(field(card, /Plaats/)).toHaveValue('Waver')
    expect(field(card, 'Land')).toHaveValue('BE')
    expect(within(card).getByText('Overgenomen van adres')).toBeInTheDocument()

    expect(latest.payload.stops[0]).toMatchObject({
      locationId: 'loc-nov', locationName: 'Novellini', address: 'Avenue Sabin 1', postalCode: '1300', city: 'Waver',
      countryCode: 'BE', addressOverridden: false, saveToAddressBook: false,
    })
    // The other stop is untouched.
    expect(latest.payload.stops[1]).toMatchObject({ locationId: null, city: null })
  })

  it('editing one field of a linked stop marks the override, shows the hint and can be reset', async () => {
    const user = userEvent.setup()
    render(<Harness initial={[emptyStop('Loading')]} />)
    const card = cards()[0]
    await user.type(field(card, 'Adres zoeken'), 'Novel')
    await user.click(await within(card).findByText('Novellini'))
    expect(within(card).queryByText(/Afwijkend van adresboek/)).not.toBeInTheDocument()

    await user.clear(field(card, 'Straat + nr'))
    await user.type(field(card, 'Straat + nr'), 'Avenue Sabin 3')

    expect(within(card).getByText('Afwijkend van adresboek — alleen voor dit dossier')).toBeInTheDocument()
    expect(latest.payload.stops[0]).toMatchObject({ locationId: 'loc-nov', address: 'Avenue Sabin 3', addressOverridden: true })

    // "Adres opnieuw overnemen" on an unsaved stop refills from the record and clears the flag.
    locationsApi.getLocation.mockResolvedValue({
      id: 'loc-nov', name: 'Novellini', street: 'Avenue Sabin', houseNumber: '1', postalCode: '1300', city: 'Waver', countryCode: 'BE',
    })
    await user.click(within(card).getByRole('button', { name: 'Adres opnieuw overnemen' }))
    await waitFor(() => expect(field(card, 'Straat + nr')).toHaveValue('Avenue Sabin 1'))
    expect(within(card).queryByText(/Afwijkend van adresboek/)).not.toBeInTheDocument()
    expect(latest.payload.stops[0]).toMatchObject({ addressOverridden: false, refreshSnapshot: false })
  })

  it('a free address with NO suggestion chosen is saved exactly as typed', async () => {
    const user = userEvent.setup()
    render(<Harness initial={[emptyStop('Loading')]} />)
    const card = cards()[0]

    await user.type(field(card, 'Naam locatie'), 'Werf Dupont')
    await user.type(field(card, 'Straat + nr'), 'Avenue Sab')
    // Suggestions appear — and are ignored.
    expect(await within(card).findByText('Novellini')).toBeInTheDocument()
    await user.type(field(card, 'Postcode'), '1300')
    await user.type(field(card, /Plaats/), 'Wavre')

    expect(field(card, 'Straat + nr')).toHaveValue('Avenue Sab')
    expect(latest.payload.stops[0]).toMatchObject({
      locationId: null, locationName: 'Werf Dupont', address: 'Avenue Sab', postalCode: '1300', city: 'Wavre',
      addressOverridden: false,
    })
    await user.click(screen.getByRole('button', { name: 'valideer' }))
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('street-field suggestions never auto-select (Enter keeps the text) and fill the other fields on an explicit choice', async () => {
    const user = userEvent.setup()
    render(<Harness initial={[emptyStop('Loading')]} />)
    const card = cards()[0]
    const street = field(card, 'Straat + nr')

    await user.type(street, 'Avenue Sab')
    await within(card).findByText('Novellini')
    await user.keyboard('{Enter}')
    expect(street).toHaveValue('Avenue Sab')
    expect(latest.payload.stops[0]).toMatchObject({ locationId: null, address: 'Avenue Sab' })

    await user.type(street, 'i')
    await within(card).findByText('Novellini')
    await user.keyboard('{ArrowDown}{Enter}')
    expect(street).toHaveValue('Avenue Sabin 1')
    expect(field(card, 'Naam locatie')).toHaveValue('Novellini')
    expect(field(card, 'Postcode')).toHaveValue('1300')
    expect(field(card, /Plaats/)).toHaveValue('Waver')
    expect(latest.payload.stops[0]).toMatchObject({ locationId: 'loc-nov', addressOverridden: false })
  })
})

describe('"Adres ook bewaren in klantenbestand"', () => {
  it('is OFF by default, only sets a payload flag, and is hidden for an unmodified existing address', async () => {
    const user = userEvent.setup()
    render(<Harness initial={[emptyStop('Loading')]} />)
    const card = cards()[0]
    // Nothing to store yet.
    expect(within(card).queryByLabelText('Adres ook bewaren in klantenbestand')).not.toBeInTheDocument()

    await user.type(field(card, /Plaats/), 'Wavre')
    const checkbox = field(card, 'Adres ook bewaren in klantenbestand')
    expect(checkbox).not.toBeChecked()
    expect(within(card).getByText('Zonder deze optie wordt het adres alleen bij dit dossier opgeslagen.')).toBeInTheDocument()
    expect(latest.payload.stops[0].saveToAddressBook).toBe(false)

    await user.click(checkbox)
    expect(latest.payload.stops[0].saveToAddressBook).toBe(true)

    // Choosing an existing address: nothing new to store — option gone, flag cleared.
    await user.type(field(card, 'Adres zoeken'), 'Novel')
    await user.click(await within(card).findByText('Novellini'))
    expect(within(card).queryByLabelText('Adres ook bewaren in klantenbestand')).not.toBeInTheDocument()
    expect(latest.payload.stops[0].saveToAddressBook).toBe(false)

    // A deviating existing address IS new again.
    await user.type(field(card, 'Straat + nr'), 'bis')
    expect(field(card, 'Adres ook bewaren in klantenbestand')).not.toBeChecked()
  })

  it('is not offered without locations.create', async () => {
    const user = userEvent.setup()
    render(<Harness initial={[emptyStop('Loading')]} canSave={false} />)
    await user.type(field(cards()[0], /Plaats/), 'Wavre')
    expect(screen.queryByLabelText('Adres ook bewaren in klantenbestand')).not.toBeInTheDocument()
    expect(latest.payload.stops[0].saveToAddressBook).toBe(false)
  })

  it('shows possible existing addresses before saving and can adopt one', async () => {
    pickerApi.checkAddressDuplicates.mockResolvedValue({
      hasExactMatch: true,
      candidates: [{
        locationId: 'loc-nov', code: 'NOV', name: 'Novellini', match: 'Exact', street: 'Avenue Sabin', houseNumber: '1',
        postalCode: '1300', city: 'Waver', countryCode: 'BE', isActive: true, linkedCustomers: ['Van Caudenberg BV'], type: 'CustomerLocation',
      }],
    })
    pickerApi.pickAddresses.mockResolvedValue([])
    const user = userEvent.setup()
    render(<Harness initial={[emptyStop('Loading')]} />)
    const card = cards()[0]
    await user.type(field(card, 'Straat + nr'), 'Avenue Sabin 1')
    await user.type(field(card, /Plaats/), 'Waver')
    await user.click(field(card, 'Adres ook bewaren in klantenbestand'))

    expect(await within(card).findByText('Dit adres bestaat mogelijk al:')).toBeInTheDocument()
    expect(pickerApi.checkAddressDuplicates).toHaveBeenLastCalledWith(
      expect.objectContaining({ street: 'Avenue Sabin', houseNumber: '1', city: 'Waver', countryCode: 'BE' }),
    )
    await user.click(within(card).getByRole('button', { name: 'Dit adres gebruiken' }))
    expect(latest.payload.stops[0]).toMatchObject({ locationId: 'loc-nov', locationName: 'Novellini', saveToAddressBook: false })
  })
})

describe('Postal code — validated only when typed in this session', () => {
  it('blocks a freshly typed wrong Belgian code with the specific message; stored data never blocks', async () => {
    const stored: StopFormRow = { ...emptyStop('Unloading'), id: 'stop-2', city: 'Gent', postalCode: 'B-9000' }
    const user = userEvent.setup()
    render(<Harness initial={[{ ...emptyStop('Loading'), city: 'Leuven' }, stored]} />)

    await user.click(screen.getByRole('button', { name: 'valideer' }))
    expect(screen.queryByText('Een Belgische postcode heeft 4 cijfers.')).not.toBeInTheDocument()

    await user.type(field(cards()[0], 'Postcode'), '30800')
    await user.click(screen.getByRole('button', { name: 'valideer' }))
    expect(within(cards()[0]).getByText('Een Belgische postcode heeft 4 cijfers.')).toBeInTheDocument()
    expect(within(cards()[1]).queryByText('Een Belgische postcode heeft 4 cijfers.')).not.toBeInTheDocument()
  })
})

describe('Multiple stops — one stop edited, the others untouched (id-preserving full list)', () => {
  const persisted = (id: string, stopType: StopFormRow['stopType'], city: string, extra: Partial<StopFormRow> = {}): StopFormRow => ({
    ...emptyStop(stopType), id, city, locationName: `Naam ${city}`, address: `${city}straat 1`, postalCode: '1000',
    date: '2026-09-22', fromTime: '08:00', toTime: '10:00', reference: `REF-${id}`, ...extra,
  })

  it('editing stop 2 of 3 leaves stops 1 and 3 byte-identical in the payload, ids and version echoed', async () => {
    const user = userEvent.setup()
    render(
      <Harness
        initial={[
          persisted('s1', 'Loading', 'Antwerpen'),
          persisted('s2', 'Unloading', 'Gent'),
          persisted('s3', 'Unloading', 'Brugge', { timeRequirement: 'Before', timeReqTo: '08:00' }),
        ]}
      />,
    )
    const before = structuredClone(latest.payload.stops)
    expect(cards()).toHaveLength(3)

    const second = cards()[1]
    await user.clear(field(second, /Plaats/))
    await user.type(field(second, /Plaats/), 'Aalst')
    await user.clear(field(second, 'Tot'))
    await user.type(field(second, 'Tot'), '11:30')
    await user.tab()

    const after = latest.payload
    expect(after.version).toBe('v7')
    expect(after.stops.map((s) => s.id)).toEqual(['s1', 's2', 's3'])
    expect(after.stops[0]).toEqual(before[0])
    expect(after.stops[2]).toEqual(before[2])
    expect(after.stops[2]).toMatchObject({ timeRequirement: 'Before', timeRequirementTo: '08:00' })
    expect(after.stops[1]).toMatchObject({ id: 's2', city: 'Aalst' })
    expect(after.stops[1].plannedTo).not.toEqual(before[1].plannedTo)
  })

  it('adding, reordering and removing a stop never changes another stop’s data', async () => {
    const user = userEvent.setup()
    render(
      <Harness
        initial={[persisted('s1', 'Loading', 'Antwerpen'), persisted('s2', 'Unloading', 'Gent'), persisted('s3', 'Unloading', 'Brugge')]}
      />,
    )
    const byId = () => Object.fromEntries(latest.payload.stops.map((s) => [s.id, s]))
    const before = structuredClone(byId())

    await user.click(screen.getByRole('button', { name: 'Stop 3 omhoog' }))
    expect(latest.payload.stops.map((s) => s.id)).toEqual(['s1', 's3', 's2'])
    expect(byId()).toEqual(before)

    await user.click(screen.getByRole('button', { name: 'Stop 2 verwijderen' }))
    expect(latest.payload.stops.map((s) => s.id)).toEqual(['s1', 's2'])
    expect(byId().s1).toEqual(before.s1)
    expect(byId().s2).toEqual(before.s2)
  })

  it('Van and Tot sit in one two-column pair, both optional, both 24h text fields', () => {
    render(<Harness initial={[emptyStop('Loading'), emptyStop('Unloading')]} />)
    for (const card of cards()) {
      const from = field(card, 'Van')
      const to = field(card, 'Tot')
      const pair = from.closest('.tof-time-pair')
      expect(pair).not.toBeNull()
      expect(pair).toBe(to.closest('.tof-time-pair'))
      expect(pair!.querySelectorAll('.ui-form-field')).toHaveLength(2)
      expect(from).toHaveAttribute('inputmode', 'numeric')
      expect(from).not.toBeRequired()
      expect(to).not.toBeRequired()
    }
  })
})
