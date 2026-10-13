import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { CustomerDetailPage } from '../CustomerDetailPage'
import type { CustomerContact, CustomerDetail } from '../../types'

/**
 * UX-sprint 2026-09-09 §1.1 root cause 2: after a contact save the page reloads the customer.
 * It used to swap the whole tree for the loading state, unmounting the panel and the dialog
 * while the notification PUT was still to be sent. With a customer already on screen the
 * reload must be invisible (stale-while-refetch).
 */

vi.mock('../../../auth/authContextValue', () => ({
  useAuth: () => ({
    hasPermission: (code: string) => code === 'customers.view',
    hasAnyPermission: (codes: string[]) => codes.includes('customers.view'),
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

// Every render of the page-level loading state is recorded, so "never shown again" is provable.
const loading = vi.hoisted(() => ({ renders: [] as string[] }))
vi.mock('../../../../components/feedback/LoadingState', () => ({
  LoadingState: ({ message }: { message?: string }) => {
    loading.renders.push(message ?? '')
    return <p>{message}</p>
  },
}))

function contact(overrides: Partial<CustomerContact> = {}): CustomerContact {
  return {
    id: 'ct-1',
    firstName: 'Jan',
    lastName: 'Peeters',
    displayName: null,
    nickname: null,
    role: null,
    contactType: 'Algemeen',
    departmentId: null,
    departmentName: null,
    email: 'jan@example.com',
    phoneNumber: null,
    mobilePhone: null,
    preferredLanguageCode: null,
    isPrimary: false,
    isActive: true,
    notes: null,
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
  updateCustomerContact: vi.fn(),
  addCustomerContact: vi.fn(),
  removeCustomerContact: vi.fn(),
}))
vi.mock('../../api/customersApi', () => ({
  getCustomer: (...a: unknown[]) => api.getCustomer(...a),
  updateCustomerContact: (...a: unknown[]) => api.updateCustomerContact(...a),
  addCustomerContact: (...a: unknown[]) => api.addCustomerContact(...a),
  removeCustomerContact: (...a: unknown[]) => api.removeCustomerContact(...a),
  changeCustomerNumber: vi.fn(),
  getCustomerFiscalWarnings: () => Promise.resolve([]),
}))

function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((res) => {
    resolve = res
  })
  return { promise, resolve }
}

function renderPage() {
  return render(
    <MemoryRouter initialEntries={['/customers/c1']}>
      <Routes>
        <Route path="/customers/:id" element={<CustomerDetailPage />} />
      </Routes>
    </MemoryRouter>,
  )
}

describe('CustomerDetailPage — stale-while-refetch after a contact save', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    loading.renders = []
    api.updateCustomerContact.mockResolvedValue(undefined)
  })

  it('keeps the loaded customer on screen while the reload after a contact save is in flight', async () => {
    const refetch = deferred<CustomerDetail>()
    api.getCustomer.mockResolvedValueOnce(customer()).mockReturnValueOnce(refetch.promise)
    renderPage()

    await waitFor(() => expect(screen.getByRole('heading', { name: 'Haven BV' })).toBeInTheDocument())
    // The very first load legitimately showed the loading state — once.
    const initialLoadingRenders = loading.renders.filter((m) => m === 'Klant laden...').length
    expect(initialLoadingRenders).toBeGreaterThan(0)

    await userEvent.click(await screen.findByRole('tab', { name: /Contactpersonen/ }))
    await userEvent.click(screen.getByRole('button', { name: 'Bewerken' }))
    const dialog = await screen.findByRole('dialog')
    await userEvent.type(within(dialog).getByLabelText(/Functie/), 'Planner')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Opslaan' }))

    await waitFor(() => expect(api.updateCustomerContact).toHaveBeenCalledTimes(1))
    expect(api.updateCustomerContact).toHaveBeenCalledWith('c1', 'ct-1', expect.objectContaining({ role: 'Planner' }))
    // The save triggered exactly one reload, which is still pending …
    await waitFor(() => expect(api.getCustomer).toHaveBeenCalledTimes(2))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())

    // … and meanwhile the page never fell back to the loading state: the tree stays mounted.
    expect(screen.getByRole('heading', { name: 'Haven BV' })).toBeInTheDocument()
    expect(screen.queryByText('Klant laden...')).not.toBeInTheDocument()
    expect(screen.getByRole('tab', { name: /Contactpersonen/ })).toHaveAttribute('aria-selected', 'true')
    expect(loading.renders.filter((m) => m === 'Klant laden...').length).toBe(initialLoadingRenders)

    refetch.resolve(customer({ contacts: [contact({ role: 'Planner' })] }))
    await waitFor(() => expect(screen.getByText('Planner')).toBeInTheDocument())
    expect(loading.renders.filter((m) => m === 'Klant laden...').length).toBe(initialLoadingRenders)
    expect(screen.getByRole('heading', { name: 'Haven BV' })).toBeInTheDocument()
  })
})
