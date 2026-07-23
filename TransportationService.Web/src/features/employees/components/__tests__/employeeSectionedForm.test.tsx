import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { EmployeeForm } from '../EmployeeForm'

// Mutable permission set so each test controls what useAuth() reports.
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

// Lookups + network-backed field controls are stubbed: this suite exercises the section
// navigation, not the data sources those controls fetch.
vi.mock('../../../master-data/hooks/useLookupOptions', () => ({
  useLookupOptions: () => ({ options: [], isLoading: false, error: null }),
}))
vi.mock('../../../master-data/components/LookupSelect', () => ({
  LookupSelect: ({ id }: { id?: string }) => <input id={id} aria-label="lookup" />,
}))
vi.mock('../../../reference/components/CountryCombobox', () => ({
  CountryCombobox: ({ id }: { id?: string }) => <input id={id} aria-label="Land" />,
}))
// useBlocker needs a data router; the guard itself is not under test here.
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

describe('EmployeeForm section navigation', () => {
  it('shows one section at a time and preserves values across switches', async () => {
    auth.permissions = ['employees.view_confidential']
    renderForm()
    await userEvent.type(screen.getByLabelText(/Voornaam/i), 'Jan')
    await userEvent.click(screen.getByRole('tab', { name: /Dienstverband/i }))
    expect(screen.queryByLabelText(/Voornaam/i)).not.toBeInTheDocument()
    await userEvent.click(screen.getByRole('tab', { name: /Algemeen/i }))
    expect(screen.getByLabelText(/Voornaam/i)).toHaveValue('Jan')
  })

  it('opens the first section containing a validation error on failed submit', async () => {
    auth.permissions = ['employees.view_confidential']
    renderForm()
    await userEvent.click(screen.getByRole('tab', { name: /Noodcontacten/i }))
    await userEvent.click(screen.getByRole('button', { name: /Opslaan/i }))
    // required Voornaam is empty -> routes back to Algemeen
    const algemeen = screen.getByRole('tab', { name: /Algemeen/i })
    expect(algemeen).toHaveAttribute('aria-selected', 'true')
    expect(algemeen).toHaveAttribute('data-has-error', 'true')
  })

  it('does not mark optional sections as required', () => {
    auth.permissions = ['employees.view_confidential']
    renderForm()
    expect(screen.getByRole('tab', { name: /Noodcontacten/i })).not.toHaveAttribute('data-required')
  })

  it('hides confidential fields without the permission', async () => {
    auth.permissions = []
    renderForm()
    await userEvent.click(screen.getByRole('tab', { name: /^HR/i }))
    // HR section is active but the confidential NRN field is gated away.
    expect(screen.queryByLabelText(/Rijksregisternummer/i)).not.toBeInTheDocument()
  })

  it('shows confidential fields in the HR section with the permission', async () => {
    auth.permissions = ['employees.view_confidential']
    renderForm()
    await userEvent.click(screen.getByRole('tab', { name: /^HR/i }))
    expect(screen.getByLabelText(/Rijksregisternummer/i)).toBeInTheDocument()
  })
})
