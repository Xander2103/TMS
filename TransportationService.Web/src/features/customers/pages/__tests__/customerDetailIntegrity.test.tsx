import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { ApiError } from '../../../../api/apiClient'
import { CustomerDetailPage } from '../CustomerDetailPage'
import type { CustomerDetail } from '../../types'

/**
 * Master-data remediation 2026-10-18 — customer integrity in the UI: a customer with history
 * is never offered a doomed delete (pre-check + 409 race → "Deactiveren"), a stale save shows
 * the server message with "Gegevens herladen" (never a silent retry), a blank payment term
 * means "billing entity's term", and a duplicate VAT number is a warning, not a block.
 */

vi.mock('../../../auth/authContextValue', () => ({
  useAuth: () => ({
    hasPermission: (code: string) =>
      ['customers.view', 'customers.edit', 'customers.delete', 'customers.deactivate', 'customers.manage_fiscal'].includes(code),
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
vi.mock('../../components/CustomerUnitPricingPanel', () => ({ CustomerUnitPricingPanel: () => <div /> }))
vi.mock('../../components/CustomerPriceAdjustmentsPanel', () => ({ CustomerPriceAdjustmentsPanel: () => <div /> }))
vi.mock('../../../tarification/components/CombinedDiscountsPanel', () => ({ CombinedDiscountsPanel: () => <div /> }))
vi.mock('../../components/CustomerUnitsPanel', () => ({ CustomerUnitsPanel: () => <div /> }))
vi.mock('../../components/CustomerBillingPanel', () => ({ CustomerBillingPanel: () => <div /> }))
vi.mock('../../components/CustomerHistoryPanel', () => ({ CustomerHistoryPanel: () => <div /> }))
vi.mock('../../components/CustomerCommunicationPanel', () => ({ CustomerCommunicationPanel: () => <div /> }))
vi.mock('../../components/CustomerAddressesPanel', () => ({ CustomerAddressesPanel: () => <div /> }))

function customer(overrides: Partial<CustomerDetail> = {}): CustomerDetail {
  return {
    id: 'c1', customerNumber: 'KL-1', name: 'Haven BV', legalName: null, vatNumber: null,
    categoryId: null, categoryName: null, email: null, phoneNumber: null, website: null,
    street: null, houseNumber: null, postalCode: null, city: null, countryCode: null,
    invoiceEmail: null, paymentTermDays: 30, defaultLanguageCode: null, notes: null,
    isActive: true, isBlocked: false, blockReason: null, nickname: null, companyNumber: null,
    currencyCode: 'EUR', iban: null, bic: null, bankName: null, bankAccountNumber: null,
    defaultLegalEntityId: null, contacts: [], version: 1,
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
  deleteCustomer: vi.fn(),
  getCustomerReferences: vi.fn(),
  setCustomerActive: vi.fn(),
  getCustomerIdentityMatches: vi.fn(),
}))
vi.mock('../../api/customersApi', () => ({
  getCustomer: (...a: unknown[]) => api.getCustomer(...a),
  updateCustomer: (...a: unknown[]) => api.updateCustomer(...a),
  deleteCustomer: (...a: unknown[]) => api.deleteCustomer(...a),
  getCustomerReferences: (...a: unknown[]) => api.getCustomerReferences(...a),
  setCustomerActive: (...a: unknown[]) => api.setCustomerActive(...a),
  getCustomerIdentityMatches: (...a: unknown[]) => api.getCustomerIdentityMatches(...a),
  setCustomerBlocked: vi.fn(),
  updateCustomerContact: vi.fn(),
  addCustomerContact: vi.fn(),
  removeCustomerContact: vi.fn(),
  changeCustomerNumber: vi.fn(),
  getCustomerFiscalWarnings: () => Promise.resolve([]),
  getVatTreatments: () => Promise.resolve([]),
  getPeppolSchemes: () => Promise.resolve([]),
}))

const STALE = 'Deze gegevens zijn intussen gewijzigd door een andere gebruiker. Herlaad de gegevens en voer je wijziging opnieuw uit.'

function renderPage() {
  const router = createMemoryRouter([{ path: '/customers/:id', element: <CustomerDetailPage /> }], { initialEntries: ['/customers/c1'] })
  return render(<RouterProvider router={router} />)
}

async function openEdit() {
  renderPage()
  await screen.findByRole('heading', { name: 'Haven BV' })
  await userEvent.click(screen.getByRole('button', { name: 'Bewerken' }))
  await screen.findByLabelText(/^Naam/)
}

const saveButton = () => screen.getAllByRole('button', { name: 'Opslaan' }).at(-1)!

beforeEach(() => {
  vi.clearAllMocks()
  api.getCustomer.mockResolvedValue(customer())
  api.updateCustomer.mockResolvedValue(customer())
  api.setCustomerActive.mockResolvedValue(undefined)
  api.getCustomerIdentityMatches.mockResolvedValue([])
})

describe('CustomerDetailPage — delete a customer with history', () => {
  it('pre-checks the references and offers "Deactiveren" instead of a delete confirm', async () => {
    api.getCustomerReferences.mockResolvedValue([
      { key: 'orders', count: 1, label: '1 opdracht' },
      { key: 'invoices', count: 1, label: '1 factuur' },
    ])
    renderPage()
    await screen.findByRole('heading', { name: 'Haven BV' })

    await userEvent.click(screen.getByRole('button', { name: 'Meer' }))
    await userEvent.click(screen.getByRole('menuitem', { name: 'Verwijderen' }))

    const dialog = await screen.findByRole('dialog', { name: 'Klant kan niet worden verwijderd' })
    expect(dialog).toHaveTextContent('Deze klant kan niet worden verwijderd omdat er historische gegevens aan gekoppeld zijn:')
    expect(within(dialog).getAllByRole('listitem').map((li) => li.textContent)).toEqual(['1 opdracht', '1 factuur'])
    expect(dialog).toHaveTextContent('Gebruik "Deactiveren"')
    expect(api.deleteCustomer).not.toHaveBeenCalled()

    await userEvent.click(within(dialog).getByRole('button', { name: 'Deactiveren' }))
    await waitFor(() => expect(api.setCustomerActive).toHaveBeenCalledWith('c1', false))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  })

  it('a 409 from the delete itself (history linked meanwhile) lands in the same dialog', async () => {
    api.getCustomerReferences.mockResolvedValue([])
    api.deleteCustomer.mockRejectedValue(
      new ApiError('Deze klant kan niet worden verwijderd …', 409, {
        message: 'Deze klant kan niet worden verwijderd …',
        code: 'common.record_referenced',
        references: [{ key: 'orders', count: 2, label: '2 opdrachten' }],
        canDeactivate: true,
      }),
    )
    renderPage()
    await screen.findByRole('heading', { name: 'Haven BV' })

    await userEvent.click(screen.getByRole('button', { name: 'Meer' }))
    await userEvent.click(screen.getByRole('menuitem', { name: 'Verwijderen' }))
    const confirm = await screen.findByRole('dialog', { name: 'Klant verwijderen' })
    await userEvent.click(within(confirm).getByRole('button', { name: 'Verwijderen' }))

    const dialog = await screen.findByRole('dialog', { name: 'Klant kan niet worden verwijderd' })
    expect(within(dialog).getByRole('listitem')).toHaveTextContent('2 opdrachten')
    expect(within(dialog).getByRole('button', { name: 'Deactiveren' })).toBeInTheDocument()
  })
})

describe('CustomerDetailPage — stale save (optimistic concurrency)', () => {
  it('sends the loaded version, shows the server message and reloads the form on "Gegevens herladen"', async () => {
    api.getCustomer
      .mockResolvedValueOnce(customer())
      .mockResolvedValueOnce(customer({ name: 'Haven BV (collega)', version: 2 }))
    api.updateCustomer.mockRejectedValueOnce(new ApiError(STALE, 409, { message: STALE, code: 'common.concurrency_conflict' }))
    await openEdit()

    const name = screen.getByLabelText(/^Naam/)
    await userEvent.clear(name)
    await userEvent.type(name, 'Haven Antwerpen BV')
    await userEvent.click(saveButton())

    await waitFor(() => expect(api.updateCustomer).toHaveBeenCalledTimes(1))
    expect(api.updateCustomer).toHaveBeenCalledWith('c1', expect.objectContaining({ name: 'Haven Antwerpen BV', expectedVersion: 1 }))
    expect(await screen.findByText(STALE)).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Gegevens herladen' }))
    await waitFor(() => expect(screen.getByLabelText(/^Naam/)).toHaveValue('Haven BV (collega)'))
    expect(screen.queryByText(STALE)).not.toBeInTheDocument()
    // Never retried on its own.
    expect(api.updateCustomer).toHaveBeenCalledTimes(1)

    await userEvent.click(saveButton())
    await waitFor(() => expect(api.updateCustomer).toHaveBeenCalledTimes(2))
    expect(api.updateCustomer).toHaveBeenLastCalledWith('c1', expect.objectContaining({ expectedVersion: 2 }))
  })
})

describe('CustomerForm — payment term and duplicate identity', () => {
  it('shows a stored 0 as an empty field with the fallback hint and saves blank as null', async () => {
    api.getCustomer.mockResolvedValue(customer({ paymentTermDays: 0 }))
    await openEdit()
    await userEvent.click(screen.getByRole('tab', { name: /Facturatie/ }))

    const term = screen.getByLabelText(/Betaaltermijn/)
    expect(term).toHaveValue(null)
    expect(screen.getByText('Leeg = betaaltermijn van de facturerende entiteit')).toBeInTheDocument()

    await userEvent.click(saveButton())
    await waitFor(() => expect(api.updateCustomer).toHaveBeenCalledWith('c1', expect.objectContaining({ paymentTermDays: null })))
  })

  it('refuses a negative payment term client-side', async () => {
    await openEdit()
    await userEvent.click(screen.getByRole('tab', { name: /Facturatie/ }))
    const term = screen.getByLabelText(/Betaaltermijn/)
    await userEvent.clear(term)
    await userEvent.type(term, '-5')
    await userEvent.click(saveButton())

    expect((await screen.findAllByText(/0 of meer hele dagen/)).length).toBeGreaterThan(0)
    expect(api.updateCustomer).not.toHaveBeenCalled()
  })

  it('warns (without blocking) when the VAT number is already used by another customer', async () => {
    api.getCustomerIdentityMatches.mockResolvedValue([
      { id: 'c2', customerNumber: 'KL-0007', name: 'Haven Logistics', isActive: true, matchedOn: 'Vat' },
    ])
    await openEdit()
    await userEvent.click(screen.getByRole('tab', { name: /Fiscaal/ }))
    await userEvent.type(screen.getByLabelText(/BTW-nummer/), 'BE0403123486')
    await userEvent.tab()

    expect(await screen.findByText('Let op: dit btw-nummer wordt al gebruikt door Haven Logistics (KL-0007).')).toBeInTheDocument()
    expect(api.getCustomerIdentityMatches).toHaveBeenCalledWith({ vatNumber: 'BE0403123486', companyNumber: null, excludeId: 'c1' })

    await userEvent.click(saveButton())
    await waitFor(() => expect(api.updateCustomer).toHaveBeenCalledWith('c1', expect.objectContaining({ vatNumber: 'BE0403123486' })))
  })
})
