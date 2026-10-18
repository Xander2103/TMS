import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { CustomerDetailPage } from '../CustomerDetailPage'
import type { CustomerDetail } from '../../types'

/**
 * B17 — customer overview: read-first cards, open dossiers and recent invoices only for users
 * who may see them (and no request otherwise), one navigation layer in ?tab=, rare and
 * destructive actions behind "Meer".
 */

const perms = vi.hoisted(() => ({ value: [] as string[] }))
vi.mock('../../../auth/authContextValue', () => ({
  useAuth: () => ({
    hasPermission: (code: string) => perms.value.includes(code),
    hasAnyPermission: (codes: string[]) => codes.some((code) => perms.value.includes(code)),
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
// The tariff panels have their own tests; here only their placement matters.
vi.mock('../../components/CustomerUnitPricingPanel', () => ({ CustomerUnitPricingPanel: () => <div>prijzen-paneel</div> }))
vi.mock('../../components/CustomerPriceAdjustmentsPanel', () => ({ CustomerPriceAdjustmentsPanel: () => <div>aanpassingen-paneel</div> }))
vi.mock('../../../tarification/components/CombinedDiscountsPanel', () => ({ CombinedDiscountsPanel: () => <div>kortingen-paneel</div> }))
vi.mock('../../components/CustomerUnitsPanel', () => ({ CustomerUnitsPanel: () => <div>eenheden-paneel</div> }))
vi.mock('../../components/CustomerBillingPanel', () => ({ CustomerBillingPanel: () => <div>facturatie-paneel</div> }))
vi.mock('../../components/CustomerHistoryPanel', () => ({ CustomerHistoryPanel: () => <div>historiek-paneel</div> }))

const lists = vi.hoisted(() => ({ dossiers: vi.fn(), invoices: vi.fn() }))
vi.mock('../../../dossiers/api/dossiersApi', () => ({ searchDossiers: (...a: unknown[]) => lists.dossiers(...a) }))
vi.mock('../../../invoices/api/invoicesApi', () => ({ searchInvoices: (...a: unknown[]) => lists.invoices(...a) }))

const current = vi.hoisted(() => ({ value: null as CustomerDetail | null }))
vi.mock('../../api/customersApi', () => ({
  getCustomer: () => Promise.resolve(current.value),
  changeCustomerNumber: vi.fn(),
  getCustomerFiscalWarnings: () => Promise.resolve([]),
  getVatTreatments: () => Promise.resolve([]),
  getPeppolSchemes: () => Promise.resolve([]),
}))

function customer(overrides: Partial<CustomerDetail> = {}): CustomerDetail {
  return {
    id: 'c1', customerNumber: 'KL-1', name: 'Haven BV', legalName: null, vatNumber: 'BE0403123486',
    categoryId: null, categoryName: null, email: 'info@haven.example', phoneNumber: null, website: null,
    street: 'Noorderlaan', houseNumber: '147', postalCode: '2030', city: 'Antwerpen', countryCode: 'BE',
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

function renderPage(url = '/customers/c1') {
  const router = createMemoryRouter([{ path: '/customers/:id', element: <CustomerDetailPage /> }], { initialEntries: [url] })
  render(<RouterProvider router={router} />)
  return router
}

beforeEach(() => {
  vi.clearAllMocks()
  current.value = customer()
  perms.value = ['customers.view', 'customers.edit']
  lists.dossiers.mockResolvedValue({
    items: [{ id: 'd1', dossierNumber: 'DOS-0021', status: 'Open', dossierDate: '2026-10-15', createdAt: '2026-10-14T10:00:00Z', firstLoadingCity: 'Antwerpen', lastUnloadingCity: 'Luik', customerReference: null }],
    totalCount: 7, page: 1, pageSize: 5,
  })
  lists.invoices.mockResolvedValue({
    items: [{ id: 'i1', invoiceNumber: 'F-2026-0012', invoiceDate: '2026-10-01', status: 'Sent', total: 1234.5, currency: 'EUR' }],
    totalCount: 1, page: 1, pageSize: 5,
  })
})

describe('CustomerDetailPage — B17 overview', () => {
  it('shows identity and invoicing as read-only cards, with the name as the page title', async () => {
    renderPage()
    expect(await screen.findByRole('heading', { level: 2, name: 'Haven BV' })).toBeInTheDocument()
    const identity = screen.getByRole('region', { name: 'Klantgegevens' })
    expect(within(identity).getByText('Noorderlaan 147, 2030 Antwerpen (BE)')).toBeInTheDocument()
    expect(within(identity).queryByRole('textbox')).not.toBeInTheDocument()
    expect(screen.getByRole('region', { name: 'Facturatie & fiscaal' })).toHaveTextContent('BE0403123486')
  })

  it('without dossier or invoice rights shows neither card and requests neither list', async () => {
    renderPage()
    await screen.findByRole('heading', { level: 2, name: 'Haven BV' })
    expect(screen.queryByRole('region', { name: /Open dossiers/ })).not.toBeInTheDocument()
    expect(screen.queryByRole('region', { name: /Recente facturen/ })).not.toBeInTheDocument()
    expect(lists.dossiers).not.toHaveBeenCalled()
    expect(lists.invoices).not.toHaveBeenCalled()
  })

  it('with dossier rights only: a small page of open dossiers, never invoices', async () => {
    perms.value = ['customers.view', 'dossiers.view']
    renderPage()
    const card = await screen.findByRole('region', { name: /Open dossiers/ })
    expect(await within(card).findByRole('link', { name: 'DOS-0021' })).toHaveAttribute('href', '/dossiers/d1')
    expect(card).toHaveTextContent('7')
    expect(card).toHaveTextContent('Antwerpen → Luik')
    expect(within(card).getByRole('link', { name: /Alle open dossiers/ })).toHaveAttribute('href', '/dossiers?status=Open&customerId=c1')
    expect(lists.dossiers).toHaveBeenCalledWith(expect.objectContaining({ customerId: 'c1', status: 'Open', pageSize: 5 }))
    expect(lists.invoices).not.toHaveBeenCalled()
    expect(screen.queryByRole('region', { name: /Recente facturen/ })).not.toBeInTheDocument()
  })

  it('with invoice rights: the latest invoices read-only, linked', async () => {
    perms.value = ['customers.view', 'invoices.view']
    renderPage()
    const card = await screen.findByRole('region', { name: /Recente facturen/ })
    expect(await within(card).findByRole('link', { name: 'F-2026-0012' })).toHaveAttribute('href', '/invoices/i1')
    expect(lists.invoices).toHaveBeenCalledWith(expect.objectContaining({ customerId: 'c1', pageSize: 5, sort: 'date', dir: 'desc' }))
    expect(within(card).queryByRole('button')).not.toBeInTheDocument()
  })

  it('keeps destructive actions behind Meer and only offers what the user may do', async () => {
    perms.value = ['customers.view', 'customers.edit']
    renderPage()
    await screen.findByRole('heading', { level: 2, name: 'Haven BV' })
    await userEvent.click(screen.getByRole('button', { name: 'Meer' }))
    expect(screen.getAllByRole('menuitem').map((i) => i.textContent)).toEqual(['Blokkeren'])
  })

  it('offers delete and deactivate in the menu only with those rights', async () => {
    perms.value = ['customers.view', 'customers.edit', 'customers.delete', 'customers.deactivate', 'customers.override_number']
    renderPage()
    await screen.findByRole('heading', { level: 2, name: 'Haven BV' })
    expect(screen.queryByRole('button', { name: 'Verwijderen' })).not.toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Meer' }))
    expect(screen.getAllByRole('menuitem').map((i) => i.textContent)).toEqual(['Nummer wijzigen', 'Blokkeren', 'Deactiveren', 'Verwijderen'])
  })

  it('shows a block as critical attention with the reason', async () => {
    current.value = customer({ isBlocked: true, blockReason: 'Openstaande facturen' })
    renderPage()
    const strip = await screen.findByRole('region', { name: 'Aandacht' })
    expect(strip).toHaveClass('is-critical')
    expect(strip).toHaveTextContent('Klant geblokkeerd')
    expect(strip).toHaveTextContent('Openstaande facturen')
  })

  it('hides the tariffs section without customer view rights and the edit buttons without edit rights', async () => {
    perms.value = []
    renderPage()
    await screen.findByRole('heading', { level: 2, name: 'Haven BV' })
    expect(screen.queryByRole('tab', { name: /Tarieven/ })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /bewerken/i })).not.toBeInTheDocument()
  })

  it('opens the edit form on the section of the card that was edited', async () => {
    renderPage()
    await screen.findByRole('heading', { level: 2, name: 'Haven BV' })
    await userEvent.click(screen.getByRole('button', { name: 'Facturatie & fiscaal bewerken' }))
    expect(await screen.findByRole('tab', { name: /Fiscaal & Peppol/ })).toHaveAttribute('aria-selected', 'true')
  })
})

describe('CustomerDetailPage — B17 section navigation', () => {
  it('keeps the section in ?tab= and redirects the former tab ids', async () => {
    perms.value = ['customers.view', 'tariffs.view']
    const router = renderPage('/customers/c1?tab=billing')
    expect(await screen.findByRole('tab', { name: /Tarieven/ })).toHaveAttribute('aria-selected', 'true')
    await waitFor(() => expect(router.state.location.search).toBe('?tab=tarieven'))
    expect(screen.getByText('prijzen-paneel')).toBeInTheDocument()
    await userEvent.click(screen.getByRole('tab', { name: /Historiek/ }))
    expect(router.state.location.search).toBe('?tab=historiek')
  })

  it('moves "Documenten per dag" off the overview into its own section for order users', async () => {
    perms.value = ['customers.view', 'orders.view']
    renderPage()
    await screen.findByRole('heading', { level: 2, name: 'Haven BV' })
    expect(screen.queryByRole('heading', { name: 'Documenten per dag' })).not.toBeInTheDocument()
    await userEvent.click(screen.getByRole('tab', { name: 'Documenten per dag' }))
    expect(screen.getByRole('heading', { name: 'Documenten per dag' })).toBeInTheDocument()
  })
})
