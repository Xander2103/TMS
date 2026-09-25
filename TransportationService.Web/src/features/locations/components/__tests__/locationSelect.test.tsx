import { describe, expect, it, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { LocationSelect } from '../LocationSelect'
import type { AddressPickerOption } from '../../api/customerAddressesApi'

const pickerApi = vi.hoisted(() => ({ pickAddresses: vi.fn() }))
vi.mock('../../api/customerAddressesApi', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../api/customerAddressesApi')>()),
  pickAddresses: pickerApi.pickAddresses,
}))

const locationsApi = vi.hoisted(() => ({ getLocation: vi.fn() }))
vi.mock('../../api/locationsApi', () => locationsApi)

const novellini: AddressPickerOption = {
  locationId: 'loc-1',
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

const depot: AddressPickerOption = {
  locationId: 'loc-2',
  code: 'Depot Gent',
  name: 'Depot Gent',
  type: 'Depot',
  street: null,
  houseNumber: null,
  postalCode: '9000',
  city: 'Gent',
  countryCode: 'BE',
  group: 'Recent',
}

function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((res) => {
    resolve = res
  })
  return { promise, resolve }
}

beforeEach(() => {
  pickerApi.pickAddresses.mockReset().mockResolvedValue([novellini, depot])
  locationsApi.getLocation.mockReset()
})

/**
 * The stop's address-search field: GPS-style suggestions on the one shared address search. It is
 * a search box, not a select — nothing is ever chosen on the planner's behalf.
 */
describe('LocationSelect (address search field)', () => {
  it('does not search below two characters and debounces typing into one request', async () => {
    render(<LocationSelect value="" onChange={() => {}} customerId="cust-1" />)
    const input = screen.getByRole('combobox')

    const typedSearches = () =>
      pickerApi.pickAddresses.mock.calls.map(([params]) => params as { search: string }).filter((params) => params.search !== '')

    // (The click that focuses the empty field offers the customer's own addresses: search ''.)
    await userEvent.type(input, 'A')
    await new Promise((r) => setTimeout(r, 320))
    expect(typedSearches()).toEqual([])

    await userEvent.type(input, 'venue Sab')
    await waitFor(() => expect(typedSearches()).toHaveLength(1))
    expect(typedSearches()[0]).toMatchObject({ customerId: 'cust-1', search: 'Avenue Sab', take: 8 })
    expect((typedSearches()[0] as { signal?: unknown }).signal).toBeInstanceOf(AbortSignal)
  })

  it('shows location name, full address and owning customer per suggestion', async () => {
    render(<LocationSelect value="" onChange={() => {}} customerId="cust-1" />)
    await userEvent.type(screen.getByRole('combobox'), 'Avenue Sab')

    expect(await screen.findByText('Novellini')).toBeInTheDocument()
    expect(screen.getByText('Avenue Sabin 1, 1300 Waver')).toBeInTheDocument()
    expect(screen.getByText('Adres van deze klant · Van Caudenberg BV')).toBeInTheDocument()
    // Missing street parts are skipped; a row without customers shows its group only.
    expect(screen.getByText('9000 Gent')).toBeInTheDocument()
  })

  it('NEVER auto-selects: Enter without a highlighted row keeps the typed text and selects nothing', async () => {
    const onChange = vi.fn()
    const onSelectAddress = vi.fn()
    render(<LocationSelect value="" onChange={onChange} onSelectAddress={onSelectAddress} />)
    const input = screen.getByRole('combobox')

    await userEvent.type(input, 'Avenue Sab')
    await screen.findByText('Novellini')
    expect(input).not.toHaveAttribute('aria-activedescendant')

    await userEvent.keyboard('{Enter}')
    expect(onChange).not.toHaveBeenCalled()
    expect(onSelectAddress).not.toHaveBeenCalled()
    expect(input).toHaveValue('Avenue Sab')
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument()
  })

  it('selects explicitly with the arrow keys + Enter and hands over the full address record', async () => {
    const onChange = vi.fn()
    const onSelectAddress = vi.fn()
    render(<LocationSelect value="" onChange={onChange} onSelectAddress={onSelectAddress} />)
    const input = screen.getByRole('combobox')

    await userEvent.type(input, 'Avenue Sab')
    await screen.findByText('Novellini')
    await userEvent.keyboard('{ArrowDown}{Enter}')

    expect(onChange).toHaveBeenCalledWith('loc-1')
    expect(onSelectAddress).toHaveBeenCalledWith(
      expect.objectContaining({ locationId: 'loc-1', name: 'Novellini', street: 'Avenue Sabin', houseNumber: '1', postalCode: '1300', city: 'Waver', countryCode: 'BE' }),
    )
    // A search box: the query is cleared, the chosen address lives in the fields below it.
    expect(input).toHaveValue('')
  })

  it('selects on click, and Escape closes the list without touching the text', async () => {
    const onChange = vi.fn()
    render(<LocationSelect value="" onChange={onChange} />)
    const input = screen.getByRole('combobox')

    await userEvent.type(input, 'Gent')
    await screen.findByText('Depot Gent')
    await userEvent.keyboard('{Escape}')
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument()
    expect(input).toHaveValue('Gent')
    expect(onChange).not.toHaveBeenCalled()

    await userEvent.type(input, 'b')
    await userEvent.click(await screen.findByText('Depot Gent'))
    expect(onChange).toHaveBeenCalledWith('loc-2')
  })

  it('closes on a click outside', async () => {
    render(
      <div>
        <LocationSelect value="" onChange={() => {}} />
        <button type="button">elders</button>
      </div>,
    )
    await userEvent.type(screen.getByRole('combobox'), 'Avenue')
    await screen.findByText('Novellini')
    await userEvent.click(screen.getByRole('button', { name: 'elders' }))
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument()
  })

  it('never lets a stale response replace the newer one', async () => {
    const first = deferred<AddressPickerOption[]>()
    const second = deferred<AddressPickerOption[]>()
    pickerApi.pickAddresses.mockImplementation(({ search }: { search: string }) =>
      search === 'Av' ? first.promise : second.promise,
    )
    render(<LocationSelect value="" onChange={() => {}} />)
    const input = screen.getByRole('combobox')

    const callFor = (search: string) =>
      pickerApi.pickAddresses.mock.calls.map(([params]) => params as { search: string; signal: AbortSignal }).find((params) => params.search === search)

    await userEvent.type(input, 'Av')
    await waitFor(() => expect(callFor('Av')).toBeDefined())
    await userEvent.type(input, 'enue')
    await waitFor(() => expect(callFor('Avenue')).toBeDefined())
    // The superseded request was aborted as soon as the query moved on.
    expect(callFor('Av')!.signal.aborted).toBe(true)

    second.resolve([novellini])
    expect(await screen.findByText('Novellini')).toBeInTheDocument()

    first.resolve([depot])
    await new Promise((r) => setTimeout(r, 20))
    expect(screen.getByText('Novellini')).toBeInTheDocument()
    expect(screen.queryByText('Depot Gent')).not.toBeInTheDocument()
  })

  it('reports no results honestly and keeps the free text valid', async () => {
    pickerApi.pickAddresses.mockResolvedValue([])
    const onChange = vi.fn()
    render(<LocationSelect value="" onChange={onChange} />)
    const input = screen.getByRole('combobox')
    await userEvent.type(input, 'Onbekendstraat 5')
    expect(await screen.findByText('Geen bekend adres gevonden — vul het adres vrij in.')).toBeInTheDocument()
    expect(input).toHaveValue('Onbekendstraat 5')
    expect(onChange).not.toHaveBeenCalled()
  })

  it('reports a failed search instead of "no results"', async () => {
    pickerApi.pickAddresses.mockRejectedValue(new Error('boom'))
    render(<LocationSelect value="" onChange={() => {}} />)
    await userEvent.type(screen.getByRole('combobox'), 'Avenue')
    expect(await screen.findByText(/Zoeken is mislukt/)).toBeInTheDocument()
  })

  it('a click on the empty field offers the customer’s own and recent addresses', async () => {
    render(<LocationSelect value="" onChange={() => {}} customerId="cust-1" />)
    await userEvent.click(screen.getByRole('combobox'))
    await waitFor(() => expect(pickerApi.pickAddresses).toHaveBeenCalledTimes(1))
    expect(pickerApi.pickAddresses.mock.calls[0][0]).toMatchObject({ customerId: 'cust-1', search: '' })
    expect(await screen.findByText('Novellini')).toBeInTheDocument()
  })

  it('offers the inline-create row and fills from the created record', async () => {
    const onChange = vi.fn()
    const onSelectAddress = vi.fn()
    const onCreateNew = vi.fn().mockResolvedValue({ id: 'loc-9', code: 'NEW', name: 'Nieuwe werf', type: 'CustomerLocation', city: 'Mechelen' })
    locationsApi.getLocation.mockResolvedValue({
      id: 'loc-9', name: 'Nieuwe werf', street: 'Stationsstraat', houseNumber: '5', postalCode: '2800', city: 'Mechelen', countryCode: 'BE',
    })
    pickerApi.pickAddresses.mockResolvedValue([])
    render(<LocationSelect value="" onChange={onChange} onSelectAddress={onSelectAddress} onCreateNew={onCreateNew} />)

    await userEvent.type(screen.getByRole('combobox'), 'Nieuwe werf')
    await userEvent.click(await screen.findByText(/Nieuw adres “Nieuwe werf” aanmaken/))

    await waitFor(() => expect(onCreateNew).toHaveBeenCalledWith('Nieuwe werf'))
    await waitFor(() => expect(onChange).toHaveBeenCalledWith('loc-9'))
    expect(onSelectAddress).toHaveBeenCalledWith(
      expect.objectContaining({ locationId: 'loc-9', name: 'Nieuwe werf', street: 'Stationsstraat', houseNumber: '5', postalCode: '2800' }),
    )
  })
})
