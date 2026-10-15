import { useCallback, useEffect } from 'react'
import { useSearchParams } from 'react-router-dom'

export interface DetailTabAlias<T extends string> {
  tab: T
  /** Extra params the alias implies (e.g. `section=chauffeursgegevens`). */
  params?: Record<string, string>
}

interface UseDetailTabOptions<T extends string> {
  /** Tabs the current user may open, in display order; anything else falls back to `defaultId`. */
  ids: readonly T[]
  defaultId: T
  /**
   * Retired `?tab=` values → their new home. Old bookmarks keep working: the URL is rewritten
   * once (replace, so Back does not bounce) and never keeps showing a retired id.
   */
  aliases?: Partial<Record<string, DetailTabAlias<T>>>
}

/**
 * The single section-navigation layer of an entity detail page, kept in `?tab=` (B15/B16/B17):
 * refresh, Back and shared links reopen the same section; the default tab keeps a clean URL.
 */
export function useDetailTab<T extends string>({ ids, defaultId, aliases }: UseDetailTabOptions<T>) {
  const [searchParams, setSearchParams] = useSearchParams()
  const requested = searchParams.get('tab')
  const alias = requested ? aliases?.[requested] : undefined
  const candidate = alias ? alias.tab : (requested as T | null)
  const tab: T = candidate && ids.includes(candidate) ? candidate : defaultId

  useEffect(() => {
    if (!alias) return
    const next = new URLSearchParams(searchParams)
    if (alias.tab === defaultId) next.delete('tab')
    else next.set('tab', alias.tab)
    for (const [key, value] of Object.entries(alias.params ?? {})) next.set(key, value)
    setSearchParams(next, { replace: true })
    // Only the raw ?tab= value matters; the alias is derived from it.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [requested])

  /** Opens a section. Row highlights and other section-local params are dropped unless passed. */
  const setTab = useCallback(
    (next: T, params?: Record<string, string>) => {
      const fresh = new URLSearchParams(params)
      if (next !== defaultId) fresh.set('tab', next)
      setSearchParams(fresh, { replace: true })
    },
    [defaultId, setSearchParams],
  )

  return { tab, setTab, searchParams, setSearchParams }
}
