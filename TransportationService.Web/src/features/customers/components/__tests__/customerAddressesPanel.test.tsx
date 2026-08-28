import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { CustomerAddressesPanel } from '../CustomerAddressesPanel'
import type { CustomerAddress, AddressPickerOption } from '../../../locations/api/customerAddressesApi'

/**
 * Sprint 2 — the customer's Adressen tab manages the RELATIONSHIP to a shared physical address.
 */

const auth = vi.hoisted(() => ({ permissions: ['locations.view', 'locations.edit', 'locations.create'] }))
vi.mock('../../../auth/authContextValue', () => ({
  useAuth: () => ({
    status: 'authenticated' as const,
    user: null,
    login: vi.fn(),
    logout: vi.fn(),
    hasPermission: (code: string) => auth.permissions.includes(code),
    hasAnyPermission: (codes: string[]) => codes.some((c) => auth.permissions.includes(c)),
  }),
}))

const toast = vi.hoisted(() => ({ showSuccess: vi.fn(), showError: vi.fn(), showToast: vi.fn() }))
vi.mock('../../../../components/ui/toastContext', () => ({ useToast: () => toast }))

const api = vi.hoisted(() => ({
  list: vi.fn(),
  link: vi.fn(),
  unlink: vi.fn(),
  update: vi.fn(),
  picker: vi.fn(),
  check: vi.fn(),
  createLocation: vi.fn(),
}))
vi.mock('../../../locations/api/customerAddressesApi', async (orig) => ({
  ...(await orig<typeof import('../../../locations/api/customerAddressesApi')>()),
  listCustomerAddresses: (...a: unknown[]) => api.list(...a),
  linkCustomerAddress: (...a: unknown[]) => api.link(...a),
  unlinkCustomerAddress: (...a: unknown[]) => api.unlink(...a),
  updateCustomerAddressLink: (...a: unknown[]) => api.update(...a),
  pickAddresses: (...a: unknown[]) => api.picker(...a),
  checkAddressDuplicates: (...a: unknown[]) => api.check(...a),
}))
vi.mock('../../../locations/api/locationsApi', async (orig) => ({
  ...(await orig<typeof import('../../../locations/api/locationsApi')>()),
  createLocation: (...a: unknown[]) => api.createLocation(...a),
}))
vi.mock('../../../reference/components/CountryCombobox', () => ({
  CountryCombobox: ({ id }: { id?: string }) => <input id={id} aria-label="Land" />,
}))

function address(overrides: Partial<CustomerAddress> = {}): CustomerAddress {
  return {
    linkId: 'link-1',
    locationId: 'loc-1',
    customerId: 'c1',
    code: 'ADR-1',
    name: 'Magazijn Noord',
    alias: null,
    customerReference: null,
    type: 'CustomerLocation',
    role: 'Both',
    isDefaultLoading: false,
    isDefaultUnloading: false,
    isDefaultBilling: false,
    instructions: null,
    isActive: true,
    addressIsActive: true,
    street: 'Noorderlaan',
    houseNumber: '10',
    postalCode: '2030',
    city: 'Antwerpen',
    countryCode: 'BE',
    linkedCustomerCount: 1,
    ...overrides,
  }
}

function pickerOption(overrides: Partial<AddressPickerOption> = {}): AddressPickerOption {
  return {
    locationId: 'loc-2',
    code: 'ADR-2',
    name: 'Gedeeld magazijn',
    type: 'CustomerLocation',
    street: 'Zuidlaan',
    houseNumber: '5',
    postalCode: '9000',
    city: 'Gent',
    countryCode: 'BE',
    group: 'CustomerAddress',
    ...overrides,
  }
}

function renderPanel() {
  return render(
    <MemoryRouter>
      <CustomerAddressesPanel customerId="c1" />
    </MemoryRouter>,
  )
}

beforeEach(() => {
  vi.clearAllMocks()
  auth.permissions = ['locations.view', 'locations.edit', 'locations.create']
  api.list.mockResolvedValue([address()])
  api.picker.mockResolvedValue([pickerOption()])
  api.link.mockResolvedValue(address({ linkId: 'link-2', locationId: 'loc-2' }))
  api.unlink.mockResolvedValue(undefined)
  api.check.mockResolvedValue({ hasExactMatch: false, candidates: [] })
  api.createLocation.mockResolvedValue({
    id: 'loc-new',
    code: 'LOC-NEW',
    name: 'Nieuw magazijn',
    type: 'CustomerLocation',
    city: 'Gent',
    isDefaultLoadingLocation: false,
    isDefaultUnloadingLocation: false,
    isDefaultBillingLocation: false,
  })
})

describe('CustomerAddressesPanel', () => {
  it('lists the relationship with its address and role', async () => {
    renderPanel()
    expect(await screen.findByText('Magazijn Noord')).toBeInTheDocument()
    expect(screen.getByText('Noorderlaan 10, 2030 Antwerpen')).toBeInTheDocument()
    expect(screen.getByText('Laden en lossen')).toBeInTheDocument()
  })

  it('shows that an address is shared with other customers', async () => {
    api.list.mockResolvedValue([address({ linkedCustomerCount: 3 })])
    renderPanel()
    expect(await screen.findByText('Gedeeld met 3 klanten')).toBeInTheDocument()
  })

  it('links an existing central address instead of creating a new one', async () => {
    renderPanel()
    await screen.findByText('Magazijn Noord')

    await userEvent.click(screen.getByRole('button', { name: 'Bestaand adres koppelen' }))
    await userEvent.click(await screen.findByRole('button', { name: 'Gedeeld magazijn' }))

    await waitFor(() => expect(api.link).toHaveBeenCalledTimes(1))
    expect(api.link).toHaveBeenCalledWith('c1', expect.objectContaining({ locationId: 'loc-2' }))
  })

  it('asks the server to leave out already-linked addresses (before the take), not the client', async () => {
    renderPanel()
    await screen.findByText('Magazijn Noord')

    await userEvent.click(screen.getByRole('button', { name: 'Bestaand adres koppelen' }))

    expect(await screen.findByRole('button', { name: 'Gedeeld magazijn' })).toBeInTheDocument()
    expect(api.picker).toHaveBeenCalledWith(expect.objectContaining({ excludeCustomerId: 'c1', take: 50 }))
  })

  it('creates a new address WITHOUT a second link call and reloads the list (D1)', async () => {
    renderPanel()
    await screen.findByText('Magazijn Noord')
    expect(api.list).toHaveBeenCalledTimes(1)

    await userEvent.click(screen.getByRole('button', { name: '+ Nieuw adres' }))
    const dialog = await screen.findByRole('dialog')
    await userEvent.type(within(dialog).getByLabelText(/^Naam/), 'Nieuw magazijn')
    await userEvent.type(within(dialog).getByLabelText('Straat'), 'Zuidlaan')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Adres aanmaken' }))

    await waitFor(() => expect(api.createLocation).toHaveBeenCalledTimes(1))
    // The server links the address through customerId; a follow-up link would 409.
    expect(api.createLocation).toHaveBeenCalledWith(expect.objectContaining({ customerId: 'c1', overrideDuplicate: false }))
    expect(api.link).not.toHaveBeenCalled()
    await waitFor(() => expect(api.list).toHaveBeenCalledTimes(2))
    expect(toast.showSuccess).toHaveBeenCalledWith('Adres aangemaakt en aan deze klant gekoppeld.')
    expect(toast.showError).not.toHaveBeenCalled()
  })

  it('links (does not create) when the user picks an existing address from the duplicate warning', async () => {
    api.check.mockResolvedValue({
      hasExactMatch: true,
      candidates: [
        {
          locationId: 'loc-2',
          code: 'ADR-2',
          name: 'Gedeeld magazijn',
          match: 'Exact',
          street: 'Zuidlaan',
          houseNumber: '5',
          postalCode: '9000',
          city: 'Gent',
          countryCode: 'BE',
          isActive: true,
          linkedCustomers: ['Klant B'],
          type: 'Warehouse',
        },
        {
          locationId: 'loc-3',
          code: 'ADR-3',
          name: 'Oud magazijn',
          match: 'Exact',
          street: 'Zuidlaan',
          houseNumber: '5',
          postalCode: '9000',
          city: 'Gent',
          countryCode: 'BE',
          isActive: false,
          linkedCustomers: [],
          type: 'Warehouse',
        },
      ],
    })
    renderPanel()
    await screen.findByText('Magazijn Noord')

    await userEvent.click(screen.getByRole('button', { name: '+ Nieuw adres' }))
    const dialog = await screen.findByRole('dialog')
    await userEvent.type(within(dialog).getByLabelText(/^Naam/), 'Nog een magazijn')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Adres aanmaken' }))

    // Only the ACTIVE candidate is offered for reuse; the inactive one is shown for context only.
    const useExisting = await within(dialog).findAllByRole('button', { name: 'Bestaand adres gebruiken' })
    expect(useExisting).toHaveLength(1)
    await userEvent.click(useExisting[0])

    await waitFor(() => expect(api.link).toHaveBeenCalledWith('c1', expect.objectContaining({ locationId: 'loc-2' })))
    expect(api.createLocation).not.toHaveBeenCalled()
  })

  it('unlinks the relationship and says the address itself is kept', async () => {
    renderPanel()
    await screen.findByText('Magazijn Noord')

    // The row action opens the confirmation.
    await userEvent.click(screen.getByRole('button', { name: 'Ontkoppelen' }))
    const dialog = await screen.findByRole('dialog')
    expect(
      within(dialog).getByText(/Het adres zelf blijft bestaan voor andere klanten en voor bestaande opdrachten\./),
    ).toBeInTheDocument()

    await userEvent.click(within(dialog).getByRole('button', { name: 'Ontkoppelen' }))

    await waitFor(() => expect(api.unlink).toHaveBeenCalledWith('c1', 'link-1'))
  })

  it('hides the management actions without locations.edit', async () => {
    auth.permissions = ['locations.view']
    renderPanel()
    await screen.findByText('Magazijn Noord')

    expect(screen.queryByRole('button', { name: 'Bestaand adres koppelen' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Ontkoppelen' })).not.toBeInTheDocument()
  })
})
