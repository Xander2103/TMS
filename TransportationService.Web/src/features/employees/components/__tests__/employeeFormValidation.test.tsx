import { describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { EmployeeForm } from '../EmployeeForm'
import type { EmployeeDetail } from '../../types/employee'

// Fase 8 (master-data wave): alleen voor- en achternaam blokkeren nog; alle andere velden
// zijn optioneel, met formaatcontroles enkel wanneer er een waarde is ingevuld. Daarnaast:
// einddatum hoort bij Dienstverband en de bewaarbalk staat boven én onder het formulier.

const auth = vi.hoisted(() => ({ permissions: [] as string[] }))

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

function renderForm(props: Partial<Parameters<typeof EmployeeForm>[0]> = {}) {
  return render(
    <MemoryRouter>
      <EmployeeForm
        mode="create"
        isSubmitting={false}
        submitError={null}
        onSubmit={vi.fn()}
        onCancel={vi.fn()}
        {...props}
      />
    </MemoryRouter>,
  )
}

function clickSave() {
  // De bewaarbalk staat boven én onder het formulier; beide knoppen posten hetzelfde formulier.
  return userEvent.click(screen.getAllByRole('button', { name: 'Opslaan' })[0])
}

const EDIT_INITIAL: EmployeeDetail = {
  id: 'emp-1',
  employeeNumber: 'EMP-0001',
  firstName: 'Jan',
  lastName: 'Peeters',
  dateOfBirth: '1990-05-01',
  placeOfBirth: null,
  nationalityCode: null,
  preferredLanguageCode: null,
  email: 'jan@voorbeeld.be',
  phoneNumber: '011 23 45 67',
  mobilePhone: null,
  street: 'Dorpsstraat',
  houseNumber: '1',
  postalCode: '3500',
  city: 'Hasselt',
  countryCode: 'BE',
  emergencyContactName: null,
  emergencyContactPhone: null,
  employmentStartDate: '2020-01-01',
  employmentEndDate: null,
  employmentStatus: 'Active',
  departmentId: null,
  departmentName: null,
  contractTypeId: null,
  contractTypeName: null,
  jobFunctionIds: [],
  functionNames: [],
  isActive: true,
  notes: null,
  driverId: null,
  nationalRegisterNumber: null,
  iban: null,
  bic: null,
  civilStatus: null,
  dependentChildren: null,
  dimonaNumber: null,
  identityCardNumber: null,
  emergencyContacts: [],
}

describe('EmployeeForm — minimale aanmaak', () => {
  it('laat opslaan toe met enkel voornaam en achternaam; optionele velden gaan als null mee', async () => {
    auth.permissions = []
    const onSubmit = vi.fn()
    renderForm({ onSubmit })

    await userEvent.type(screen.getByLabelText(/Voornaam/i), 'Jan')
    await userEvent.type(screen.getByLabelText(/Achternaam/i), 'Peeters')
    await clickSave()

    expect(screen.queryByText('Voornaam is verplicht.')).not.toBeInTheDocument()
    expect(screen.queryByText('Achternaam is verplicht.')).not.toBeInTheDocument()
    expect(onSubmit).toHaveBeenCalledTimes(1)
    const [values] = onSubmit.mock.calls[0]
    expect(values).toMatchObject({
      firstName: 'Jan',
      lastName: 'Peeters',
      email: null,
      phoneNumber: null,
      dateOfBirth: null,
      employmentStartDate: null,
      employmentEndDate: null,
      street: null,
      houseNumber: null,
      postalCode: null,
      city: null,
    })
  })

  it('blokkeert nog steeds wanneer de naam ontbreekt', async () => {
    auth.permissions = []
    const onSubmit = vi.fn()
    renderForm({ onSubmit })
    await clickSave()
    expect(screen.getByText('Voornaam is verplicht.')).toBeInTheDocument()
    expect(screen.getByText('Achternaam is verplicht.')).toBeInTheDocument()
    expect(onSubmit).not.toHaveBeenCalled()
  })
})

describe('EmployeeForm — e-mailformaat', () => {
  it('toont enkel een formaatfout wanneer een ongeldig e-mailadres is ingevuld', async () => {
    auth.permissions = []
    const onSubmit = vi.fn()
    renderForm({ onSubmit })

    await userEvent.type(screen.getByLabelText(/Voornaam/i), 'Jan')
    await userEvent.type(screen.getByLabelText(/Achternaam/i), 'Peeters')
    await userEvent.type(screen.getByLabelText(/^E-mail$/i), 'geen-adres')
    await clickSave()

    expect(screen.getByText('Geef een geldig e-mailadres op.')).toBeInTheDocument()
    expect(onSubmit).not.toHaveBeenCalled()

    await userEvent.clear(screen.getByLabelText(/^E-mail$/i))
    await userEvent.type(screen.getByLabelText(/^E-mail$/i), 'jan@voorbeeld.be')
    await clickSave()
    expect(onSubmit).toHaveBeenCalledTimes(1)
    expect(onSubmit.mock.calls[0][0]).toMatchObject({ email: 'jan@voorbeeld.be' })
  })
})

describe('EmployeeForm — dienstverbanddatums', () => {
  it('toont de einddatum in de sectie Dienstverband en niet meer in HR', async () => {
    auth.permissions = []
    renderForm()
    await userEvent.click(screen.getByRole('tab', { name: /Dienstverband/i }))
    expect(screen.getByLabelText(/Einddatum tewerkstelling/i)).toBeInTheDocument()
    await userEvent.click(screen.getByRole('tab', { name: /^HR/i }))
    expect(screen.queryByLabelText(/Einddatum tewerkstelling/i)).not.toBeInTheDocument()
  })

  it('weigert een einddatum vóór de startdatum en routeert naar Dienstverband', async () => {
    auth.permissions = []
    const onSubmit = vi.fn()
    renderForm({ onSubmit })

    await userEvent.type(screen.getByLabelText(/Voornaam/i), 'Jan')
    await userEvent.type(screen.getByLabelText(/Achternaam/i), 'Peeters')
    await userEvent.click(screen.getByRole('tab', { name: /Dienstverband/i }))
    fireEvent.change(screen.getByLabelText(/Startdatum/i), { target: { value: '2026-02-01' } })
    fireEvent.change(screen.getByLabelText(/Einddatum tewerkstelling/i), { target: { value: '2026-01-01' } })

    // Vanuit een andere sectie opslaan: de fout moet terug naar Dienstverband leiden.
    await userEvent.click(screen.getByRole('tab', { name: /Algemeen/i }))
    await clickSave()

    const dienstverbandTab = screen.getByRole('tab', { name: /Dienstverband/i })
    expect(dienstverbandTab).toHaveAttribute('aria-selected', 'true')
    expect(dienstverbandTab).toHaveAttribute('data-has-error', 'true')
    expect(screen.getByText('De einddatum moet na de startdatum liggen.')).toBeInTheDocument()
    expect(onSubmit).not.toHaveBeenCalled()
  })
})

describe('EmployeeForm — bewaarbalk boven en onder', () => {
  it('toont de bewaarbalk bovenaan én onderaan en verbergt beide op paneelsecties (edit)', async () => {
    auth.permissions = []
    renderForm({
      mode: 'edit',
      initial: EDIT_INITIAL,
      extraSections: [
        { id: 'paneel', label: 'Paneelsectie', optional: true, panel: true, render: () => <div>Zelfbewarend paneel</div> },
      ],
    })

    // Gewone sectie: twee bewaarbalken (boven + onder), dus twee Opslaan-knoppen.
    expect(screen.getAllByRole('button', { name: 'Opslaan' })).toHaveLength(2)
    expect(screen.getAllByRole('button', { name: 'Annuleren' })).toHaveLength(2)
    // Edit-modus kent geen "Opslaan en nieuwe werknemer".
    expect(screen.queryByRole('button', { name: 'Opslaan en nieuwe werknemer' })).not.toBeInTheDocument()

    // Paneelsectie: beide balken verdwijnen (het paneel bewaart zichzelf).
    await userEvent.click(screen.getByRole('tab', { name: /Paneelsectie/i }))
    expect(screen.getByText('Zelfbewarend paneel')).toBeInTheDocument()
    expect(screen.queryAllByRole('button', { name: 'Opslaan' })).toHaveLength(0)
    expect(screen.queryAllByRole('button', { name: 'Annuleren' })).toHaveLength(0)
  })

  it('toont in create-modus ook "Opslaan en nieuwe werknemer" in beide balken', () => {
    auth.permissions = []
    renderForm()
    expect(screen.getAllByRole('button', { name: 'Opslaan en nieuwe werknemer' })).toHaveLength(2)
  })
})
