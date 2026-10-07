import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { parseDossierSearchParams, toSearchParams, type DossierSearchFilters } from './dossierSearchParams'

interface FilterView {
  /** The URL querystring this view was last reconciled with. */
  seenUrl: string
  /** The querystring the UI shows: the newest request, which the URL reaches a render later. */
  query: string
  /** Querystrings navigated to but not yet seen in the URL, oldest first. */
  sent: string[]
}

/** An own navigation arriving keeps the newest request; any other URL change (Back/Forward, a link) wins. */
function reconcile(view: FilterView, url: string): FilterView {
  const index = view.sent.lastIndexOf(url)
  return index >= 0 ? { seenUrl: url, query: view.query, sent: view.sent.slice(index + 1) } : { seenUrl: url, query: url, sent: [] }
}

interface DossierFiltersApi {
  filters: DossierSearchFilters
  /**
   * Patches the filters. Any change restarts at page 1 (a shrunken result set must not strand the
   * user past the end); an explicit `page` in the patch wins. Typing passes `replace` so the back
   * button skips keystrokes; discrete choices push, so back undoes them one by one.
   */
  apply: (patch: Partial<DossierSearchFilters>, options?: { replace?: boolean }) => void
}

/**
 * Filter state of the dossier list, with the querystring as the one durable source of truth:
 * refresh, Back/Forward and a shared link all restore the exact view.
 *
 * The router commits a URL change a render later (as a transition). Changes made in that gap —
 * the next keystroke, a second filter picked right after the first — must build on the previous
 * REQUEST, not on the URL that has not caught up yet, or they would silently drop it. So the hook
 * shows the newest request at once and lets the URL follow; it never holds a filter the URL does
 * not get.
 */
export function useDossierFilters(): DossierFiltersApi {
  const [searchParams, setSearchParams] = useSearchParams()
  const url = searchParams.toString()
  const [view, setView] = useState<FilterView>(() => ({ seenUrl: url, query: url, sent: [] }))

  let current = view
  if (view.seenUrl !== url) {
    current = reconcile(view, url)
    setView(current)
  }

  const latest = useRef(current)
  useEffect(() => {
    latest.current = current
  }, [current])

  const filters = useMemo(() => parseDossierSearchParams(new URLSearchParams(current.query)), [current.query])

  const apply = useCallback(
    (patch: Partial<DossierSearchFilters>, options: { replace?: boolean } = {}) => {
      const base = parseDossierSearchParams(new URLSearchParams(latest.current.query))
      const next = toSearchParams({ ...base, ...patch, page: patch.page ?? 1 })
      const query = next.toString()
      if (query === latest.current.query) return
      const requested: FilterView = { ...latest.current, query, sent: [...latest.current.sent, query] }
      latest.current = requested
      setView(requested)
      setSearchParams(next, options)
    },
    [setSearchParams],
  )

  return { filters, apply }
}
