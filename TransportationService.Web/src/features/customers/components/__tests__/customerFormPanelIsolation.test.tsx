import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import * as api from '../../api/customersApi'
import * as legalEntitiesApi from '../../../legal-entities/api/legalEntitiesApi'
import { CustomerForm } from '../CustomerForm'
import { CustomerContactsPanel } from '../CustomerContactsPanel'
import type { CustomerContact, CustomerDetail } from '../../types'

/**
 * UX-sprint 2026-09-09 §1.1 root cause 1: the self-saving contacts panel (and its dialog)
 * render INSIDE the page-level <form onSubmit onChange>. React bubbles those synthetic events
 * through the component tree even across portals, so a tick in the dialog used to mark the
 * customer page dirty and the dialog's Opslaan also submitted the whole customer. The
 * SelfSavingPanel boundary must stop both.
 */

const auth = vi.hoisted(() => ({ permissions: ['customers.view', 'customers.manage_fiscal', 'customers.manage_communication'] }))
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
// Rendered as an inspectable marker so the page's dirty flag can be asserted.
vi.mock('../../../../components/ui/UnsavedChangesGuard', () => ({
  UnsavedChangesGuard: ({ when }: { when: boolean }) => <span data-testid="guard" data-when={String(when)} />,
}))
const toast = vi.hoisted(() => ({ showSuccess: vi.fn(), showError: vi.fn(), showToast: vi.fn() }))
vi.mock('../../../../components/ui/toastContext', () => ({
  useToast: () => toast,
}))

const notificationsApi = vi.hoisted(() => ({ options: vi.fn(), get: vi.fn(), set: vi.fn() }))
vi.mock('../../api/customerNotificationsApi', async (orig) => ({
  ...(await orig<typeof import('../../api/customerNotificationsApi')>()),
  getNotificationOptions: () => notificationsApi.options(),
  getContactNotifications: (...a: unknown[]) => notificationsApi.get(...a),
  setContactNotifications: (...a: unknown[]) => notificationsApi.set(...a),
}))

function contact(): CustomerContact {
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
  } as CustomerContact
}

function customerDetail(): CustomerDetail {
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
  }
}

beforeEach(() => {
  vi.clearAllMocks()
  vi.spyOn(api, 'getVatTreatments').mockResolvedValue([])
  vi.spyOn(api, 'getPeppolSchemes').mockResolvedValue([])
  vi.spyOn(legalEntitiesApi, 'getLegalEntityOptions').mockResolvedValue([])
  notificationsApi.options.mockResolvedValue([
    { key: 'planning', group: 'Transport' },
    { key: 'invoice', group: 'Facturatie' },
  ])
  notificationsApi.get.mockResolvedValue({ contactId: 'ct-1', optionKeys: [] })
  notificationsApi.set.mockResolvedValue({ contactId: 'ct-1', optionKeys: ['planning'] })
})

function renderEditForm() {
  const onSubmit = vi.fn()
  const onUpdate = vi.fn().mockResolvedValue(true)
  const onChanged = vi.fn()
  render(
    <MemoryRouter>
      <CustomerForm
        mode="edit"
        initial={customerDetail()}
        isSubmitting={false}
        submitError={null}
        onSubmit={onSubmit}
        onCancel={vi.fn()}
        editPanels={{
          contactpersonen: (
            <CustomerContactsPanel
              customerId="c1"
              contacts={[contact()]}
              isSubmitting={false}
              showTitle={false}
              onAdd={vi.fn()}
              onUpdate={onUpdate}
              onRemove={vi.fn()}
              onChanged={onChanged}
            />
          ),
        }}
      />
    </MemoryRouter>,
  )
  return { onSubmit, onUpdate, onChanged }
}

function guardWhen() {
  return screen.getByTestId('guard').getAttribute('data-when')
}

describe('CustomerForm — self-saving contacts panel is isolated from the page form', () => {
  it('hides the page-level save bars on the Contactpersonen section in edit mode', async () => {
    renderEditForm()
    expect(screen.getAllByRole('button', { name: 'Opslaan' }).length).toBeGreaterThan(0)
    await userEvent.click(screen.getByRole('tab', { name: /Contactpersonen/i }))
    expect(screen.queryByRole('button', { name: 'Opslaan' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Annuleren' })).not.toBeInTheDocument()
    // The section heading comes from the flat FormSection; the panel renders no second title.
    expect(screen.getAllByRole('heading', { name: 'Contactpersonen' })).toHaveLength(1)
    expect(screen.getByRole('button', { name: '+ Contact toevoegen' })).toBeInTheDocument()
  })

  it('a tick + Opslaan inside the contact dialog neither dirties nor submits the customer form', async () => {
    const { onSubmit, onUpdate, onChanged } = renderEditForm()
    await userEvent.click(screen.getByRole('tab', { name: /Contactpersonen/i }))
    expect(guardWhen()).toBe('false')

    await userEvent.click(screen.getByRole('button', { name: 'Bewerken' }))
    const dialog = await screen.findByRole('dialog')
    const planning = within(dialog).getByLabelText('Planning / levervenster')
    await waitFor(() => expect(planning).toBeEnabled())

    // Typing and ticking inside the dialog: the page's dirty flag must stay false.
    await userEvent.type(within(dialog).getByLabelText(/Voornaam/), 'ne')
    await userEvent.click(planning)
    expect(planning).toBeChecked()
    expect(guardWhen()).toBe('false')

    await userEvent.click(within(dialog).getByRole('button', { name: 'Opslaan' }))

    await waitFor(() => expect(onUpdate).toHaveBeenCalledTimes(1))
    await waitFor(() => expect(notificationsApi.set).toHaveBeenCalledWith('c1', 'ct-1', ['planning']))
    await waitFor(() => expect(onChanged).toHaveBeenCalledTimes(1))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())

    // The dialog's submit never reached the customer form.
    expect(onSubmit).not.toHaveBeenCalled()
    expect(guardWhen()).toBe('false')
    // Still in edit mode on the same section, without a page-level save bar.
    expect(screen.getByRole('tab', { name: /Contactpersonen/i })).toHaveAttribute('aria-selected', 'true')
    expect(screen.queryByRole('button', { name: 'Opslaan' })).not.toBeInTheDocument()
  })

  it('the type filter inside the panel does not dirty the page either', async () => {
    renderEditForm()
    await userEvent.click(screen.getByRole('tab', { name: /Contactpersonen/i }))
    await userEvent.selectOptions(screen.getByLabelText('Type'), 'Facturatie')
    expect(guardWhen()).toBe('false')
  })
})
