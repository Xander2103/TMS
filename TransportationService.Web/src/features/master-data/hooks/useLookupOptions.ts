import { useEffect, useState } from 'react'
import { getActiveLocale } from '../../../i18n/activeLocale'
import { translate } from '../../../i18n/translations'
import { createLookupApi } from '../api/lookupApi'
import type { LookupOption } from '../types'

interface UseLookupOptionsResult {
  options: LookupOption[]
  isLoading: boolean
  error: string | null
}

/**
 * Loads the active options for a lookup resource (e.g. `/api/customer-categories`) for dropdowns.
 * Pass `enabled: false` (e.g. when the user lacks the view permission) to skip the request.
 */
export function useLookupOptions(basePath: string, opts?: { enabled?: boolean }): UseLookupOptionsResult {
  const enabled = opts?.enabled ?? true
  const [state, setState] = useState<{ options: LookupOption[]; error: string | null; loadedKey: string }>({
    options: [],
    error: null,
    loadedKey: '',
  })

  useEffect(() => {
    if (!enabled) return
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
        // Ready-to-display text (module-level translate, mirroring utils/dates.ts): callers
        // render this string as-is, so it must not be a raw key.
        setState({ options: [], error: translate(getActiveLocale(), 'masterData.lookup.optionsLoadFailed'), loadedKey: basePath })
      })
    return () => {
      isMounted = false
    }
  }, [basePath, enabled])

  return { options: state.options, isLoading: enabled && state.loadedKey !== basePath, error: state.error }
}
