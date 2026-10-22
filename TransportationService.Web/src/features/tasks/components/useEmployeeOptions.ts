import { useEffect, useState } from 'react'
import type { SearchableSelectOption, SearchableSelectSearchFn } from '../../../components/ui/SearchableSelect'
import { searchActiveEmployeeOptions } from '../../employees/api/employeeOptions'
import { listAssignableEmployees } from '../api/tasksApi'
import type { AssignLimitation, AssignScope } from '../api/types'

export type EmployeeOptionsSource = 'all' | 'assignable'

interface EmployeeOptionsState {
  options: SearchableSelectOption[]
  isLoading: boolean
  /**
   * PERF-H3: source "all" searches the server as the user types (the first 500 employees used to
   * be loaded and filtered client-side); `options` is then only the labels already known.
   */
  onSearch?: SearchableSelectSearchFn
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
 * Combobox options for the employee pickers (name + employee number). Source "all" searches the
 * active employees server-side as the user types; "assignable" (task assignee pickers, HR
 * acceptance #4) loads only those the signed-in user may assign a task to, so the picker never
 * offers a choice the server refuses.
 */
export function useEmployeeOptions(source: EmployeeOptionsSource = 'all'): EmployeeOptionsState {
  const [state, setState] = useState<EmployeeOptionsState>({ options: [], isLoading: source === 'assignable' })

  useEffect(() => {
    if (source !== 'assignable') return
    let mounted = true
    listAssignableEmployees()
      .then((result) => {
        if (!mounted) return
        setState({
          options: result.items.map(toOption),
          isLoading: false,
          scope: result.scope,
          limitation: result.limitation,
          departmentName: result.departmentName,
        })
      })
      .catch(() => {
        if (mounted) setState({ options: [], isLoading: false })
      })
    return () => {
      mounted = false
    }
  }, [source])

  if (source === 'assignable') return state
  return { options: [], isLoading: false, onSearch: searchActiveEmployeeOptions }
}
