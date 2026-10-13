import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { Link, RouterProvider, createMemoryRouter } from 'react-router-dom'
import { EmployeeForm } from '../EmployeeForm'

// Regression guard for the section-switching bug: switching sections must be internal form
// navigation only — it must NOT trigger the route-leave blocker / unsaved-changes modal.
// The real UnsavedChangesGuard is deliberately NOT mocked here (it was in the other suites).

const auth = vi.hoisted(() => ({ permissions: ['employees.view_confidential'] }))

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

function renderInRouter() {
  const router = createMemoryRouter(
    [
      {
        path: '/employees/new',
        element: (
          <>
            <EmployeeForm mode="create" isSubmitting={false} submitError={null} onSubmit={vi.fn()} onCancel={vi.fn()} />
            <Link to="/elsewhere">Elders</Link>
          </>
        ),
      },
      { path: '/elsewhere', element: <div>Elders pagina</div> },
    ],
    { initialEntries: ['/employees/new'] },
  )
  render(<RouterProvider router={router} />)
  return router
}

const MODAL_TEXT = /wijzigingen die nog niet zijn opgeslagen/i

describe('employee section navigation — unsaved-changes regression', () => {
  it('switching sections on a dirty form does NOT open the unsaved-changes modal, keeps values, changes section, stays on route', async () => {
    const router = renderInRouter()
    // Make the form dirty.
    await userEvent.type(screen.getByLabelText(/Voornaam/i), 'Jan')

    // Switch section.
    await userEvent.click(screen.getByRole('tab', { name: /Noodcontacten/i }))

    // No modal, section changed, route unchanged.
    expect(screen.queryByText(MODAL_TEXT)).not.toBeInTheDocument()
    expect(screen.getByRole('tab', { name: /Noodcontacten/i })).toHaveAttribute('aria-selected', 'true')
    expect(router.state.location.pathname).toBe('/employees/new')

    // Value survives the switch.
    await userEvent.click(screen.getByRole('tab', { name: /Algemeen/i }))
    expect(screen.getByLabelText(/Voornaam/i)).toHaveValue('Jan')
  })

  it('actually leaving the page (a cross-route link) still opens the unsaved-changes modal', async () => {
    renderInRouter()
    await userEvent.type(screen.getByLabelText(/Voornaam/i), 'Jan')
    await userEvent.click(screen.getByRole('link', { name: 'Elders' }))
    expect(await screen.findByText(MODAL_TEXT)).toBeInTheDocument()
  })
})
