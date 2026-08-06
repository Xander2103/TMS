import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import * as api from '../../api/employeesApi'
import { EmployeesPage } from '../EmployeesPage'
import type { EmployeeListItem } from '../../types/employee'
import { completenessTone, contractEndBadge } from '../../utils/employeeListBadges'

// HR maturity wave, task 9: sorting, persisted filters and dossier-completeness badges on the
// personnel list. Covers the three failing-test bullets from the brief: (a) stored filters are
// restored from localStorage and reach the API call, (b) changing the sort control persists it,
// (c) the completeness badge picks the right tone per threshold.

const FILTER_STORAGE_KEY = 'ts.employees.filters'

vi.mock('../../../auth/authContextValue', () => ({
  useAuth: () => ({ hasPermission: () => true }),
}))
vi.mock('../../../master-data/hooks/useLookupOptions', () => ({
  useLookupOptions: () => ({ options: [], isLoading: false, error: null }),
}))

function employee(overrides: Partial<EmployeeListItem> = {}): EmployeeListItem {
  return {
    id: 'emp-1',
    employeeNumber: 'MED-0001',
    firstName: 'Jan',
    lastName: 'Peeters',
    functionNames: [],
    departmentName: null,
    employmentStatus: 'Active',
    isActive: true,
    isDriver: false,
    completenessPercentage: 100,
    employmentEndDate: null,
    ...overrides,
  }
}

function renderPage() {
  return render(
    <MemoryRouter initialEntries={['/employees']}>
      <EmployeesPage />
    </MemoryRouter>,
  )
}

beforeEach(() => {
  localStorage.clear()
  vi.restoreAllMocks()
  vi.spyOn(api, 'searchEmployees').mockResolvedValue({
    items: [employee()],
    totalCount: 1,
    page: 1,
    pageSize: 25,
  })
})

describe('EmployeesPage — persisted filters', () => {
  it('restores sort and incompleteOnly from localStorage and passes them to the API call', async () => {
    localStorage.setItem(FILTER_STORAGE_KEY, JSON.stringify({ sort: 'recent', incompleteOnly: true }))

    renderPage()

    expect(screen.getByLabelText('Sorteren')).toHaveValue('recent')
    expect(screen.getByLabelText('Enkel onvolledige dossiers')).toBeChecked()
    await waitFor(() =>
      expect(api.searchEmployees).toHaveBeenCalledWith(
        expect.objectContaining({ sort: 'recent', incompleteOnly: true }),
      ),
    )
  })

  it('falls back to the defaults when nothing is stored', async () => {
    renderPage()

    expect(screen.getByLabelText('Sorteren')).toHaveValue('name_asc')
    expect(screen.getByLabelText('Enkel onvolledige dossiers')).not.toBeChecked()
    await waitFor(() =>
      expect(api.searchEmployees).toHaveBeenCalledWith(
        expect.objectContaining({ sort: 'name_asc', incompleteOnly: undefined }),
      ),
    )
  })

  it('persists a sort change to localStorage and refetches with the new value', async () => {
    renderPage()
    await waitFor(() => expect(api.searchEmployees).toHaveBeenCalled())

    fireEvent.change(screen.getByLabelText('Sorteren'), { target: { value: 'number' } })

    await waitFor(() => {
      const stored = JSON.parse(localStorage.getItem(FILTER_STORAGE_KEY) ?? '{}')
      expect(stored.sort).toBe('number')
    })
    await waitFor(() =>
      expect(api.searchEmployees).toHaveBeenCalledWith(expect.objectContaining({ sort: 'number' })),
    )
  })

  it('checking "Enkel onvolledige dossiers" persists incompleteOnly and refetches', async () => {
    renderPage()
    await waitFor(() => expect(api.searchEmployees).toHaveBeenCalled())

    fireEvent.click(screen.getByLabelText('Enkel onvolledige dossiers'))

    await waitFor(() => {
      const stored = JSON.parse(localStorage.getItem(FILTER_STORAGE_KEY) ?? '{}')
      expect(stored.incompleteOnly).toBe(true)
    })
    await waitFor(() =>
      expect(api.searchEmployees).toHaveBeenCalledWith(expect.objectContaining({ incompleteOnly: true })),
    )
  })
})

describe('completenessTone', () => {
  it('is danger below 60%', () => {
    expect(completenessTone(0)).toBe('danger')
    expect(completenessTone(59)).toBe('danger')
  })

  it('is warning between 60% and 99%', () => {
    expect(completenessTone(60)).toBe('warning')
    expect(completenessTone(99)).toBe('warning')
  })

  it('is success at 100%', () => {
    expect(completenessTone(100)).toBe('success')
  })
})

describe('contractEndBadge', () => {
  const today = new Date(2026, 7, 6) // 2026-08-06

  it('warns when the end date is within 30 days (inclusive)', () => {
    expect(contractEndBadge({ employmentEndDate: '2026-08-06', isActive: true }, today))
      .toEqual({ tone: 'warning', label: 'Uit dienst over 0 d' })
    expect(contractEndBadge({ employmentEndDate: '2026-09-05', isActive: true }, today))
      .toEqual({ tone: 'warning', label: 'Uit dienst over 30 d' })
  })

  it('flags an overdue end date on a still-active employee as danger', () => {
    expect(contractEndBadge({ employmentEndDate: '2026-08-01', isActive: true }, today))
      .toEqual({ tone: 'danger', label: 'Einddatum verstreken' })
  })

  it('returns null for an overdue end date once the employee is no longer active', () => {
    expect(contractEndBadge({ employmentEndDate: '2026-08-01', isActive: false }, today)).toBeNull()
  })

  it('returns null when there is no end date or it is further than 30 days out', () => {
    expect(contractEndBadge({ employmentEndDate: null, isActive: true }, today)).toBeNull()
    expect(contractEndBadge({ employmentEndDate: '2026-09-10', isActive: true }, today)).toBeNull()
  })
})
