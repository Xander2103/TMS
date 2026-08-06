import { describe, expect, it, vi, beforeEach } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { NewEmployeePage } from '../NewEmployeePage'

// Task 10: a non-blocking hint appears when the (debounced) search for the entered first+last
// name turns up an existing employee with the exact same trimmed, case-insensitive name. It
// must never block submission — only warn.

const nav = vi.hoisted(() => ({ spy: vi.fn() }))
const searchMock = vi.hoisted(() => vi.fn())

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
    hasPermission: () => false,
    hasAnyPermission: () => false,
  }),
}))
vi.mock('../../hooks/useEmployeeMutations', () => ({
  useEmployeeMutations: () => ({
    create: vi.fn(),
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
  useToast: () => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }),
}))
vi.mock('../../api/employeesApi', () => ({
  searchEmployees: searchMock,
}))

beforeEach(() => {
  nav.spy.mockReset()
  searchMock.mockReset()
  searchMock.mockResolvedValue({ items: [], totalCount: 0, page: 1, pageSize: 25 })
})

function renderPage() {
  return render(
    <MemoryRouter>
      <NewEmployeePage />
    </MemoryRouter>,
  )
}

describe('NewEmployeePage — duplicaatwaarschuwing', () => {
  it('shows the hint after the debounce when an exact first+last name match already exists', async () => {
    searchMock.mockResolvedValue({
      items: [{ id: 'emp-1', firstName: 'Jan', lastName: 'Peeters' }],
      totalCount: 1,
      page: 1,
      pageSize: 25,
    })
    renderPage()

    fireEvent.change(screen.getByLabelText(/Voornaam/i), { target: { value: 'Jan' } })
    fireEvent.change(screen.getByLabelText(/Achternaam/i), { target: { value: 'Peeters' } })

    await waitFor(() => expect(searchMock).toHaveBeenCalled(), { timeout: 2000 })
    expect(searchMock).toHaveBeenCalledWith({ search: 'Peeters', page: 1, pageSize: 25 })
    expect(await screen.findByText('Er bestaat al een medewerker met deze naam.')).toBeInTheDocument()
  })

  it('does not show the hint when no exact match exists', async () => {
    searchMock.mockResolvedValue({
      items: [{ id: 'emp-1', firstName: 'Piet', lastName: 'Peeters' }],
      totalCount: 1,
      page: 1,
      pageSize: 25,
    })
    renderPage()

    fireEvent.change(screen.getByLabelText(/Voornaam/i), { target: { value: 'Jan' } })
    fireEvent.change(screen.getByLabelText(/Achternaam/i), { target: { value: 'Peeters' } })

    await waitFor(() => expect(searchMock).toHaveBeenCalled(), { timeout: 2000 })
    expect(screen.queryByText('Er bestaat al een medewerker met deze naam.')).not.toBeInTheDocument()
  })

  it('does not call the search API until both first and last name are filled in', async () => {
    renderPage()
    fireEvent.change(screen.getByLabelText(/Voornaam/i), { target: { value: 'Jan' } })
    await new Promise((resolve) => setTimeout(resolve, 500))
    expect(searchMock).not.toHaveBeenCalled()
  })

  it('never blocks submission even when a duplicate is found', async () => {
    searchMock.mockResolvedValue({
      items: [{ id: 'emp-1', firstName: 'Jan', lastName: 'Peeters' }],
      totalCount: 1,
      page: 1,
      pageSize: 25,
    })
    renderPage()

    fireEvent.change(screen.getByLabelText(/Voornaam/i), { target: { value: 'Jan' } })
    fireEvent.change(screen.getByLabelText(/Achternaam/i), { target: { value: 'Peeters' } })
    await screen.findByText('Er bestaat al een medewerker met deze naam.')

    expect(screen.getAllByRole('button', { name: 'Opslaan' })[0]).not.toBeDisabled()
  })
})
