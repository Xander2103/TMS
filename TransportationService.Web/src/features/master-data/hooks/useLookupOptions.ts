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
  const [state, setState] = useState<{ options: LookupOption[]; error: string | null; loadedKey: string }>({
    options: [],
    error: null,
    loadedKey: '',
  })

  useEffect(() => {
    let isMounted = true
    const api = createLookupApi(basePath)
    api
      .options()
      .then((data) => {
        if (!isMounted) return
        setState({ options: data, error: null, loadedKey: basePath })
      })
      .catch(() => {
        if (!isMounted) return
        setState({ options: [], error: 'Keuzelijst kon niet worden geladen.', loadedKey: basePath })
      })
    return () => {
      isMounted = false
    }
  }, [basePath])

  return { options: state.options, isLoading: state.loadedKey !== basePath, error: state.error }
}
