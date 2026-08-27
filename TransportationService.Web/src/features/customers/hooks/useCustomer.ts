import { useCallback, useEffect, useState } from 'react'
import { getCustomer } from '../api/customersApi'
import type { CustomerDetail } from '../types'

interface UseCustomerResult {
  customer: CustomerDetail | null
  isLoading: boolean
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

  return {
    customer: state.customer,
    // Loading whenever an id is set but its result has not yet arrived.
    isLoading: id !== undefined && state.loadedKey !== requestKey,
    error: state.error,
    reload,
  }
}
