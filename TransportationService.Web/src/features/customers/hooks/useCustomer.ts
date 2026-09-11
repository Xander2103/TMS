import { useCallback, useEffect, useState } from 'react'
import { getCustomer } from '../api/customersApi'
import type { CustomerDetail } from '../types'

interface UseCustomerResult {
  customer: CustomerDetail | null
  /** True only while nothing for this id has been loaded yet (first load or a new id). */
  isLoading: boolean
  /**
   * True while a `reload()` for the already-shown customer is in flight. The previous
   * `customer` stays available (stale-while-refetch) so a page never has to unmount its tree.
   */
  isRefreshing: boolean
  error: string | null
  reload: () => void
}

export function useCustomer(id: string | undefined): UseCustomerResult {
  const [state, setState] = useState<{ customer: CustomerDetail | null; error: string | null; loadedKey: string }>({
    customer: null,
    error: null,
    loadedKey: '',
  })
  const [reloadToken, setReloadToken] = useState(0)

  const reload = useCallback(() => setReloadToken((token) => token + 1), [])

  const requestKey = id ? `${id}:${reloadToken}` : ''

  useEffect(() => {
    if (!id) return
    let isMounted = true
    getCustomer(id)
      .then((data) => {
        if (!isMounted) return
        setState({ customer: data, error: null, loadedKey: requestKey })
      })
      .catch(() => {
        if (!isMounted) return
        // Vertaalsleutel; de renderende pagina vertaalt via t(error).
        setState({ customer: null, error: 'customers.detail.loadFailed', loadedKey: requestKey })
      })
    return () => {
      isMounted = false
    }
  }, [id, requestKey])

  // Pending whenever an id is set but the result for this exact request has not yet arrived.
  const pending = id !== undefined && state.loadedKey !== requestKey
  // The customer we hold belongs to the current id (not to a previous route) → keep showing it.
  const hasCurrent = state.customer !== null && id !== undefined && state.loadedKey.startsWith(`${id}:`)

  return {
    customer: hasCurrent ? state.customer : null,
    isLoading: pending && !hasCurrent,
    isRefreshing: pending && hasCurrent,
    error: state.error,
    reload,
  }
}
