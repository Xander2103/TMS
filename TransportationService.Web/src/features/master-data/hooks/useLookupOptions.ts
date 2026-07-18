import { useEffect, useState } from 'react'
import { createLookupApi } from '../api/lookupApi'
import type { LookupOption } from '../types'

interface UseLookupOptionsResult {
  options: LookupOption[]
  isLoading: boolean
  error: string | null
}

/** Loads the active options for a lookup resource (e.g. `/api/customer-categories`) for dropdowns. */
export function useLookupOptions(basePath: string): UseLookupOptionsResult {
  const [options, setOptions] = useState<LookupOption[]>([])
  const [isLoading, setIsLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let isMounted = true
    const api = createLookupApi(basePath)
    setIsLoading(true)
    api
      .options()
      .then((data) => {
        if (!isMounted) return
        setOptions(data)
        setError(null)
        setIsLoading(false)
      })
      .catch(() => {
        if (!isMounted) return
        setError('Keuzelijst kon niet worden geladen.')
        setIsLoading(false)
      })
    return () => {
      isMounted = false
    }
  }, [basePath])

  return { options, isLoading, error }
}
