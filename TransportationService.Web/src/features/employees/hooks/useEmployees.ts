import { useEffect, useState } from 'react'
import { searchEmployees } from '../api/employeesApi'
import type { EmployeeListItem } from '../types/employee'

const PAGE_SIZE = 25
const SEARCH_DEBOUNCE_MS = 300
const LOAD_ERROR_MESSAGE = 'Medewerkers konden niet worden geladen.'

export interface UseEmployeesParams {
  search: string
  isActive?: boolean
  page: number
}

interface UseEmployeesResult {
  items: EmployeeListItem[]
  totalCount: number
  pageSize: number
  isLoading: boolean
  error: string | null
}

export function useEmployees(params: UseEmployeesParams): UseEmployeesResult {
  const [items, setItems] = useState<EmployeeListItem[]>([])
  const [totalCount, setTotalCount] = useState(0)
  const [isLoading, setIsLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let isMounted = true
    const timeoutId = window.setTimeout(() => {
      searchEmployees({ search: params.search, isActive: params.isActive, page: params.page, pageSize: PAGE_SIZE })
        .then((data) => {
          if (!isMounted) return
          setItems(data.items)
          setTotalCount(data.totalCount)
          setError(null)
          setIsLoading(false)
        })
        .catch(() => {
          if (!isMounted) return
          setError(LOAD_ERROR_MESSAGE)
          setIsLoading(false)
        })
    }, SEARCH_DEBOUNCE_MS)

    return () => {
      isMounted = false
      window.clearTimeout(timeoutId)
    }
  }, [params.search, params.isActive, params.page])

  return { items, totalCount, pageSize: PAGE_SIZE, isLoading, error }
}
