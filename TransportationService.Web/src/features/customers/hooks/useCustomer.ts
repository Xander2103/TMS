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
  const [customer, setCustomer] = useState<CustomerDetail | null>(null)
  const [isLoading, setIsLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [reloadToken, setReloadToken] = useState(0)

  const reload = useCallback(() => setReloadToken((token) => token + 1), [])

  useEffect(() => {
    if (!id) return
    let isMounted = true
    setIsLoading(true)
    getCustomer(id)
      .then((data) => {
        if (!isMounted) return
        setCustomer(data)
        setError(null)
        setIsLoading(false)
      })
      .catch(() => {
        if (!isMounted) return
        setError('Klant kon niet worden geladen.')
        setIsLoading(false)
      })
    return () => {
      isMounted = false
    }
  }, [id, reloadToken])

  return { customer, isLoading, error, reload }
}
