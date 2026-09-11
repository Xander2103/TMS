import { describe, expect, it, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { LocationSelect } from '../LocationSelect'
import type { AddressPickerOption } from '../../api/customerAddressesApi'

const pickerApi = vi.hoisted(() => ({ pickAddresses: vi.fn() }))
vi.mock('../../api/customerAddressesApi', () => pickerApi)

const locationsApi = vi.hoisted(() => ({ getLocation: vi.fn() }))
vi.mock('../../api/locationsApi', () => locationsApi)

const warehouse: AddressPickerOption = {
  locationId: 'loc-1',
  code: 'LOC-1',
  name: 'Magazijn Antwerpen',
  type: 'Warehouse',
  street: 'Noorderlaan',
  houseNumber: '10',
  postalCode: '2030',
  city: 'Antwerpen',
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

const zaventem: AddressPickerOption = {
  locationId: 'loc-3',
  code: 'ZAV',
  name: 'Hub Zaventem',
  type: 'Terminal',
  street: 'Luchthavenlaan',
  houseNumber: '1',
  postalCode: '1930',
  city: 'Zaventem',
  countryCode: 'BE',
  group: 'All',
}

function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((res) => {
    resolve = res
  })
  return { promise, resolve }
}

beforeEach(() => {
  pickerApi.pickAddresses.mockReset().mockResolvedValue([warehouse, depot])
  locationsApi.getLocation.mockReset()
})

describe('LocationSelect (server-side picker)', () => {
  it('opens with the customer suggestions and renders rich rows', async () => {
    render(<LocationSelect value="" onChange={() => {}} customerId="cust-1" />)
    await userEvent.click(screen.getByRole('combobox'))

    await waitFor(() => expect(pickerApi.pickAddresses).toHaveBeenCalledTimes(1))
    expect(pickerApi.pickAddresses.mock.calls[0][0]).toMatchObject({ customerId: 'cust-1', search: '', take: 20 })
    expect(pickerApi.pickAddresses.mock.calls[0][0].signal).toBeInstanceOf(AbortSignal)

    expect(await screen.findByText('Magazijn Antwerpen (LOC-1)')).toBeInTheDocument()
    expect(screen.getByText('Noorderlaan 10, 2030 Antwerpen')).toBeInTheDocument()
    expect(screen.getByText('Klantadres · Van Caudenberg BV')).toBeInTheDocument()
    // Code equal to the name is not repeated; missing street parts are skipped.
    expect(screen.getByText('Depot Gent')).toBeInTheDocument()
    expect(screen.getByText('9000 Gent')).toBeInTheDocument()
    expect(screen.getByText('Recent gebruikt')).toBeInTheDocument()
    // Nothing is fetched for an empty value.
    expect(locationsApi.getLocation).not.toHaveBeenCalled()
  })

  it('debounces typing into one server search and selects by locationId', async () => {
    pickerApi.pickAddresses.mockImplementation(async ({ search }: { search: string }) =>
      search === 'zaven' ? [zaventem] : [warehouse, depot],
    )
    const onChange = vi.fn()
    render(<LocationSelect value="" onChange={onChange} customerId="cust-1" />)

    const combobox = screen.getByRole('combobox')
    await userEvent.click(combobox)
    await screen.findByText('Magazijn Antwerpen (LOC-1)')

    await userEvent.type(combobox, 'zaven')
    await waitFor(() => expect(pickerApi.pickAddresses).toHaveBeenCalledTimes(2))
    expect(pickerApi.pickAddresses.mock.calls[1][0]).toMatchObject({ customerId: 'cust-1', search: 'zaven' })

    const row = await screen.findByText('Hub Zaventem (ZAV)')
    expect(screen.getByText('Alle adressen')).toBeInTheDocument()
    await userEvent.click(row)
    expect(onChange).toHaveBeenCalledWith('loc-3')
  })

  it('never lets a stale response replace the newer one', async () => {
    const first = deferred<AddressPickerOption[]>()
    const second = deferred<AddressPickerOption[]>()
    pickerApi.pickAddresses.mockImplementation(() =>
      pickerApi.pickAddresses.mock.calls.length === 1 ? first.promise : second.promise,
    )
    render(<LocationSelect value="" onChange={() => {}} />)

    const combobox = screen.getByRole('combobox')
    await userEvent.click(combobox)
    await waitFor(() => expect(pickerApi.pickAddresses).toHaveBeenCalledTimes(1))
    await userEvent.type(combobox, 'z')
    await waitFor(() => expect(pickerApi.pickAddresses).toHaveBeenCalledTimes(2))

    second.resolve([zaventem])
    expect(await screen.findByText('Hub Zaventem (ZAV)')).toBeInTheDocument()

    first.resolve([warehouse, depot])
    await new Promise((r) => setTimeout(r, 20))
    expect(screen.getByText('Hub Zaventem (ZAV)')).toBeInTheDocument()
    expect(screen.queryByText('Magazijn Antwerpen (LOC-1)')).not.toBeInTheDocument()
  })

  it('resolves the label of a pre-set value through getLocation', async () => {
    locationsApi.getLocation.mockResolvedValue({
      id: 'loc-1',
      name: 'Magazijn Antwerpen',
      street: 'Noorderlaan',
      houseNumber: '10',
      postalCode: '2030',
      city: 'Antwerpen',
    })
    render(<LocationSelect value="loc-1" onChange={() => {}} />)

    await waitFor(() =>
      expect((screen.getByRole('combobox') as HTMLInputElement).value).toBe(
        'Magazijn Antwerpen — Noorderlaan 10, 2030 Antwerpen',
      ),
    )
    expect(locationsApi.getLocation).toHaveBeenCalledTimes(1)
    expect(locationsApi.getLocation).toHaveBeenCalledWith('loc-1')
  })

  it('filters on type client-side when a type is requested', async () => {
    render(<LocationSelect value="" onChange={() => {}} type="Depot" />)
    await userEvent.click(screen.getByRole('combobox'))
    expect(await screen.findByText('Depot Gent')).toBeInTheDocument()
    expect(screen.queryByText('Magazijn Antwerpen (LOC-1)')).not.toBeInTheDocument()
  })

  it('shows the create row and auto-selects the created address', async () => {
    pickerApi.pickAddresses.mockResolvedValue([])
    const onCreateNew = vi.fn(async (name: string) => ({
      id: 'new-1',
      code: 'NEW-1',
      name,
      type: 'Depot' as const,
      city: 'Brussel',
      isDefaultLoadingLocation: false,
      isDefaultUnloadingLocation: false,
      isDefaultBillingLocation: false,
    }))
    const onChange = vi.fn()
    render(<LocationSelect value="" onChange={onChange} onCreateNew={onCreateNew} />)

    const combobox = screen.getByRole('combobox')
    await userEvent.type(combobox, 'Kade 9')
    const row = await screen.findByRole('option', { name: '+ Nieuw adres “Kade 9” aanmaken…' })
    await userEvent.click(row)

    await waitFor(() => expect(onCreateNew).toHaveBeenCalledWith('Kade 9'))
    expect(onChange).toHaveBeenCalledWith('new-1')
    expect(locationsApi.getLocation).not.toHaveBeenCalled()
  })
})
