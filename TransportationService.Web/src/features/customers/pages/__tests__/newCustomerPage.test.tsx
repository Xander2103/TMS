import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes, useParams } from 'react-router-dom'
import { NewCustomerPage } from '../NewCustomerPage'
import type { CustomerDetail } from '../../types'

const auth = vi.hoisted(() => ({ permissions: ['customers.manage_fiscal', 'customers.view', 'locations.create'] }))
const toast = vi.hoisted(() => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }))
const createCustomerSpy = vi.hoisted(() => vi.fn())
const createLocationSpy = vi.hoisted(() => vi.fn())

vi.mock('../../../auth/authContextValue', () => ({
  useAuth: () => ({
    status: 'authenticated' as const,
    user: null,
    login: vi.fn(),
    logout: vi.fn(),
    hasPermission: (code: string) => auth.permissions.includes(code),
    hasAnyPermission: (codes: string[]) => codes.some((code) => auth.permissions.includes(code)),
  }),
}))
vi.mock('../../../../components/ui/toastContext', () => ({
  useToast: () => toast,
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
vi.mock('../../../../components/ui/UnsavedChangesGuard', () => ({
  UnsavedChangesGuard: () => null,
}))
vi.mock('../../api/customersApi', () => ({
  createCustomer: (...args: unknown[]) => createCustomerSpy(...args),
  updateCustomer: vi.fn(),
  deleteCustomer: vi.fn(),
  setCustomerActive: vi.fn(),
  setCustomerBlocked: vi.fn(),
  addCustomerContact: vi.fn(),
  updateCustomerContact: vi.fn(),
  removeCustomerContact: vi.fn(),
  getVatTreatments: () => Promise.resolve([]),
  getPeppolSchemes: () => Promise.resolve([]),
  registryLookup: vi.fn(),
  verifyCustomerPeppol: vi.fn(),
}))
vi.mock('../../../locations/api/locationsApi', () => ({
  createLocation: (...args: unknown[]) => createLocationSpy(...args),
}))

function createdCustomer(): CustomerDetail {
  return {
    id: 'c1', customerNumber: 'KL-9', name: 'Acme', legalName: null, vatNumber: null,
    categoryId: null, categoryName: null, email: null, phoneNumber: null, website: null,
    street: null, houseNumber: null, postalCode: null, city: null, countryCode: null,
    invoiceEmail: null, paymentTermDays: 30, defaultLanguageCode: null, notes: null,
    isActive: true, isBlocked: false, blockReason: null, nickname: null, companyNumber: null,
    currencyCode: 'EUR', iban: null, bic: null, bankName: null, bankAccountNumber: null,
    defaultLegalEntityId: null, contacts: [],
    vatTreatment: 'DomesticVat', defaultVatRatePercent: null, vatCountryCode: null, vatNotes: null,
    peppolId: null, peppolScheme: null, invoiceLanguageCode: null, purchaseOrderRequired: false,
    signedDeliveryNoteRequired: false, customerReferenceRequired: false,
    peppolEnabled: false, peppolDeliveryPreference: 'Peppol', buyerReference: null,
    peppolValidationStatus: 'Unknown', peppolValidatedAt: null, peppolValidationReference: null,
  }
}

function DetailProbe() {
  const { id } = useParams()
  return <div>DETAIL {id}</div>
}

function renderPage() {
  return render(
    <MemoryRouter initialEntries={['/customers/new']}>
      <Routes>
        <Route path="/customers/new" element={<NewCustomerPage />} />
        <Route path="/customers/:id" element={<DetailProbe />} />
      </Routes>
    </MemoryRouter>,
  )
}

async function stageLocation(name: string) {
  await userEvent.click(screen.getByRole('button', { name: '+ Locatie toevoegen' }))
  await userEvent.type(screen.getByLabelText(/^Naam/, { selector: '#sl-name' }), name)
  await userEvent.click(screen.getByRole('button', { name: 'Toevoegen' }))
}

function saveButton() {
  return screen.getAllByRole('button', { name: 'Opslaan' })[0]
}

describe('NewCustomerPage — staged locations + save-and-new', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    createCustomerSpy.mockResolvedValue(createdCustomer())
    createLocationSpy.mockResolvedValue({ id: 'loc-1' })
  })

  it('creates every staged location with the new customerId and navigates to the detail', async () => {
    renderPage()
    await userEvent.type(screen.getByRole('textbox', { name: 'Naam' }), 'Acme')
    await stageLocation('Depot A')
    await stageLocation('Depot B')

    // Staged rows render as compact cards; nothing is posted yet.
    expect(screen.getByText('Depot A')).toBeInTheDocument()
    expect(screen.getByText('Depot B')).toBeInTheDocument()
    expect(createLocationSpy).not.toHaveBeenCalled()

    await userEvent.click(saveButton())

    await waitFor(() => expect(createLocationSpy).toHaveBeenCalledTimes(2))
    expect(createLocationSpy).toHaveBeenNthCalledWith(1, expect.objectContaining({ name: 'Depot A', customerId: 'c1' }))
    expect(createLocationSpy).toHaveBeenNthCalledWith(2, expect.objectContaining({ name: 'Depot B', customerId: 'c1' }))
    expect(await screen.findByText('DETAIL c1')).toBeInTheDocument()
  })

  it('offers retry when part of the staged locations fails, then navigates after a successful retry', async () => {
    createLocationSpy.mockImplementation((input: { name: string }) =>
      input.name === 'Depot B' && createLocationSpy.mock.calls.length <= 2
        ? Promise.reject(new Error('Locatiecode bestaat al.'))
        : Promise.resolve({ id: 'loc-x' }),
    )

    renderPage()
    await userEvent.type(screen.getByRole('textbox', { name: 'Naam' }), 'Acme')
    await stageLocation('Depot A')
    await stageLocation('Depot B')
    await userEvent.click(saveButton())

    // Partial failure: the customer exists, the retry dialog lists the failed location.
    expect(await screen.findByText('Klant aangemaakt — locaties deels mislukt')).toBeInTheDocument()
    expect(screen.getByText(/Locatiecode bestaat al\./)).toBeInTheDocument()
    expect(toast.showError).toHaveBeenCalled()
    expect(screen.queryByText('DETAIL c1')).not.toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: /Mislukte opnieuw proberen/ }))

    await waitFor(() => expect(createLocationSpy).toHaveBeenCalledTimes(3))
    // The retry only replays the failed row.
    expect(createLocationSpy).toHaveBeenNthCalledWith(3, expect.objectContaining({ name: 'Depot B', customerId: 'c1' }))
    expect(await screen.findByText('DETAIL c1')).toBeInTheDocument()
  })

  it('resets the create page in place for "Opslaan en nieuwe klant" and shows a toast', async () => {
    renderPage()
    await userEvent.type(screen.getByRole('textbox', { name: 'Naam' }), 'Acme')
    await userEvent.click(screen.getAllByRole('button', { name: 'Opslaan en nieuwe klant' })[0])

    await waitFor(() => expect(toast.showSuccess).toHaveBeenCalledWith('Klant KL-9 aangemaakt.'))
    // Still on the create page, with a fresh (remounted) form.
    expect(screen.queryByText('DETAIL c1')).not.toBeInTheDocument()
    await waitFor(() => expect(screen.getByRole('textbox', { name: 'Naam' })).toHaveValue(''))
  })
})
