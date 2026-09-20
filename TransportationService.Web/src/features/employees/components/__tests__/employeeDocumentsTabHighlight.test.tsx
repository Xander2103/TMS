import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { EmployeeDocumentsTab } from '../EmployeeDocumentsTab'
import type { EmployeeDocument } from '../../api/employeeDocumentsApi'

// Deep-link highlight (?documentId=…) coming from the expiry-warning strip: the matching row
// gets a stable DOM id + `is-highlighted`, and is scrolled into view.

vi.mock('../../../auth/authContextValue', () => ({
  useAuth: () => ({ hasPermission: () => false }),
}))
vi.mock('../../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }),
}))

function doc(id: string, category: EmployeeDocument['category'], fileName: string): EmployeeDocument {
  return {
    id,
    category,
    customLabel: null,
    fileName,
    contentType: 'application/pdf',
    sizeBytes: 1024,
    expiryDate: '2026-09-24',
    notes: null,
    isArchived: false,
    isSensitive: false,
    uploadedAt: '2026-09-01T10:00:00Z',
    uploadedByUserId: null,
  }
}

vi.mock('../../hooks/useEmployeeDocuments', () => ({
  useEmployeeDocuments: () => ({
    documents: [doc('doc-1', 'MedicalDocument', 'attest.pdf'), doc('doc-2', 'Contract', 'contract.pdf')],
    isLoading: false,
    error: null,
    reload: vi.fn(),
  }),
}))

describe('EmployeeDocumentsTab — deep-link highlight', () => {
  const scrollIntoView = vi.fn()

  beforeEach(() => {
    scrollIntoView.mockReset()
    Element.prototype.scrollIntoView = scrollIntoView
  })

  it('marks the requested document and scrolls it into view', () => {
    render(<EmployeeDocumentsTab employeeId="emp-1" highlightDocumentId="doc-2" />)

    const row = document.getElementById('employee-document-doc-2')
    expect(row).not.toBeNull()
    expect(row).toHaveClass('is-highlighted')
    expect(row).toHaveTextContent('contract.pdf')
    expect(document.getElementById('employee-document-doc-1')).not.toHaveClass('is-highlighted')
    expect(scrollIntoView).toHaveBeenCalledTimes(1)
  })

  it('highlights nothing without a target', () => {
    render(<EmployeeDocumentsTab employeeId="emp-1" />)
    expect(screen.getByText('attest.pdf')).toBeInTheDocument()
    expect(document.querySelector('.is-highlighted')).toBeNull()
    expect(scrollIntoView).not.toHaveBeenCalled()
  })
})
