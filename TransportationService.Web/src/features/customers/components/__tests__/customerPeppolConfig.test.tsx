import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import * as api from '../../api/customersApi'
import * as legalEntitiesApi from '../../../legal-entities/api/legalEntitiesApi'
import { CustomerForm } from '../CustomerForm'
import type { CustomerDetail } from '../../types'

const auth = vi.hoisted(() => ({ permissions: ['customers.manage_fiscal', 'customers.view'] }))

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

function customerDetail(overrides: Partial<CustomerDetail> = {}): CustomerDetail {
  return {
    id: 'c1',
    customerNumber: 'KL-0001',
    name: 'Acme NV',
    legalName: null,
    vatNumber: null,
    categoryId: null,
    categoryName: null,
    email: null,
    phoneNumber: null,
    website: null,
    street: null,
    houseNumber: null,
    postalCode: null,
    city: null,
    countryCode: null,
    invoiceEmail: null,
    paymentTermDays: 30,
    defaultLanguageCode: null,
    notes: null,
    isActive: true,
    isBlocked: false,
    blockReason: null,
    nickname: null,
    companyNumber: null,
    currencyCode: 'EUR',
    iban: null,
    bic: null,
    bankName: null,
    bankAccountNumber: null,
    defaultLegalEntityId: null,
    vatTreatment: 'DomesticVat',
    defaultVatRatePercent: null,
    vatCountryCode: null,
    vatNotes: null,
    peppolId: '0123456789',
    peppolScheme: '0208',
    peppolEnabled: false,
    peppolDeliveryPreference: 'Peppol',
    buyerReference: null,
    invoiceLanguageCode: null,
    purchaseOrderRequired: false,
    signedDeliveryNoteRequired: false,
    customerReferenceRequired: false,
    peppolValidationStatus: 'Unknown',
    peppolValidatedAt: null,
    peppolValidationReference: null,
    contacts: [],
    ...overrides,
  }
}

beforeEach(() => {
  auth.permissions = ['customers.manage_fiscal', 'customers.view']
  vi.spyOn(api, 'getVatTreatments').mockResolvedValue([])
  vi.spyOn(legalEntitiesApi, 'getLegalEntityOptions').mockResolvedValue([])
  vi.spyOn(api, 'getPeppolSchemes').mockResolvedValue([
    { code: '0208', label: 'Belgisch ondernemingsnummer', countryCode: 'BE' },
  ])
})

function renderForm(props = {}) {
  return render(
    <MemoryRouter>
      <CustomerForm mode="create" isSubmitting={false} submitError={null} onSubmit={vi.fn()} onCancel={vi.fn()} {...props} />
    </MemoryRouter>,
  )
}

async function openFiscaal() {
  await userEvent.click(screen.getByRole('tab', { name: /Fiscaal & Peppol/i }))
}

describe('Customer Peppol configuration (fiscaal section)', () => {
  it('renders the Peppol delivery fields and includes them in the submit payload', async () => {
    const onSubmit = vi.fn()
    renderForm({ onSubmit })
    await userEvent.type(screen.getByRole('textbox', { name: 'Naam' }), 'Acme')
    await openFiscaal()

    await userEvent.click(screen.getByRole('checkbox', { name: /Facturen via Peppol versturen/i }))
    await userEvent.selectOptions(screen.getByLabelText(/Bezorgvoorkeur/i), 'EmailFallback')
    await userEvent.type(screen.getByLabelText(/Kopersreferentie/i), 'Kostenplaats 42')

    await userEvent.click(screen.getByRole('button', { name: /Opslaan/i }))
    expect(onSubmit).toHaveBeenCalledWith(
      expect.objectContaining({
        peppolEnabled: true,
        peppolDeliveryPreference: 'EmailFallback',
        buyerReference: 'Kostenplaats 42',
      }),
    )
  })

  it('disables the delivery-preference select while Peppol is not enabled', async () => {
    renderForm()
    await openFiscaal()
    const select = screen.getByLabelText(/Bezorgvoorkeur/i)
    expect(select).toBeDisabled()
    await userEvent.click(screen.getByRole('checkbox', { name: /Facturen via Peppol versturen/i }))
    expect(select).toBeEnabled()
  })

  it('hides the verify button without the peppol.validate permission', async () => {
    renderForm({ mode: 'edit', initial: customerDetail() })
    await openFiscaal()
    expect(screen.queryByRole('button', { name: /Peppol-gegevens controleren/i })).not.toBeInTheDocument()
    // The persisted status is still shown.
    expect(screen.getByText(/Nog niet gecontroleerd/i)).toBeInTheDocument()
  })

  it('verifies at the provider and shows the found state', async () => {
    auth.permissions = ['customers.manage_fiscal', 'customers.view', 'peppol.validate']
    const verifySpy = vi.spyOn(api, 'verifyCustomerPeppol').mockResolvedValue({
      found: true,
      supportedDocumentTypes: ['Peppol BIS Billing 3.0 Invoice', 'Peppol BIS Billing 3.0 CreditNote'],
      lastCheckedAt: '2026-07-30T10:15:00Z',
      reference: 'sandbox-check-1',
    })
    renderForm({ mode: 'edit', initial: customerDetail() })
    await openFiscaal()

    await userEvent.click(screen.getByRole('button', { name: /Peppol-gegevens controleren/i }))
    expect(verifySpy).toHaveBeenCalledWith('c1')
    expect(await screen.findByText('Gevonden in het Peppol-netwerk')).toBeInTheDocument()
    expect(screen.getByText(/Peppol BIS Billing 3\.0 Invoice, Peppol BIS Billing 3\.0 CreditNote/)).toBeInTheDocument()
    expect(screen.getByText(/Laatst gecontroleerd:/)).toBeInTheDocument()
    expect(screen.getByText(/Referentie: sandbox-check-1/)).toBeInTheDocument()
  })

  it('shows the not-found state when the participant is unknown', async () => {
    auth.permissions = ['customers.manage_fiscal', 'customers.view', 'peppol.validate']
    vi.spyOn(api, 'verifyCustomerPeppol').mockResolvedValue({
      found: false,
      supportedDocumentTypes: [],
      lastCheckedAt: '2026-07-30T10:15:00Z',
      reference: null,
    })
    renderForm({ mode: 'edit', initial: customerDetail() })
    await openFiscaal()

    await userEvent.click(screen.getByRole('button', { name: /Peppol-gegevens controleren/i }))
    expect(await screen.findByText('Niet gevonden in het Peppol-netwerk')).toBeInTheDocument()
  })
})
