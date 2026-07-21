import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { EmployeeDocumentsTab } from '../components/EmployeeDocumentsTab'
import type { EmployeeDocument } from '../api/employeeDocumentsApi'

vi.mock('../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }),
}))

// Mutable permission set so each test controls what useAuth() reports.
const auth = vi.hoisted(() => ({ permissions: ['employee_documents.view'] }))

vi.mock('../../auth/authContextValue', () => ({
  useAuth: () => ({
    status: 'authenticated' as const,
    user: null,
    login: vi.fn(),
    logout: vi.fn(),
    hasPermission: (code: string) => auth.permissions.includes(code),
    hasAnyPermission: (codes: string[]) => codes.some((code) => auth.permissions.includes(code)),
  }),
}))

const docs = vi.hoisted(() => ({ value: [] as EmployeeDocument[] }))

vi.mock('../api/employeeDocumentsApi', async () => {
  const actual = await vi.importActual<typeof import('../api/employeeDocumentsApi')>('../api/employeeDocumentsApi')
  return {
    ...actual,
    listEmployeeDocuments: () => Promise.resolve(docs.value),
    uploadEmployeeDocument: vi.fn(),
    replaceEmployeeDocumentFile: vi.fn(),
    setEmployeeDocumentArchived: vi.fn(),
    deleteEmployeeDocument: vi.fn(),
    downloadEmployeeDocument: vi.fn(),
  }
})

function makeDocument(overrides: Partial<EmployeeDocument>): EmployeeDocument {
  return {
    id: 'doc-1',
    category: 'AdditionalDocument',
    customLabel: null,
    fileName: 'paspoort.pdf',
    contentType: 'application/pdf',
    sizeBytes: 4096,
    expiryDate: null,
    notes: null,
    isArchived: false,
    isSensitive: false,
    uploadedAt: '2026-07-21T09:00:00Z',
    uploadedByUserId: null,
    ...overrides,
  }
}

describe('EmployeeDocumentsTab', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    auth.permissions = ['employee_documents.view']
    docs.value = [makeDocument({})]
  })

  it('shows the sensitive-hidden info line and renders documents without view_sensitive', async () => {
    render(<EmployeeDocumentsTab employeeId="e-1" />)

    expect(await screen.findByText('paspoort.pdf')).toBeInTheDocument()
    expect(
      screen.getByText(/Gevoelige documenten \(identiteitskaart, medisch, contract\) zijn verborgen/),
    ).toBeInTheDocument()
  })

  it('hides the info line when the user has view_sensitive', async () => {
    auth.permissions = ['employee_documents.view', 'employee_documents.view_sensitive']
    render(<EmployeeDocumentsTab employeeId="e-1" />)

    expect(await screen.findByText('paspoort.pdf')).toBeInTheDocument()
    expect(screen.queryByText(/Gevoelige documenten/)).not.toBeInTheDocument()
  })
})
