import { useEffect, useRef, useState } from 'react'
import { createTransportOrder } from '../../../api/transportOrdersApi'
import type { CreateTransportOrderInput, TransportOrder } from '../types/transportOrder'

const SUBMIT_ERROR_MESSAGE = 'De opdracht kon niet worden aangemaakt. Probeer het opnieuw.'

interface UseCreateTransportOrderResult {
  isSubmitting: boolean
  error: string | null
  submit: (input: CreateTransportOrderInput) => Promise<TransportOrder | null>
}

export function useCreateTransportOrder(): UseCreateTransportOrderResult {
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const isMountedRef = useRef(true)

  useEffect(() => {
    isMountedRef.current = true
    return () => {
      isMountedRef.current = false
    }
  }, [])

  async function submit(input: CreateTransportOrderInput): Promise<TransportOrder | null> {
    setIsSubmitting(true)
    setError(null)

    try {
      const created = await createTransportOrder(input)
      if (isMountedRef.current) {
        setIsSubmitting(false)
      }
      return created
    } catch {
      if (isMountedRef.current) {
        setError(SUBMIT_ERROR_MESSAGE)
        setIsSubmitting(false)
      }
      return null
    }
  }

  return { isSubmitting, error, submit }
}
