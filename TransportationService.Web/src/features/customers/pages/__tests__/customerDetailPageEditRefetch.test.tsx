import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { CustomerDetailPage } from '../CustomerDetailPage'
import type { CustomerContact, CustomerDetail } from '../../types'

/**
 * Hardening 2026-09-10 — stale-while-refetch with UNSAVED page-level edits: the customer edit
 * form hosts the self-saving contacts panel. A contact save reloads the customer; the fresh
 * payload must never replace what the user typed elsewhere on the page (the form derives its
 * state from `initial` once and is never remounted by the reload).
 */

vi.mock('../../../auth/authContextValue', () => ({
  useAuth: () => ({
    hasPermission: (code: string) =>
      ['customers.view', 'customers.edit', 'customers.manage_communication', 'customers.manage_fiscal'].includes(code),
    hasAnyPermission: (codes: string[]) => codes.some((code) => ['customers.view', 'customers.edit'].includes(code)),
  }),
}))
vi.mock('../../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }),
}))
vi.mock('../../../master-data/hooks/useLookupOptions', () => ({
  useLookupOptions: () => ({ options: [], isLoading: false, error: null }),
}))
vi.mock('../../../master-data/components/LookupSelect', () => ({
  LookupSelect: ({ id }: { id?: string }) => <input id={id} aria-label="lookup" />,
}))
vi.mock('../../../reference/components/CountryCombobox', () => ({
  CountryCombobox: ({ id }: { id?: string }) => <input id={id} aria-label="Land" />,
}))
vi.mock('../../../legal-entities/api/legalEntitiesApi', () => ({ getLegalEntityOptions: () => Promise.resolve([]) }))
const notificationsApi = vi.hoisted(() => ({ options: vi.fn(), get: vi.fn(), set: vi.fn() }))
vi.mock('../../api/customerNotificationsApi', async (orig) => ({
  ...(await orig<typeof import('../../api/customerNotificationsApi')>()),
  getNotificationOptions: () => notificationsApi.options(),
  getContactNotifications: (...a: unknown[]) => notificationsApi.get(...a),
  setContactNotifications: (...a: unknown[]) => notificationsApi.set(...a),
}))

function contact(overrides: Partial<CustomerContact> = {}): CustomerContact {
  return {
    id: 'ct-1', firstName: 'Jan', lastName: 'Peeters', displayName: null, nickname: null, role: null,
    contactType: 'Algemeen', departmentId: null, departmentName: null, email: 'jan@example.com',
    phoneNumber: null, mobilePhone: null, preferredLanguageCode: null, isPrimary: false, isActive: true, notes: null,
    ...overrides,
  } as CustomerContact
}

function customer(overrides: Partial<CustomerDetail> = {}): CustomerDetail {
  return {
    id: 'c1', customerNumber: 'KL-1', name: 'Haven BV', legalName: null, vatNumber: null,
    categoryId: null, categoryName: null, email: null, phoneNumber: null, website: null,
    street: null, houseNumber: null, postalCode: null, city: null, countryCode: null,
    invoiceEmail: null, paymentTermDays: 30, defaultLanguageCode: null, notes: null,
    isActive: true, isBlocked: false, blockReason: null, nickname: null, companyNumber: null,
    currencyCode: 'EUR', iban: null, bic: null, bankName: null, bankAccountNumber: null,
    defaultLegalEntityId: null, contacts: [contact()],
    vatTreatment: 'DomesticVat', defaultVatRatePercent: null, vatCountryCode: null, vatNotes: null,
    peppolId: null, peppolScheme: null, invoiceLanguageCode: null, purchaseOrderRequired: false,
    signedDeliveryNoteRequired: false, customerReferenceRequired: false,
    peppolEnabled: false, peppolDeliveryPreference: 'Peppol', buyerReference: null,
    peppolValidationStatus: 'Unknown', peppolValidatedAt: null, peppolValidationReference: null,
    ...overrides,
  } as CustomerDetail
}

const api = vi.hoisted(() => ({
  getCustomer: vi.fn(),
  updateCustomer: vi.fn(),
  updateCustomerContact: vi.fn(),
  addCustomerContact: vi.fn(),
  removeCustomerContact: vi.fn(),
}))
vi.mock('../../api/customersApi', () => ({
  getCustomer: (...a: unknown[]) => api.getCustomer(...a),
  updateCustomer: (...a: unknown[]) => api.updateCustomer(...a),
  updateCustomerContact: (...a: unknown[]) => api.updateCustomerContact(...a),
  addCustomerContact: (...a: unknown[]) => api.addCustomerContact(...a),
  removeCustomerContact: (...a: unknown[]) => api.removeCustomerContact(...a),
  changeCustomerNumber: vi.fn(),
  getCustomerFiscalWarnings: () => Promise.resolve([]),
  getVatTreatments: () => Promise.resolve([]),
  getPeppolSchemes: () => Promise.resolve([]),
}))

function renderPage() {
  const router = createMemoryRouter([{ path: '/customers/:id', element: <CustomerDetailPage /> }], { initialEntries: ['/customers/c1'] })
  return render(<RouterProvider router={router} />)
}

describe('CustomerDetailPage — unsaved page edits survive a self-saving panel reload', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    notificationsApi.options.mockResolvedValue([{ key: 'planning', group: 'Transport' }])
    notificationsApi.get.mockResolvedValue({ contactId: 'ct-1', optionKeys: [] })
    notificationsApi.set.mockResolvedValue({ contactId: 'ct-1', optionKeys: ['planning'] })
    api.updateCustomerContact.mockResolvedValue(undefined)
  })

  it('keeps a typed customer name while a contact save reloads the customer with different data', async () => {
    // The reload returns a customer whose name differs from what the user is typing.
    api.getCustomer
      .mockResolvedValueOnce(customer())
      .mockResolvedValueOnce(customer({ name: 'Haven BV (server)', contacts: [contact({ role: 'Planner' })] }))
    renderPage()
    await waitFor(() => expect(screen.getByRole('heading', { name: 'Haven BV' })).toBeInTheDocument())

    await userEvent.click(screen.getByRole('button', { name: 'Bewerken' }))
    const name = await screen.findByLabelText(/^Naam/)
    await userEvent.clear(name)
    await userEvent.type(name, 'Haven Antwerpen BV')
    expect(name).toHaveValue('Haven Antwerpen BV')

    // Self-saving contacts panel inside the same edit form.
    await userEvent.click(screen.getByRole('tab', { name: /Contactpersonen/i }))
    await userEvent.click(screen.getByRole('button', { name: 'Bewerken' }))
    const dialog = await screen.findByRole('dialog')
    await userEvent.type(within(dialog).getByLabelText(/Functie/), 'Planner')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Opslaan' }))

    await waitFor(() => expect(api.updateCustomerContact).toHaveBeenCalledTimes(1))
    await waitFor(() => expect(api.getCustomer).toHaveBeenCalledTimes(2))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    // The reload landed (the panel shows the fresh contact row) …
    await waitFor(() => expect(screen.getByText('Planner')).toBeInTheDocument())

    // … while the page-level edit is untouched: still in edit mode, same section, typed value intact.
    expect(screen.getByRole('tab', { name: /Contactpersonen/i })).toHaveAttribute('aria-selected', 'true')
    await userEvent.click(screen.getByRole('tab', { name: /Klantgegevens/i }))
    expect(screen.getByLabelText(/^Naam/)).toHaveValue('Haven Antwerpen BV')
    expect(api.updateCustomer).not.toHaveBeenCalled()
  })
})
