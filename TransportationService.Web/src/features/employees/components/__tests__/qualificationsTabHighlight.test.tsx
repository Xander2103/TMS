import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { QualificationsTab } from '../QualificationsTab'
import type { EmployeeQualification } from '../../types/qualification'

// Deep-link highlight (?qualificationId=…) coming from the expiry-warning strip: the matching
// table row gets a stable DOM id + `is-highlighted`, and is scrolled into view.

vi.mock('../../../auth/authContextValue', () => ({
  useAuth: () => ({ hasPermission: () => false }),
}))
vi.mock('../../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }),
}))
vi.mock('../QualificationDialog', () => ({ QualificationDialog: () => null }))
vi.mock('../../hooks/useQualificationMutations', () => ({
  useQualificationMutations: () => ({ isSubmitting: false, error: null, verify: vi.fn(), suspend: vi.fn() }),
}))

function qualification(id: string, name: string, expiryDate: string): EmployeeQualification {
  return {
    id,
    employeeId: 'emp-1',
    qualificationTypeId: 'type-1',
    qualificationTypeCode: 'C95',
    qualificationTypeName: name,
    documentNumber: null,
    obtainedDate: '2021-01-01',
    expiryDate,
    issuingCountryCode: 'BE',
    storedStatus: 'Valid',
    effectiveStatus: 'ExpiringSoon',
    hasDocument: false,
    notes: null,
    verifiedAt: null,
    verifiedByUserId: null,
  }
}

vi.mock('../../hooks/useEmployeeQualifications', () => ({
  useEmployeeQualifications: () => ({
    qualifications: [qualification('qual-1', 'Code 95', '2026-10-05'), qualification('qual-2', 'ADR', '2026-11-01')],
    isLoading: false,
    error: null,
    reload: vi.fn(),
  }),
}))

describe('QualificationsTab — deep-link highlight', () => {
  const scrollIntoView = vi.fn()

  beforeEach(() => {
    scrollIntoView.mockReset()
    Element.prototype.scrollIntoView = scrollIntoView
  })

  it('marks the requested qualification row and scrolls it into view', () => {
    render(<QualificationsTab employeeId="emp-1" highlightQualificationId="qual-2" />)

    const row = document.getElementById('employee-qualification-qual-2')
    expect(row).not.toBeNull()
    expect(row?.tagName).toBe('TR')
    expect(row).toHaveClass('is-highlighted')
    expect(row).toHaveTextContent('ADR')
    expect(document.getElementById('employee-qualification-qual-1')).not.toHaveClass('is-highlighted')
    expect(scrollIntoView).toHaveBeenCalledTimes(1)
  })

  it('highlights nothing without a target', () => {
    render(<QualificationsTab employeeId="emp-1" />)
    expect(screen.getByText('Code 95')).toBeInTheDocument()
    expect(document.querySelector('.is-highlighted')).toBeNull()
    expect(scrollIntoView).not.toHaveBeenCalled()
  })
})
