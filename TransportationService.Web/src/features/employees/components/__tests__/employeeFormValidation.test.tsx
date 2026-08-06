import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { EmployeeForm } from '../EmployeeForm'
import type { EmployeeDetail } from '../../types/employee'

// Fase 8 (master-data wave): alleen voor- en achternaam blokkeren nog; alle andere velden
// zijn optioneel, met formaatcontroles enkel wanneer er een waarde is ingevuld. Daarnaast:
// einddatum hoort bij Dienstverband en de bewaarbalk staat boven én onder het formulier.

const auth = vi.hoisted(() => ({ permissions: [] as string[] }))
// Per-basePath lookup options so contract-type tests can inject a `requiresEndDate` option
// without disturbing every other lookup (which stays empty, as before).
const lookups = vi.hoisted(() => ({ byPath: {} as Record<string, { id: string; code: string; name: string; requiresEndDate?: boolean }[]> }))

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
  useLookupOptions: (basePath: string) => ({ options: lookups.byPath[basePath] ?? [], isLoading: false, error: null }),
}))
// A functional stand-in (real select, wired to value/onChange) so the contract-type-change
// tests below can actually drive a selection instead of only seeding it via `initial`.
vi.mock('../../../master-data/components/LookupSelect', () => ({
  LookupSelect: ({
    id,
    value,
    onChange,
    basePath,
  }: {
    id?: string
    value?: string | null
    onChange?: (value: string | null) => void
    basePath?: string
  }) => (
    <select id={id} aria-label="lookup" value={value ?? ''} onChange={(e) => onChange?.(e.target.value || null)}>
      <option value="">—</option>
      {(lookups.byPath[basePath ?? ''] ?? []).map((o) => (
        <option key={o.id} value={o.id}>
          {o.name}
        </option>
      ))}
    </select>
  ),
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

beforeEach(() => {
  lookups.byPath = {}
})

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
  completeness: null,
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
  it('toont de einddatum in de sectie Dienstverband en niet meer in Identiteit & bank', async () => {
    auth.permissions = []
    renderForm()
    await userEvent.click(screen.getByRole('tab', { name: /Dienstverband/i }))
    expect(screen.getByLabelText(/Einddatum tewerkstelling/i)).toBeInTheDocument()
    await userEvent.click(screen.getByRole('tab', { name: /Identiteit/i }))
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

// Task 10 (dossier-UX): burgerlijke staat / kinderen ten laste → Algemeen, DIMONA → Dienstverband,
// "Identiteit & bank" keeps only identity + bank fields.
describe('EmployeeForm — herziene secties', () => {
  it('toont Burgerlijke staat en kinderen ten laste in Algemeen', async () => {
    auth.permissions = []
    renderForm()
    await userEvent.click(screen.getByRole('tab', { name: /Algemeen/i }))
    expect(screen.getByLabelText(/Burgerlijke staat/i)).toBeInTheDocument()
    expect(screen.getByLabelText(/Aantal kinderen ten laste/i)).toBeInTheDocument()
  })

  it('toont DIMONA-nummer in Dienstverband, niet meer in Algemeen', async () => {
    auth.permissions = []
    renderForm()
    await userEvent.click(screen.getByRole('tab', { name: /Dienstverband/i }))
    expect(screen.getByLabelText(/DIMONA-nummer/i)).toBeInTheDocument()
    await userEvent.click(screen.getByRole('tab', { name: /Algemeen/i }))
    expect(screen.queryByLabelText(/DIMONA-nummer/i)).not.toBeInTheDocument()
    expect(screen.queryByLabelText(/Burgerlijke staat/i)).toBeInTheDocument()
  })

  it('toont in Identiteit & bank enkel de vertrouwelijke velden, met permissie', async () => {
    auth.permissions = ['employees.view_confidential']
    renderForm()
    await userEvent.click(screen.getByRole('tab', { name: /Identiteit/i }))
    expect(screen.getByLabelText(/Rijksregisternummer/i)).toBeInTheDocument()
    expect(screen.getByLabelText(/IBAN/i)).toBeInTheDocument()
    expect(screen.queryByLabelText(/Burgerlijke staat/i)).not.toBeInTheDocument()
    expect(screen.queryByLabelText(/DIMONA-nummer/i)).not.toBeInTheDocument()
  })

  it('toont een placeholder in Identiteit & bank zonder de vertrouwelijke permissie', async () => {
    auth.permissions = []
    renderForm()
    await userEvent.click(screen.getByRole('tab', { name: /Identiteit/i }))
    expect(screen.getByText('Je hebt geen rechten om deze gegevens te bekijken.')).toBeInTheDocument()
  })
})

describe('EmployeeForm — contracttype-gedreven einddatum', () => {
  it('markeert de einddatum niet als verplicht zonder contracttype', async () => {
    auth.permissions = []
    const { container } = renderForm()
    await userEvent.click(screen.getByRole('tab', { name: /Dienstverband/i }))
    const label = container.querySelector('label[for="e-end"]')
    expect(label?.querySelector('.ui-form-field-required')).not.toBeInTheDocument()
    expect(screen.getByText('Leeg = onbepaalde duur.')).toBeInTheDocument()
  })

  it('markeert de einddatum als verplicht en blokkeert opslaan zonder waarde wanneer het contracttype in deze bewerking wijzigt naar requiresEndDate', async () => {
    // Zachte regel (spec §2.4): een bewerking die het contracttype ZELF wijzigt naar een
    // requiresEndDate-type blijft blokkeren zonder einddatum, ook in edit-modus.
    auth.permissions = []
    lookups.byPath['/api/contract-types'] = [{ id: 'ct-1', code: 'TIJDELIJK', name: 'Tijdelijk contract', requiresEndDate: true }]
    const onSubmit = vi.fn()
    const { container } = renderForm({
      mode: 'edit',
      initial: EDIT_INITIAL, // contractTypeId: null
      onSubmit,
    })
    await userEvent.click(screen.getByRole('tab', { name: /Dienstverband/i }))
    fireEvent.change(container.querySelector('#e-contract')!, { target: { value: 'ct-1' } })

    const label = container.querySelector('label[for="e-end"]')
    expect(label?.querySelector('.ui-form-field-required')).toBeInTheDocument()
    expect(screen.queryByText('Leeg = onbepaalde duur.')).not.toBeInTheDocument()

    await clickSave()
    expect(screen.getByText('Einddatum is verplicht voor dit contracttype.')).toBeInTheDocument()
    expect(onSubmit).not.toHaveBeenCalled()
  })

  it('toont een niet-blokkerende hint (geen required-markering) wanneer een bestaand contracttype ongewijzigd blijft zonder einddatum', async () => {
    // Zachte regel: een dossier dat al bestaat zonder einddatum (bv. na backfill van
    // RequiresEndDate op BEP/UITZ) mag verder bewerkt worden zolang het contracttype zelf
    // niet wijzigt — de completeness-kaart signaleert de ontbrekende einddatum, niet dit veld.
    auth.permissions = []
    lookups.byPath['/api/contract-types'] = [{ id: 'ct-1', code: 'TIJDELIJK', name: 'Tijdelijk contract', requiresEndDate: true }]
    const onSubmit = vi.fn()
    const { container } = renderForm({
      mode: 'edit',
      initial: { ...EDIT_INITIAL, contractTypeId: 'ct-1' },
      onSubmit,
    })
    await userEvent.click(screen.getByRole('tab', { name: /Dienstverband/i }))

    const label = container.querySelector('label[for="e-end"]')
    expect(label?.querySelector('.ui-form-field-required')).not.toBeInTheDocument()
    expect(screen.getByText('Einddatum ontbreekt voor dit contracttype.')).toBeInTheDocument()

    await userEvent.click(screen.getByRole('tab', { name: /Algemeen/i }))
    await userEvent.type(screen.getByLabelText(/^Telefoon$/i), '011 22 33 44')
    await clickSave()
    expect(onSubmit).toHaveBeenCalledTimes(1)
    expect(onSubmit.mock.calls[0][0]).toMatchObject({ employmentEndDate: null })
  })

  it('laat opslaan toe zodra de verplichte einddatum is ingevuld', async () => {
    auth.permissions = []
    lookups.byPath['/api/contract-types'] = [{ id: 'ct-1', code: 'TIJDELIJK', name: 'Tijdelijk contract', requiresEndDate: true }]
    const onSubmit = vi.fn()
    renderForm({
      mode: 'edit',
      initial: { ...EDIT_INITIAL, contractTypeId: 'ct-1' },
      onSubmit,
    })
    await userEvent.click(screen.getByRole('tab', { name: /Dienstverband/i }))
    fireEvent.change(screen.getByLabelText(/Einddatum tewerkstelling/i), { target: { value: '2026-12-31' } })
    await clickSave()
    expect(onSubmit).toHaveBeenCalledTimes(1)
    expect(onSubmit.mock.calls[0][0]).toMatchObject({ employmentEndDate: '2026-12-31' })
  })
})

describe('EmployeeForm — presetknoppen einddatum', () => {
  it('zet de einddatum op 1 maand na de startdatum minus 1 dag', async () => {
    auth.permissions = []
    renderForm({ mode: 'edit', initial: { ...EDIT_INITIAL, employmentStartDate: '2026-01-01' } })
    await userEvent.click(screen.getByRole('tab', { name: /Dienstverband/i }))
    await userEvent.click(screen.getByRole('button', { name: '1 m' }))
    expect(screen.getByLabelText(/Einddatum tewerkstelling/i)).toHaveValue('2026-01-31')
  })

  it('klemt 31 januari + 1 maand op het einde van februari (schrikkeljaar)', async () => {
    auth.permissions = []
    renderForm({ mode: 'edit', initial: { ...EDIT_INITIAL, employmentStartDate: '2028-01-31' } })
    await userEvent.click(screen.getByRole('tab', { name: /Dienstverband/i }))
    await userEvent.click(screen.getByRole('button', { name: '1 m' }))
    expect(screen.getByLabelText(/Einddatum tewerkstelling/i)).toHaveValue('2028-02-29')
  })

  it('12 m-preset zet de einddatum op het einde van het jaar bij een startdatum op 1 januari', async () => {
    auth.permissions = []
    renderForm({ mode: 'edit', initial: { ...EDIT_INITIAL, employmentStartDate: '2026-01-01' } })
    await userEvent.click(screen.getByRole('tab', { name: /Dienstverband/i }))
    await userEvent.click(screen.getByRole('button', { name: '12 m' }))
    expect(screen.getByLabelText(/Einddatum tewerkstelling/i)).toHaveValue('2026-12-31')
  })
})
