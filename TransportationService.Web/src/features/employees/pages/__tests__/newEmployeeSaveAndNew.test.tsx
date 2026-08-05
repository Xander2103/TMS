import { describe, expect, it, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { NewEmployeePage } from '../NewEmployeePage'

// Fase 8 (master-data wave): de aanmaakpagina deelt het sectie-id "chauffeursgegevens" met de
// detailpagina, een minimale aanmaak (enkel naam) gaat zonder chauffeursvelden naar de API, en
// "Opslaan en nieuwe werknemer" reset de volledige pagina zonder te navigeren.

const nav = vi.hoisted(() => ({ spy: vi.fn() }))
const mutation = vi.hoisted(() => ({ create: vi.fn() }))
const toast = vi.hoisted(() => ({ showSuccess: vi.fn(), showError: vi.fn() }))

vi.mock('react-router-dom', async (orig) => ({
  ...(await orig<typeof import('react-router-dom')>()),
  useNavigate: () => nav.spy,
}))
vi.mock('../../../auth/authContextValue', () => ({
  useAuth: () => ({
    status: 'authenticated' as const,
    user: null,
    login: vi.fn(),
    logout: vi.fn(),
    hasPermission: (code: string) => ['drivers.create'].includes(code),
    hasAnyPermission: () => true,
  }),
}))
vi.mock('../../hooks/useEmployeeMutations', () => ({
  useEmployeeMutations: () => ({
    create: mutation.create,
    update: vi.fn(),
    deactivate: vi.fn(),
    reactivate: vi.fn(),
    isSubmitting: false,
    error: null,
    fieldErrors: {},
  }),
}))
vi.mock('../../hooks/useQualificationTypes', () => ({
  useQualificationTypes: () => ({ qualificationTypes: [], isLoading: false, error: null }),
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
vi.mock('../../../issued-items/issuedItemsApi', () => ({
  listIssuedItemTemplates: () => Promise.resolve([]),
}))
vi.mock('../../../../components/ui/toastContext', () => ({
  useToast: () => toast,
}))

beforeEach(() => {
  nav.spy.mockReset()
  mutation.create.mockReset()
  toast.showSuccess.mockReset()
  toast.showError.mockReset()
})

function renderPage() {
  return render(
    <MemoryRouter>
      <NewEmployeePage />
    </MemoryRouter>,
  )
}

async function fillNames(firstName = 'Jan', lastName = 'Peeters') {
  await userEvent.type(screen.getByLabelText(/Voornaam/i), firstName)
  await userEvent.type(screen.getByLabelText(/Achternaam/i), lastName)
}

describe('NewEmployeePage — chauffeurssectie', () => {
  it('gebruikt hetzelfde sectie-id/label "Chauffeursgegevens" als de detailpagina', async () => {
    renderPage()
    expect(screen.queryByRole('tab', { name: /Chauffeursprofiel/i })).not.toBeInTheDocument()
    await userEvent.click(screen.getByRole('tab', { name: /Chauffeursgegevens/i }))
    expect(screen.getByLabelText(/Deze medewerker is chauffeur/i)).toBeInTheDocument()
  })

  it('toont zonder aangevinkte chauffeurscheckbox geen chauffeursvelden en blokkeert de aanmaak niet', async () => {
    mutation.create.mockResolvedValue({ id: 'emp-1', employeeNumber: 'M001', driverId: null })
    renderPage()

    await userEvent.click(screen.getByRole('tab', { name: /Chauffeursgegevens/i }))
    expect(screen.getByLabelText(/Deze medewerker is chauffeur/i)).not.toBeChecked()
    expect(screen.queryByText(/Chauffeurcategorieën/i)).not.toBeInTheDocument()
    expect(screen.queryByLabelText(/Chauffeursnotities/i)).not.toBeInTheDocument()

    await userEvent.click(screen.getByRole('tab', { name: /Algemeen/i }))
    await fillNames()
    await userEvent.click(screen.getAllByRole('button', { name: 'Opslaan' })[0])

    await waitFor(() => expect(mutation.create).toHaveBeenCalledTimes(1))
    expect(mutation.create.mock.calls[0][0]).toMatchObject({
      firstName: 'Jan',
      lastName: 'Peeters',
      email: null,
      phoneNumber: null,
      dateOfBirth: null,
      employmentStartDate: null,
      street: null,
      houseNumber: null,
      postalCode: null,
      city: null,
      driverProfile: null,
    })
    await waitFor(() => expect(nav.spy).toHaveBeenCalledWith('/employees/emp-1'))
  })
})

describe('NewEmployeePage — Opslaan en nieuwe werknemer', () => {
  it('reset het formulier voor een volgende invoer en blijft op de pagina', async () => {
    mutation.create.mockResolvedValue({ id: 'emp-2', employeeNumber: 'M002', driverId: null })
    renderPage()

    await fillNames('An', 'Willems')
    await userEvent.click(screen.getAllByRole('button', { name: 'Opslaan en nieuwe werknemer' })[0])

    await waitFor(() => expect(mutation.create).toHaveBeenCalledTimes(1))
    expect(mutation.create.mock.calls[0][0]).toMatchObject({ firstName: 'An', lastName: 'Willems' })
    expect(toast.showSuccess).toHaveBeenCalledWith('Medewerker M002 aangemaakt.')
    // Geen navigatie: de pagina blijft klaarstaan voor de volgende medewerker.
    expect(nav.spy).not.toHaveBeenCalled()
    // Het formulier is volledig leeg (remount via key-bump).
    await waitFor(() => expect(screen.getByLabelText(/Voornaam/i)).toHaveValue(''))
    expect(screen.getByLabelText(/Achternaam/i)).toHaveValue('')
  })

  it('gewoon "Opslaan" behoudt het navigeer-naar-detail-gedrag', async () => {
    mutation.create.mockResolvedValue({ id: 'emp-3', employeeNumber: 'M003', driverId: null })
    renderPage()

    await fillNames()
    await userEvent.click(screen.getAllByRole('button', { name: 'Opslaan' })[0])

    await waitFor(() => expect(nav.spy).toHaveBeenCalledWith('/employees/emp-3'))
  })
})
