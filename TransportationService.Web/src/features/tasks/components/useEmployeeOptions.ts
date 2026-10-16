import { useEffect, useState } from 'react'
import type { SearchableSelectOption } from '../../../components/ui/SearchableSelect'
import { searchEmployees } from '../../employees/api/employeesApi'
import { listAssignableEmployees } from '../api/tasksApi'
import type { AssignLimitation, AssignScope } from '../api/types'

export type EmployeeOptionsSource = 'all' | 'assignable'

interface EmployeeOptionsState {
  options: SearchableSelectOption[]
  isLoading: boolean
  /** Only for source "assignable": the caller's assign scope and why it is narrowed. */
  scope?: AssignScope
  limitation?: AssignLimitation | null
  departmentName?: string | null
}

interface EmployeeLike {
  id: string
  firstName: string
  lastName: string
  employeeNumber: string
}

function toOption(employee: EmployeeLike): SearchableSelectOption {
  return {
    value: employee.id,
    label: `${employee.firstName} ${employee.lastName}`,
    description: employee.employeeNumber,
    keywords: employee.employeeNumber,
  }
}

/**
 * Loads the employees once as combobox options (name + employee number). Source "all" lists the
 * active employees; "assignable" (task assignee pickers, HR acceptance #4) lists only those the
 * signed-in user may assign a task to, so the picker never offers a choice the server refuses.
 */
export function useEmployeeOptions(source: EmployeeOptionsSource = 'all'): EmployeeOptionsState {
  const [state, setState] = useState<EmployeeOptionsState>({ options: [], isLoading: true })

  useEffect(() => {
    let mounted = true
    const load: Promise<EmployeeOptionsState> =
      source === 'assignable'
        ? listAssignableEmployees().then((result) => ({
            options: result.items.map(toOption),
            isLoading: false,
            scope: result.scope,
            limitation: result.limitation,
            departmentName: result.departmentName,
          }))
        : searchEmployees({ isActive: true, page: 1, pageSize: 500 }).then((result) => ({
            options: result.items.map(toOption),
            isLoading: false,
          }))
    load
      .then((next) => {
        if (mounted) setState(next)
      })
      .catch(() => {
        if (mounted) setState({ options: [], isLoading: false })
      })
    return () => {
      mounted = false
    }
  }, [source])

  return state
}
