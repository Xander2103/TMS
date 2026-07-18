import { useCallback, useEffect, useState } from 'react'
import type { PagedResult } from '../api/types'

export interface PagedQueryArgs {
  search: string
  isActive?: boolean
  page: number
  pageSize: number
}

interface UsePagedQueryOptions {
  search: string
  isActive?: boolean
  page: number
  pageSize?: number
  debounceMs?: number
  errorMessage?: string
}

interface UsePagedQueryResult<T> {
  items: T[]
  totalCount: number
  pageSize: number
  isLoading: boolean
  error: string | null
  reload: () => void
}

const DEFAULT_PAGE_SIZE = 25
const DEFAULT_DEBOUNCE_MS = 300

/**
 * Generic debounced paged-list fetcher. Handles mount-safety, loading/error state and manual
 * reloads so list screens stay declarative. The fetcher must return a {@link PagedResult}.
 */
export function usePagedQuery<T>(
  fetcher: (args: PagedQueryArgs) => Promise<PagedResult<T>>,
  options: UsePagedQueryOptions,
): UsePagedQueryResult<T> {
  const {
    search,
    isActive,
    page,
    pageSize = DEFAULT_PAGE_SIZE,
    debounceMs = DEFAULT_DEBOUNCE_MS,
    errorMessage = 'Gegevens konden niet worden geladen.',
  } = options

  const [items, setItems] = useState<T[]>([])
  const [totalCount, setTotalCount] = useState(0)
  const [isLoading, setIsLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [reloadToken, setReloadToken] = useState(0)

  const reload = useCallback(() => setReloadToken((token) => token + 1), [])

  useEffect(() => {
    let isMounted = true
    setIsLoading(true)

    const timeoutId = window.setTimeout(() => {
      fetcher({ search, isActive, page, pageSize })
        .then((data) => {
          if (!isMounted) return
          setItems(data.items)
          setTotalCount(data.totalCount)
          setError(null)
          setIsLoading(false)
        })
        .catch(() => {
          if (!isMounted) return
          setError(errorMessage)
          setIsLoading(false)
        })
    }, debounceMs)

    return () => {
      isMounted = false
      window.clearTimeout(timeoutId)
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [search, isActive, page, pageSize, reloadToken])

  return { items, totalCount, pageSize, isLoading, error, reload }
}
