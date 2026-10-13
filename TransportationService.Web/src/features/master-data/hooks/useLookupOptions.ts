import { useCallback, useEffect, useRef, useState } from 'react'
import { getActiveLocale } from '../../../i18n/activeLocale'
import { translate } from '../../../i18n/translations'
import { createLookupApi } from '../api/lookupApi'
import type { LookupOption } from '../types'

interface UseLookupOptionsResult {
  options: LookupOption[]
  isLoading: boolean
  error: string | null
  /** Reloads the option list (e.g. after an inline create); resolves once the state is updated. */
  refresh: () => Promise<void>
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
  // Sequence number of the latest request: older responses (and responses arriving after an
  // unmount or basePath change) are dropped instead of overwriting newer state.
  const seqRef = useRef(0)

  const load = useCallback((): Promise<void> => {
    const seq = ++seqRef.current
    return createLookupApi(basePath)
      .options()
      .then(
        (data) => {
          if (seq !== seqRef.current) return
          setState({ options: data, error: null, loadedKey: basePath })
        },
        () => {
          if (seq !== seqRef.current) return
          // Ready-to-display text (module-level translate, mirroring utils/dates.ts): callers
          // render this string as-is, so it must not be a raw key.
          setState({ options: [], error: translate(getActiveLocale(), 'masterData.lookup.optionsLoadFailed'), loadedKey: basePath })
        },
      )
  }, [basePath])

  useEffect(() => {
    if (!enabled) return
    void load()
    const seq = seqRef
    return () => {
      seq.current++
    }
  }, [enabled, load])

  const refresh = useCallback(async () => {
    if (!enabled) return
    await load()
  }, [enabled, load])

  return { options: state.options, isLoading: enabled && state.loadedKey !== basePath, error: state.error, refresh }
}
