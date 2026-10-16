import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import * as employeesApi from '../../employees/api/employeesApi'
import * as api from '../api/tasksApi'
import type { AssignableEmployees } from '../api/types'
import { EmployeeMultiSelect, EmployeeSelect } from '../components/EmployeePicker'

function assignable(overrides: Partial<AssignableEmployees> = {}): AssignableEmployees {
  return {
    scope: 'department',
    limitation: null,
    departmentName: 'HR',
    items: [
      { id: 'emp-1', firstName: 'Hanne', lastName: 'Hermans', employeeNumber: 'MED-1', departmentId: 'dep-hr' },
      { id: 'emp-2', firstName: 'Pieter', lastName: 'Peeters', employeeNumber: 'MED-2', departmentId: 'dep-hr' },
    ],
    ...overrides,
  }
}

beforeEach(() => {
  vi.restoreAllMocks()
})

// HR acceptance #4: the task assignee pickers only offer employees the user may assign to.
describe('EmployeePicker (assignable source)', () => {
  it('offers only the employees in the assign scope, never the whole employee list', async () => {
    const assignableSpy = vi.spyOn(api, 'listAssignableEmployees').mockResolvedValue(assignable())
    const allSpy = vi.spyOn(employeesApi, 'searchEmployees')
    const user = userEvent.setup()

    render(<EmployeeMultiSelect source="assignable" value={[]} onChange={vi.fn()} />)
    await waitFor(() => expect(assignableSpy).toHaveBeenCalled())
    await user.click(screen.getByRole('combobox', { name: 'Medewerker toevoegen' }))

    expect(await screen.findAllByRole('option')).toHaveLength(2)
    expect(screen.getByRole('option', { name: /Hanne Hermans/ })).toBeInTheDocument()
    expect(screen.getByRole('option', { name: /Pieter Peeters/ })).toBeInTheDocument()
    expect(allSpy).not.toHaveBeenCalled()
    expect(screen.getByRole('note')).toHaveTextContent('Je kan taken toewijzen aan medewerkers van je eigen afdeling (HR).')
  })

  it('explains an HR account without a department instead of showing an unexplained empty list', async () => {
    vi.spyOn(api, 'listAssignableEmployees').mockResolvedValue(
      assignable({ scope: 'self', limitation: 'no_department', departmentName: null, items: [] }),
    )

    render(<EmployeeSelect source="assignable" value={null} onChange={vi.fn()} />)

    expect(await screen.findByRole('note')).toHaveTextContent(
      'Je account is niet aan een afdeling gekoppeld; je kan alleen taken voor jezelf aanmaken.',
    )
  })

  it('keeps the full active-employee list for non-task callers (default source)', async () => {
    const assignableSpy = vi.spyOn(api, 'listAssignableEmployees')
    const allSpy = vi.spyOn(employeesApi, 'searchEmployees').mockResolvedValue({
      items: [],
      totalCount: 0,
      page: 1,
      pageSize: 500,
    } as unknown as Awaited<ReturnType<typeof employeesApi.searchEmployees>>)

    render(<EmployeeSelect value={null} onChange={vi.fn()} />)

    await waitFor(() => expect(allSpy).toHaveBeenCalled())
    expect(assignableSpy).not.toHaveBeenCalled()
    expect(screen.queryByRole('note')).not.toBeInTheDocument()
  })
})
