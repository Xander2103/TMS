import { describe, expect, it, vi, beforeEach } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { NewEmployeePage } from '../NewEmployeePage'

const auth = vi.hoisted(() => ({
  permissions: ['employees.view_confidential', 'employee_documents.create', 'issued_items.manage'],
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
  useToast: () => ({ showSuccess: vi.fn(), showError: vi.fn() }),
}))

beforeEach(() => {
  auth.permissions = ['employees.view_confidential', 'employee_documents.create', 'issued_items.manage']
})

function renderPage() {
  return render(
    <MemoryRouter>
      <NewEmployeePage />
    </MemoryRouter>,
  )
}

describe('NewEmployeePage section expansion', () => {
  it('shows the Kwalificaties editor immediately on tab click (no second accordion click)', async () => {
    renderPage()
    await userEvent.click(screen.getByRole('tab', { name: /Kwalificaties/i }))
    expect(screen.getByRole('button', { name: /Kwalificatie toevoegen/i })).toBeInTheDocument()
  })
})

describe('NewEmployeePage prepared documents', () => {
  it('lets you prepare a document during creation and keeps it across section switches', async () => {
    const { container } = renderPage()

    // Go to the Documenten section — it must show a real editor, not a placeholder.
    await userEvent.click(screen.getByRole('tab', { name: /Documenten/i }))
    expect(screen.queryByText(/beschikbaar na het opslaan/i)).not.toBeInTheDocument()

    // Add a file to the prepared-documents editor.
    const fileInput = container.querySelector('input[type="file"][multiple]') as HTMLInputElement
    const file = new File(['data'], 'rijbewijs.pdf', { type: 'application/pdf' })
    fireEvent.change(fileInput, { target: { files: [file] } })
    expect(screen.getByText('rijbewijs.pdf')).toBeInTheDocument()

    // Switch away and back — the prepared document survives (state lives in the page).
    await userEvent.click(screen.getByRole('tab', { name: /Algemeen/i }))
    expect(screen.queryByText('rijbewijs.pdf')).not.toBeInTheDocument()
    await userEvent.click(screen.getByRole('tab', { name: /Documenten/i }))
    expect(screen.getByText('rijbewijs.pdf')).toBeInTheDocument()
  })
})
