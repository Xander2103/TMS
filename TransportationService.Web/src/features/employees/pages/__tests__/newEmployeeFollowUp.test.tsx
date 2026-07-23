import { describe, expect, it, vi, beforeEach } from 'vitest'
import { render, screen, fireEvent, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { NewEmployeePage } from '../NewEmployeePage'

const nav = vi.hoisted(() => ({ spy: vi.fn() }))
const mutation = vi.hoisted(() => ({ create: vi.fn() }))

vi.mock('react-router-dom', async (orig) => ({
  ...(await orig<typeof import('react-router-dom')>()),
  useNavigate: () => nav.spy,
}))
// Stub EmployeeForm: render the injected extra sections (so the prepared-documents editor is
// present) and a submit button that drives the create + follow-up orchestration.
vi.mock('../../components/EmployeeForm', () => ({
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  EmployeeForm: ({ extraSections, onSubmit }: any) => (
    <div>
      {/* eslint-disable-next-line @typescript-eslint/no-explicit-any */}
      {extraSections?.map((s: any) => <div key={s.id}>{s.render()}</div>)}
      <button onClick={() => onSubmit({ firstName: 'Jan', lastName: 'Piet' })}>DoSubmit</button>
    </div>
  ),
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
vi.mock('../../../auth/authContextValue', () => ({
  useAuth: () => ({
    status: 'authenticated' as const,
    user: null,
    login: vi.fn(),
    logout: vi.fn(),
    hasPermission: (code: string) =>
      ['employee_documents.create', 'issued_items.manage', 'drivers.create'].includes(code),
    hasAnyPermission: () => true,
  }),
}))
vi.mock('../../../master-data/hooks/useLookupOptions', () => ({
  useLookupOptions: () => ({ options: [], isLoading: false, error: null }),
}))
vi.mock('../../api/employeeDocumentsApi', async (orig) => ({
  ...(await orig<typeof import('../../api/employeeDocumentsApi')>()),
  uploadEmployeeDocument: vi.fn(() => Promise.resolve({})),
}))
vi.mock('../../../issued-items/issuedItemsApi', async (orig) => ({
  ...(await orig<typeof import('../../../issued-items/issuedItemsApi')>()),
  listIssuedItemTemplates: () => Promise.resolve([]),
  saveEmployeeIssuedItem: vi.fn(() => Promise.resolve({})),
}))
vi.mock('../../../../components/ui/toastContext', () => ({
  useToast: () => ({ showSuccess: vi.fn(), showError: vi.fn() }),
}))

import { uploadEmployeeDocument } from '../../api/employeeDocumentsApi'

function renderPage() {
  return render(
    <MemoryRouter>
      <NewEmployeePage />
    </MemoryRouter>,
  )
}

function addPreparedDoc(container: HTMLElement) {
  const fileInput = container.querySelector('input[type="file"][multiple]') as HTMLInputElement
  fireEvent.change(fileInput, { target: { files: [new File(['x'], 'doc.pdf', { type: 'application/pdf' })] } })
}

beforeEach(() => {
  nav.spy.mockReset()
  mutation.create.mockReset()
  vi.mocked(uploadEmployeeDocument).mockClear()
})

describe('NewEmployeePage create follow-up orchestration', () => {
  it('does NOT upload prepared documents when employee creation fails', async () => {
    mutation.create.mockResolvedValue(null) // creation failed
    const { container } = renderPage()
    addPreparedDoc(container)
    await userEvent.click(screen.getByRole('button', { name: 'DoSubmit' }))
    expect(mutation.create).toHaveBeenCalled()
    expect(uploadEmployeeDocument).not.toHaveBeenCalled()
    expect(nav.spy).not.toHaveBeenCalled()
  })

  it('uploads prepared documents against the new employee id after successful creation, then navigates', async () => {
    mutation.create.mockResolvedValue({ id: 'emp-9', employeeNumber: 'M009', driverId: null })
    const { container } = renderPage()
    addPreparedDoc(container)
    await userEvent.click(screen.getByRole('button', { name: 'DoSubmit' }))
    await waitFor(() => expect(uploadEmployeeDocument).toHaveBeenCalled())
    expect(vi.mocked(uploadEmployeeDocument).mock.calls[0][0]).toBe('emp-9')
    await waitFor(() => expect(nav.spy).toHaveBeenCalledWith('/employees/emp-9'))
  })
})
