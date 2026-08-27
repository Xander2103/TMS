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
}))
vi.mock('../../../locations/api/customerAddressesApi', async (orig) => ({
  ...(await orig<typeof import('../../../locations/api/customerAddressesApi')>()),
  listCustomerAddresses: (...a: unknown[]) => api.list(...a),
  linkCustomerAddress: (...a: unknown[]) => api.link(...a),
  unlinkCustomerAddress: (...a: unknown[]) => api.unlink(...a),
  updateCustomerAddressLink: (...a: unknown[]) => api.update(...a),
  pickAddresses: (...a: unknown[]) => api.picker(...a),
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

  it('does not offer an address that is already linked', async () => {
    api.picker.mockResolvedValue([pickerOption(), pickerOption({ locationId: 'loc-1', name: 'Magazijn Noord' })])
    renderPanel()
    await screen.findByText('Magazijn Noord')

    await userEvent.click(screen.getByRole('button', { name: 'Bestaand adres koppelen' }))

    expect(await screen.findByRole('button', { name: 'Gedeeld magazijn' })).toBeInTheDocument()
    // The already-linked one is filtered out of the candidate list (the table row stays).
    expect(screen.queryByRole('button', { name: 'Magazijn Noord' })).not.toBeInTheDocument()
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
