import { useEffect, useState } from 'react'
import { getTransportOrders } from '../../../api/transportOrdersApi'
import type { TransportOrder } from '../types/transportOrder'

interface UseTransportOrdersResult {
  orders: TransportOrder[]
  isLoading: boolean
  error: string | null
}

const LOAD_ERROR_MESSAGE = 'Transportopdrachten konden niet worden geladen.'

export function useTransportOrders(): UseTransportOrdersResult {
  const [orders, setOrders] = useState<TransportOrder[]>([])
  const [isLoading, setIsLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let isMounted = true

    getTransportOrders()
      .then((data) => {
        if (!isMounted) return
        setOrders(data)
        setIsLoading(false)
      })
      .catch(() => {
        if (!isMounted) return
        setError(LOAD_ERROR_MESSAGE)
        setIsLoading(false)
      })

    return () => {
      isMounted = false
    }
  }, [])

  return { orders, isLoading, error }
}
