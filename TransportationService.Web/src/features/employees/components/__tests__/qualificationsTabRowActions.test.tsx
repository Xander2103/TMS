import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QualificationsTab } from '../QualificationsTab'
import type { EmployeeQualification } from '../../types/qualification'

// B15: one line per qualification — one direct document action, the rest in the row's ⋯ menu,
// removing a document asks for confirmation; every action respects its own permission.

const auth = vi.hoisted(() => ({ permissions: new Set<string>() }))
vi.mock('../../../auth/authContextValue', () => ({
  useAuth: () => ({ hasPermission: (code: string) => auth.permissions.has(code) }),
}))
vi.mock('../../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }),
}))
vi.mock('../QualificationDialog', () => ({ QualificationDialog: () => null }))
vi.mock('../../hooks/useQualificationMutations', () => ({
  useQualificationMutations: () => ({ isSubmitting: false, error: null, verify: vi.fn(), suspend: vi.fn() }),
}))

const ROW: EmployeeQualification = {
  id: 'q-1',
  employeeId: 'emp-1',
  qualificationTypeId: 't-1',
  qualificationTypeCode: 'ADR',
  qualificationTypeName: 'ADR',
  documentNumber: 'ADR-100000',
  obtainedDate: '2021-01-01',
  expiryDate: '2026-09-25',
  issuingCountryCode: 'BE',
  storedStatus: 'Pending',
  effectiveStatus: 'Expired',
  hasDocument: true,
  notes: null,
  verifiedAt: null,
  verifiedByUserId: null,
}

vi.mock('../../hooks/useEmployeeQualifications', () => ({
  useEmployeeQualifications: () => ({ qualifications: [ROW], isLoading: false, error: null, reload: vi.fn() }),
}))

describe('QualificationsTab — row actions (B15)', () => {
  it('keeps the document number on the type line and has no action menu for a read-only viewer', () => {
    auth.permissions = new Set()
    render(<QualificationsTab employeeId="emp-1" />)
    expect(screen.getByText(/ADR-100000/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Downloaden' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Acties ADR' })).not.toBeInTheDocument()
  })

  it('groups edit, verify, replace, remove and suspend in the ⋯ menu with destructive items last', async () => {
    auth.permissions = new Set(['employee_documents.edit', 'employee_documents.approve', 'employee_documents.delete'])
    render(<QualificationsTab employeeId="emp-1" />)
    await userEvent.click(screen.getByRole('button', { name: 'Acties ADR' }))
    expect(screen.getAllByRole('menuitem').map((item) => item.textContent)).toEqual([
      'Bewerken',
      'Verifiëren',
      'Vervangen',
      'Document verwijderen',
      'Intrekken',
    ])
    await userEvent.click(screen.getByRole('menuitem', { name: 'Document verwijderen' }))
    expect(screen.getByRole('dialog', { name: 'Document verwijderen?' })).toBeInTheDocument()
  })
})
