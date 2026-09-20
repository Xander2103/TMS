import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { EmployeeForm } from '../EmployeeForm'

// Werknemersformulier: info-tips bij "Opslaan en nieuwe medewerker" en DIMONA-nummer, en de
// afdeling/contracttype/functie-keuzelijsten als zoekbare comboboxen met — enkel mét
// beheerrecht — een "+ Nieuwe …"-snelkoppeling die inline aanmaakt zonder de formulierstaat
// te verliezen. LookupSelect wordt hier bewust NIET gemockt.

const auth = vi.hoisted(() => ({ permissions: [] as string[] }))
const lookups = vi.hoisted(() => ({
  byPath: {} as Record<string, { id: string; code: string; name: string; requiresEndDate?: boolean }[]>,
  refresh: vi.fn(async () => {}),
  create: vi.fn(),
}))

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
  useLookupOptions: (basePath: string) => ({
    options: lookups.byPath[basePath] ?? [],
    isLoading: false,
    error: null,
    refresh: lookups.refresh,
  }),
}))
vi.mock('../../../master-data/api/lookupApi', () => ({
  createLookupApi: () => ({ create: lookups.create }),
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
      <EmployeeForm mode="create" isSubmitting={false} submitError={null} onSubmit={vi.fn()} onCancel={vi.fn()} {...props} />
    </MemoryRouter>,
  )
}

beforeEach(() => {
  auth.permissions = []
  lookups.byPath = {
    '/api/departments': [{ id: 'dep-1', code: 'PLANNING', name: 'Planning' }],
    '/api/contract-types': [{ id: 'ct-1', code: 'ONBEP', name: 'Onbepaalde duur' }],
    '/api/job-functions': [
      { id: 'jf-1', code: 'CHAUF', name: 'Chauffeur' },
      { id: 'jf-2', code: 'PLAN', name: 'Planner' },
    ],
  }
  lookups.refresh.mockClear()
  lookups.create.mockReset()
})

async function openDienstverband() {
  await userEvent.click(screen.getByRole('tab', { name: /Dienstverband/i }))
}

describe('EmployeeForm — info-tips', () => {
  it('heet "Opslaan en nieuwe medewerker" en legt op hover/focus uit wat de knop doet', async () => {
    const user = userEvent.setup()
    renderForm()
    expect(screen.getAllByRole('button', { name: 'Opslaan en nieuwe medewerker' })).toHaveLength(2)
    expect(screen.queryByRole('button', { name: 'Opslaan en nieuwe werknemer' })).not.toBeInTheDocument()

    const tips = screen.getAllByRole('button', { name: 'Meer informatie' })
    expect(tips).toHaveLength(2) // één per bewaarbalk (boven + onder)
    expect(screen.queryByRole('tooltip')).not.toBeInTheDocument()

    await user.hover(tips[0])
    expect(screen.getByRole('tooltip')).toHaveTextContent(
      'Slaat deze medewerker op en opent daarna onmiddellijk een leeg formulier om een volgende medewerker toe te voegen.',
    )
    await user.unhover(tips[0])
    expect(screen.queryByRole('tooltip')).not.toBeInTheDocument()

    fireEvent.focus(tips[1])
    expect(screen.getByRole('tooltip')).toHaveTextContent(/Slaat deze medewerker op/)
    // Het gewone Opslaan heeft geen info-tip.
    const saveButtons = screen.getAllByRole('button', { name: 'Opslaan' })
    expect(saveButtons).toHaveLength(2)
  })

  it('toont bij DIMONA-nummer een info-tip met uitleg en een externe "Meer info"-link', async () => {
    const user = userEvent.setup()
    renderForm()
    await openDienstverband()

    // Het label blijft de enige toegankelijke naam van het invoerveld.
    const input = screen.getByLabelText('DIMONA-nummer')
    expect(input).toHaveAttribute('id', 'e-dimona')

    const tip = screen.getAllByRole('button', { name: 'Meer informatie' }).find((b) => b.closest('.ui-form-field'))!
    await user.hover(tip)
    const tooltip = screen.getByRole('tooltip')
    expect(tooltip).toHaveTextContent(
      'Een Dimonanummer is een uniek identificatienummer dat de RSZ (Rijksdienst voor de Sociale Zekerheid) toekent aan elke individuele Dimona-aangifte (in- of uitdiensttreding) van een werknemer.',
    )
    const link = within(tooltip).getByRole('link', { name: 'Meer info' })
    expect(link).toHaveAttribute(
      'href',
      'https://www.socialsecurity.be/employer/instructions/dmfa/nl/latest/instructions/obligations/obligations_nsso/dimona/general.html',
    )
    expect(link).toHaveAttribute('target', '_blank')
    expect(link).toHaveAttribute('rel', 'noopener noreferrer')
  })
})

describe('EmployeeForm — afdeling / contracttype / functie als zoekbare keuzelijsten', () => {
  it('zijn comboboxen met zoeken en tonen zonder beheerrecht geen "+ Nieuwe …"-rij', async () => {
    renderForm()
    await openDienstverband()

    const department = screen.getByRole('combobox', { name: 'Afdeling' })
    const contract = screen.getByRole('combobox', { name: 'Contracttype' })
    const fn = screen.getByRole('combobox', { name: 'Functies' })
    expect(department).toHaveAttribute('id', 'e-department')
    expect(contract).toHaveAttribute('id', 'e-contract')
    expect(fn).toHaveAttribute('id', 'e-function-add')

    await userEvent.click(department)
    expect(screen.getByRole('option', { name: 'Planning' })).toBeInTheDocument()
    expect(screen.queryByRole('option', { name: '+ Nieuwe afdeling' })).not.toBeInTheDocument()
    await userEvent.type(department, 'xyz')
    expect(screen.getByText('Geen resultaten')).toBeInTheDocument()
    await userEvent.keyboard('{Escape}')

    await userEvent.click(contract)
    expect(screen.getByRole('option', { name: 'Onbepaalde duur' })).toBeInTheDocument()
    expect(screen.queryByRole('option', { name: '+ Nieuw contracttype' })).not.toBeInTheDocument()
    await userEvent.keyboard('{Escape}')

    await userEvent.click(fn)
    await userEvent.type(fn, 'plan')
    // Scoped to the open listbox: native <select> <option>s elsewhere in the form also have role "option".
    expect(within(screen.getByRole('listbox')).getAllByRole('option')).toHaveLength(1)
    expect(screen.getByRole('option', { name: 'Planner' })).toBeInTheDocument()
  })

  it('toont de "+ Nieuwe …"-rijen enkel met het bijbehorende beheerrecht', async () => {
    auth.permissions = ['departments.manage', 'reference_data.manage', 'job_functions.manage']
    renderForm()
    await openDienstverband()

    await userEvent.click(screen.getByRole('combobox', { name: 'Afdeling' }))
    expect(screen.getByRole('option', { name: '+ Nieuwe afdeling' })).toBeInTheDocument()
    await userEvent.keyboard('{Escape}')

    await userEvent.click(screen.getByRole('combobox', { name: 'Contracttype' }))
    expect(screen.getByRole('option', { name: '+ Nieuw contracttype' })).toBeInTheDocument()
    await userEvent.keyboard('{Escape}')

    await userEvent.click(screen.getByRole('combobox', { name: 'Functies' }))
    expect(screen.getByRole('option', { name: '+ Nieuwe functie' })).toBeInTheDocument()
  })

  it('biedt al gekozen functies niet meer aan en verwijdert ze via de chip', async () => {
    renderForm()
    await openDienstverband()

    const fn = screen.getByRole('combobox', { name: 'Functies' })
    await userEvent.click(fn)
    await userEvent.click(screen.getByRole('option', { name: 'Chauffeur' }))
    expect(screen.getByRole('button', { name: 'Functie Chauffeur verwijderen' })).toBeInTheDocument()

    await userEvent.click(fn)
    expect(screen.queryByRole('option', { name: 'Chauffeur' })).not.toBeInTheDocument()
    expect(screen.getByRole('option', { name: 'Planner' })).toBeInTheDocument()
    await userEvent.keyboard('{Escape}')

    await userEvent.click(screen.getByRole('button', { name: 'Functie Chauffeur verwijderen' }))
    expect(screen.getByText('Nog geen functies gekozen.')).toBeInTheDocument()
  })
})

describe('EmployeeForm — functie inline aanmaken', () => {
  it('opent de dialoog, post code: null, voegt de nieuwe functie als chip toe en behoudt de andere velden', async () => {
    auth.permissions = ['job_functions.manage']
    lookups.create.mockResolvedValue({ id: 'jf-9', code: 'MAGAZIJNIE', name: 'Magazijnier', description: null, isActive: true, sortOrder: 0 })
    const onFunctionsChanged = vi.fn()
    const onSubmit = vi.fn()
    renderForm({ onFunctionsChanged, onSubmit })

    await userEvent.type(screen.getByLabelText(/Voornaam/i), 'An')
    await userEvent.type(screen.getByLabelText(/Achternaam/i), 'Willems')
    await openDienstverband()
    await userEvent.type(screen.getByLabelText('DIMONA-nummer'), 'DIM-123')
    await userEvent.click(screen.getByRole('combobox', { name: 'Afdeling' }))
    await userEvent.click(screen.getByRole('option', { name: 'Planning' }))

    const fn = screen.getByRole('combobox', { name: 'Functies' })
    await userEvent.click(fn)
    await userEvent.click(screen.getByRole('option', { name: 'Chauffeur' }))
    await userEvent.click(fn)
    await userEvent.click(screen.getByRole('option', { name: '+ Nieuwe functie' }))

    const dialog = screen.getByRole('dialog', { name: 'Nieuwe functie' })
    await userEvent.type(within(dialog).getByLabelText(/^Naam/), 'Magazijnier')
    expect(within(dialog).getByLabelText(/^Code/)).toHaveValue('')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Toevoegen en selecteren' }))

    await waitFor(() => expect(lookups.create).toHaveBeenCalledTimes(1))
    expect(lookups.create.mock.calls[0][0]).toMatchObject({ code: null, name: 'Magazijnier' })
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())

    // Nieuwe functie staat meteen als chip in het formulier, naast de eerder gekozen.
    await waitFor(() => expect(screen.getByRole('button', { name: 'Functie Magazijnier verwijderen' })).toBeInTheDocument())
    expect(screen.getByRole('button', { name: 'Functie Chauffeur verwijderen' })).toBeInTheDocument()
    expect(onFunctionsChanged).toHaveBeenLastCalledWith(['CHAUF', 'MAGAZIJNIE'])
    expect(lookups.refresh).toHaveBeenCalled()

    // De rest van het formulier is intact (geen navigatie, geen reset).
    expect(screen.getByLabelText('DIMONA-nummer')).toHaveValue('DIM-123')
    expect(screen.getByRole('combobox', { name: 'Afdeling' })).toHaveValue('Planning')
    await userEvent.click(screen.getByRole('tab', { name: /Algemeen/i }))
    expect(screen.getByLabelText(/Voornaam/i)).toHaveValue('An')
    expect(screen.getByLabelText(/Achternaam/i)).toHaveValue('Willems')

    await userEvent.click(screen.getAllByRole('button', { name: 'Opslaan' })[0])
    expect(onSubmit).toHaveBeenCalledTimes(1)
    expect(onSubmit.mock.calls[0][0]).toMatchObject({
      firstName: 'An',
      lastName: 'Willems',
      dimonaNumber: 'DIM-123',
      departmentId: 'dep-1',
      jobFunctionIds: ['jf-1', 'jf-9'],
    })
  })
})
