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

  const [state, setState] = useState<{ items: T[]; totalCount: number; error: string | null; loadedKey: string }>({
    items: [],
    totalCount: 0,
    error: null,
    loadedKey: '',
  })
  const [reloadToken, setReloadToken] = useState(0)

  const reload = useCallback(() => setReloadToken((token) => token + 1), [])

  // Identifies the current request. Loading is derived from whether the loaded result matches it,
  // so state is only ever mutated inside async callbacks (never synchronously in the effect body).
  const requestKey = JSON.stringify({ search, isActive, page, pageSize, reloadToken })

  useEffect(() => {
    let isMounted = true

    const timeoutId = window.setTimeout(() => {
      fetcher({ search, isActive, page, pageSize })
        .then((data) => {
          if (!isMounted) return
          setState({ items: data.items, totalCount: data.totalCount, error: null, loadedKey: requestKey })
        })
        .catch(() => {
          if (!isMounted) return
          setState((current) => ({ ...current, error: errorMessage, loadedKey: requestKey }))
        })
    }, debounceMs)

    return () => {
      isMounted = false
      window.clearTimeout(timeoutId)
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [requestKey])

  return {
    items: state.items,
    totalCount: state.totalCount,
    pageSize,
    isLoading: state.loadedKey !== requestKey,
    error: state.error,
    reload,
  }
}
