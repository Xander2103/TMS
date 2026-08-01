import { useEffect, useState } from 'react'
import type { SearchableSelectOption } from '../../../components/ui/SearchableSelect'
import { searchEmployees } from '../../employees/api/employeesApi'

interface EmployeeOptionsState {
  options: SearchableSelectOption[]
  isLoading: boolean
}

/** Loads the active employees once as combobox options (name + employee number). */
export function useEmployeeOptions(): EmployeeOptionsState {
  const [state, setState] = useState<EmployeeOptionsState>({ options: [], isLoading: true })

  useEffect(() => {
    let mounted = true
    searchEmployees({ isActive: true, page: 1, pageSize: 500 })
      .then((result) => {
        if (!mounted) return
        setState({
          options: result.items.map((employee) => ({
            value: employee.id,
            label: `${employee.firstName} ${employee.lastName}`,
            description: employee.employeeNumber,
            keywords: employee.employeeNumber,
          })),
          isLoading: false,
        })
      })
      .catch(() => {
        if (!mounted) return
        setState({ options: [], isLoading: false })
      })
    return () => {
      mounted = false
    }
  }, [])

  return state
}
