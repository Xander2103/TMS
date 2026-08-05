import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import * as api from '../../api/customersApi'
import * as legalEntitiesApi from '../../../legal-entities/api/legalEntitiesApi'
import { CustomerForm } from '../CustomerForm'
import type { CustomerDetail, CustomerInput } from '../../types'

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

beforeEach(() => {
  auth.permissions = ['customers.manage_fiscal', 'customers.view']
  vi.spyOn(api, 'getVatTreatments').mockResolvedValue([])
  vi.spyOn(api, 'getPeppolSchemes').mockResolvedValue([
    { code: '0208', label: 'Belgisch ondernemingsnummer', countryCode: 'BE' },
    { code: '9925', label: 'Belgisch BTW-nummer', countryCode: 'BE' },
  ])
  vi.spyOn(legalEntitiesApi, 'getLegalEntityOptions').mockResolvedValue([])
})

function renderForm(props = {}) {
  return render(
    <MemoryRouter>
      <CustomerForm mode="create" isSubmitting={false} submitError={null} onSubmit={vi.fn()} onCancel={vi.fn()} {...props} />
    </MemoryRouter>,
  )
}

function customerDetail(): CustomerDetail {
  return {
    id: 'c1', customerNumber: 'KL-1', name: 'Haven BV', legalName: null, vatNumber: null,
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

function saveButton() {
  return screen.getAllByRole('button', { name: 'Opslaan' })[0]
}

describe('CustomerForm section structure (quick-entry consolidation)', () => {
  it('merges Algemeen/Contact/Contactpersonen/Adressen into one Klantgegevens section', () => {
    renderForm()
    expect(screen.getByRole('tab', { name: /Klantgegevens/i })).toBeInTheDocument()
    expect(screen.queryByRole('tab', { name: /^Algemeen$/i })).not.toBeInTheDocument()
    expect(screen.queryByRole('tab', { name: /^Contact$/i })).not.toBeInTheDocument()
    expect(screen.queryByRole('tab', { name: /Adressen/i })).not.toBeInTheDocument()
    expect(screen.queryByRole('tab', { name: /Notities/i })).not.toBeInTheDocument()

    // The four subsections of the merged Klantgegevens section, in order.
    expect(screen.getByText('Bedrijfsgegevens')).toBeInTheDocument()
    expect(screen.getByText('Algemene contactgegevens')).toBeInTheDocument()
    expect(screen.getByText('Contactpersonen')).toBeInTheDocument()
    expect(screen.getByText('Locaties & adressen')).toBeInTheDocument()
    expect(screen.getByText('Hoofdzetel / algemeen adres')).toBeInTheDocument()

    // Specialized sections stay.
    expect(screen.getByRole('tab', { name: /Fiscaal & Peppol/i })).toBeInTheDocument()
    expect(screen.getByRole('tab', { name: /Bank/i })).toBeInTheDocument()
    expect(screen.getByRole('tab', { name: /Facturatie/i })).toBeInTheDocument()
  })

  it('moves the internal memo under Algemene contactgegevens', () => {
    renderForm()
    expect(screen.getByLabelText(/Interne klantmemo/i)).toBeInTheDocument()
  })

  it('offers dual save bars plus "Opslaan en nieuwe klant" in create mode', () => {
    renderForm()
    expect(screen.getAllByRole('button', { name: 'Opslaan' })).toHaveLength(2)
    expect(screen.getAllByRole('button', { name: 'Opslaan en nieuwe klant' })).toHaveLength(2)
  })

  it('shows the Historiek panel section in edit mode without the save bar', async () => {
    render(
      <MemoryRouter>
        <CustomerForm
          mode="edit"
          initial={customerDetail()}
          isSubmitting={false}
          submitError={null}
          onSubmit={vi.fn()}
          onCancel={vi.fn()}
          editPanels={{ historiek: <div>HISTORIEK-PANEL</div> }}
        />
      </MemoryRouter>,
    )
    expect(screen.queryByRole('button', { name: 'Opslaan en nieuwe klant' })).not.toBeInTheDocument()
    await userEvent.click(screen.getByRole('tab', { name: /Historiek/i }))
    expect(screen.getByText('HISTORIEK-PANEL')).toBeInTheDocument()
    // Panel sections hide both save bars.
    expect(screen.queryByRole('button', { name: 'Opslaan' })).not.toBeInTheDocument()
  })
})

describe('CustomerForm section navigation', () => {
  it('preserves values when switching sections', async () => {
    renderForm()
    await userEvent.type(screen.getByRole('textbox', { name: 'Naam' }), 'Acme')
    await userEvent.click(screen.getByRole('tab', { name: /Bank/i }))
    expect(screen.queryByRole('textbox', { name: 'Naam' })).not.toBeInTheDocument()
    await userEvent.click(screen.getByRole('tab', { name: /Klantgegevens/i }))
    expect(screen.getByRole('textbox', { name: 'Naam' })).toHaveValue('Acme')
  })

  it('renders one combined Peppol-ID field with the advanced fields hidden', async () => {
    renderForm()
    await userEvent.click(screen.getByRole('tab', { name: /Fiscaal & Peppol/i }))
    expect(screen.getByRole('group', { name: 'Peppol' })).toBeInTheDocument()
    expect(screen.getByLabelText(/Peppol-ID/i)).toBeInTheDocument()
    expect(screen.queryByLabelText(/Schema/i)).not.toBeInTheDocument()
    expect(screen.queryByLabelText(/Participant-ID/i)).not.toBeInTheDocument()
  })

  it('blocks submit on an invalid combined Peppol-ID and routes to the fiscal section', async () => {
    const onSubmit = vi.fn()
    renderForm({ onSubmit })
    await userEvent.type(screen.getByRole('textbox', { name: 'Naam' }), 'Acme')
    await userEvent.click(screen.getByRole('tab', { name: /Fiscaal & Peppol/i }))
    await userEvent.type(screen.getByLabelText(/Peppol-ID/i), 'abcd:123')
    await userEvent.click(screen.getByRole('tab', { name: /Bank/i }))
    await userEvent.click(saveButton())
    expect(onSubmit).not.toHaveBeenCalled()
    const fiscaal = screen.getByRole('tab', { name: /Fiscaal & Peppol/i })
    expect(fiscaal).toHaveAttribute('aria-selected', 'true')
    expect(fiscaal).toHaveAttribute('data-has-error', 'true')
  })

  it('shows Bank section content immediately on tab click (no second accordion click)', async () => {
    renderForm()
    await userEvent.click(screen.getByRole('tab', { name: /Bank/i }))
    expect(screen.getByRole('textbox', { name: /IBAN/i })).toBeInTheDocument()
    expect(screen.getByRole('textbox', { name: /BIC/i })).toBeInTheDocument()
  })

  it('routes to Klantgegevens when a required field fails on submit', async () => {
    renderForm()
    await userEvent.click(screen.getByRole('tab', { name: /Bank/i }))
    await userEvent.click(saveButton())
    const klantgegevens = screen.getByRole('tab', { name: /Klantgegevens/i })
    expect(klantgegevens).toHaveAttribute('aria-selected', 'true')
    expect(klantgegevens).toHaveAttribute('data-has-error', 'true')
  })
})

describe('CustomerForm contacts repeater + payload', () => {
  it('submits a minimal create with empty contacts and no initialContact', async () => {
    const onSubmit = vi.fn()
    renderForm({ onSubmit })
    await userEvent.type(screen.getByRole('textbox', { name: 'Naam' }), 'Acme')
    await userEvent.click(saveButton())

    expect(onSubmit).toHaveBeenCalledTimes(1)
    const [values, intent] = onSubmit.mock.calls[0] as [CustomerInput, string]
    expect(intent).toBe('save')
    expect(values.name).toBe('Acme')
    expect(values.contacts).toEqual([])
    expect('initialContact' in values).toBe(false)
  })

  it('submits multiple contacts with types and per-type primaries', async () => {
    const onSubmit = vi.fn()
    renderForm({ onSubmit })
    await userEvent.type(screen.getByRole('textbox', { name: 'Naam' }), 'Acme')

    await userEvent.click(screen.getByRole('button', { name: '+ Contactpersoon toevoegen' }))
    await userEvent.click(screen.getByRole('button', { name: '+ Contactpersoon toevoegen' }))

    const firstNames = screen.getAllByLabelText(/Voornaam/)
    const lastNames = screen.getAllByLabelText(/Achternaam/)
    const types = screen.getAllByLabelText('Type')
    const primaries = screen.getAllByLabelText('Primair voor dit type')
    expect(firstNames).toHaveLength(3)

    await userEvent.type(firstNames[0], 'An')
    await userEvent.type(lastNames[0], 'Peeters')
    await userEvent.click(primaries[0]) // Algemeen, primair

    await userEvent.type(firstNames[1], 'Jan')
    await userEvent.type(lastNames[1], 'Claes')
    await userEvent.selectOptions(types[1], 'Facturatie')
    await userEvent.click(primaries[1]) // Facturatie, primair (ander type → toegestaan)

    await userEvent.type(firstNames[2], 'Els')
    await userEvent.type(lastNames[2], 'Maes')
    await userEvent.selectOptions(types[2], 'Planning')

    await userEvent.click(saveButton())

    expect(onSubmit).toHaveBeenCalledTimes(1)
    const [values] = onSubmit.mock.calls[0] as [CustomerInput]
    expect(values.contacts).toEqual([
      expect.objectContaining({ firstName: 'An', lastName: 'Peeters', contactType: 'Algemeen', isPrimary: true }),
      expect.objectContaining({ firstName: 'Jan', lastName: 'Claes', contactType: 'Facturatie', isPrimary: true }),
      expect.objectContaining({ firstName: 'Els', lastName: 'Maes', contactType: 'Planning', isPrimary: false }),
    ])
  })

  it('blocks a second primary of the same type with a Dutch message', async () => {
    const onSubmit = vi.fn()
    renderForm({ onSubmit })
    await userEvent.type(screen.getByRole('textbox', { name: 'Naam' }), 'Acme')

    await userEvent.click(screen.getByRole('button', { name: '+ Contactpersoon toevoegen' }))
    const firstNames = screen.getAllByLabelText(/Voornaam/)
    const lastNames = screen.getAllByLabelText(/Achternaam/)
    const primaries = screen.getAllByLabelText('Primair voor dit type')

    await userEvent.type(firstNames[0], 'An')
    await userEvent.type(lastNames[0], 'Peeters')
    await userEvent.click(primaries[0])
    await userEvent.type(firstNames[1], 'Jan')
    await userEvent.type(lastNames[1], 'Claes')
    await userEvent.click(primaries[1]) // zelfde type (Algemeen) → geblokkeerd

    await userEvent.click(saveButton())

    expect(onSubmit).not.toHaveBeenCalled()
    expect(screen.getByText('Er is al een primaire contactpersoon van dit type.')).toBeInTheDocument()
  })

  it('requires names on filled rows but drops fully empty rows', async () => {
    const onSubmit = vi.fn()
    renderForm({ onSubmit })
    await userEvent.type(screen.getByRole('textbox', { name: 'Naam' }), 'Acme')

    // Row stays fully empty → dropped, not validated: submit succeeds.
    await userEvent.click(saveButton())
    expect(onSubmit).toHaveBeenCalledTimes(1)
    onSubmit.mockClear()

    // Partially filled row → names required.
    await userEvent.type(screen.getByLabelText(/Voornaam/), 'An')
    await userEvent.click(saveButton())
    expect(onSubmit).not.toHaveBeenCalled()
    expect(screen.getByText('Achternaam van de contactpersoon is verplicht.')).toBeInTheDocument()
  })

  it('adding and removing rows preserves the other rows values', async () => {
    renderForm()
    await userEvent.type(screen.getAllByLabelText(/Voornaam/)[0], 'An')

    await userEvent.click(screen.getByRole('button', { name: '+ Contactpersoon toevoegen' }))
    const firstNames = screen.getAllByLabelText(/Voornaam/)
    expect(firstNames).toHaveLength(2)
    await userEvent.type(firstNames[1], 'Jan')
    expect(firstNames[0]).toHaveValue('An')

    await userEvent.click(screen.getByRole('button', { name: 'Contactpersoon 1 verwijderen' }))
    const remaining = screen.getAllByLabelText(/Voornaam/)
    expect(remaining).toHaveLength(1)
    expect(remaining[0]).toHaveValue('Jan')
  })

  it('passes the saveAndNew intent from "Opslaan en nieuwe klant"', async () => {
    const onSubmit = vi.fn()
    renderForm({ onSubmit })
    await userEvent.type(screen.getByRole('textbox', { name: 'Naam' }), 'Acme')
    await userEvent.click(screen.getAllByRole('button', { name: 'Opslaan en nieuwe klant' })[0])

    expect(onSubmit).toHaveBeenCalledTimes(1)
    expect(onSubmit.mock.calls[0][1]).toBe('saveAndNew')
  })
})
