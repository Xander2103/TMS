import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useSearchParams } from 'react-router-dom'

/**
 * B26: list state (search, filters, sort, page) with the URL querystring as the ONE durable
 * source of truth — refresh, Back/Forward and a shared link restore the exact view. Generalised
 * from the dossier list (`useDossierFilters`), including its fix for changes made before the
 * router committed the previous one (they build on the newest request, never on a stale URL).
 *
 * Rules: values equal to their default are omitted (a clean URL is the default view); a value
 * outside `allowed` falls back to the default (invalid links fail safely); any change except an
 * explicit `page` returns to page 1; typing uses `replace` so Back skips keystrokes.
 */
export type ListStateDefaults = Record<string, string> & { page?: never }

export interface UrlListStateOptions<T extends Record<string, string>> {
  defaults: T
  /** Whitelisted values per key; anything else reads as the default. */
  allowed?: Partial<Record<keyof T, readonly string[]>>
}

export type UrlListState<T extends Record<string, string>> = T & { page: number }

export interface UrlListStateApi<T extends Record<string, string>> {
  state: UrlListState<T>
  apply: (patch: Partial<UrlListState<T>>, options?: { replace?: boolean }) => void
  /** Back to the clean default view (keeps nothing). */
  reset: () => void
  /** True when anything differs from the defaults (search/filters/sort; page ignored). */
  isFiltered: boolean
}

interface View {
  seenUrl: string
  query: string
  sent: string[]
}

function reconcile(view: View, url: string): View {
  const index = view.sent.lastIndexOf(url)
  return index >= 0 ? { seenUrl: url, query: view.query, sent: view.sent.slice(index + 1) } : { seenUrl: url, query: url, sent: [] }
}

export function parseListState<T extends Record<string, string>>(params: URLSearchParams, options: UrlListStateOptions<T>): UrlListState<T> {
  const result = {} as Record<string, string | number>
  for (const key of Object.keys(options.defaults)) {
    const raw = params.get(key)
    const allowed = options.allowed?.[key as keyof T]
    result[key] = raw === null || (allowed && !allowed.includes(raw)) ? options.defaults[key] : raw
  }
  const page = Number(params.get('page'))
  result.page = Number.isInteger(page) && page >= 1 ? page : 1
  return result as UrlListState<T>
}

export function serializeListState<T extends Record<string, string>>(state: UrlListState<T>, options: UrlListStateOptions<T>): URLSearchParams {
  const params = new URLSearchParams()
  for (const key of Object.keys(options.defaults)) {
    const value = state[key]
    if (value !== undefined && value !== null && value !== '' && value !== options.defaults[key]) params.set(key, String(value))
  }
  if (state.page > 1) params.set('page', String(state.page))
  return params
}

export function useUrlListState<T extends Record<string, string>>(options: UrlListStateOptions<T>): UrlListStateApi<T> {
  const [searchParams, setSearchParams] = useSearchParams()
  const url = searchParams.toString()
  const [view, setView] = useState<View>(() => ({ seenUrl: url, query: url, sent: [] }))

  let current = view
  if (view.seenUrl !== url) {
    current = reconcile(view, url)
    setView(current)
  }

  const latest = useRef(current)
  useEffect(() => {
    latest.current = current
  }, [current])

  const optionsRef = useRef(options)
  useEffect(() => {
    optionsRef.current = options
  })

  // The options object is usually a module constant; the parse depends on its contents only.
  const state = useMemo(() => parseListState(new URLSearchParams(current.query), options), [current.query]) // eslint-disable-line react-hooks/exhaustive-deps

  const apply = useCallback(
    (patch: Partial<UrlListState<T>>, applyOptions: { replace?: boolean } = {}) => {
      const opts = optionsRef.current
      const base = parseListState(new URLSearchParams(latest.current.query), opts)
      const next = serializeListState({ ...base, ...patch, page: patch.page ?? 1 } as UrlListState<T>, opts)
      const query = next.toString()
      if (query === latest.current.query) return
      const requested: View = { ...latest.current, query, sent: [...latest.current.sent, query] }
      latest.current = requested
      setView(requested)
      setSearchParams(next, applyOptions)
    },
    [setSearchParams],
  )

  const reset = useCallback(() => {
    const requested: View = { ...latest.current, query: '', sent: [...latest.current.sent, ''] }
    latest.current = requested
    setView(requested)
    setSearchParams(new URLSearchParams())
  }, [setSearchParams])

  const isFiltered = Object.keys(options.defaults).some((key) => state[key] !== options.defaults[key])

  return { state, apply, reset, isFiltered }
}

/** The DataTable sort for a list state; an empty sort shows no column as sorted. */
export function tableSortOf(sort: string, dir: string, descendingByDefault: readonly string[] = []): { key: string; dir: 'asc' | 'desc' } | null {
  if (!sort) return null
  return { key: sort, dir: dir === 'asc' || dir === 'desc' ? dir : descendingByDefault.includes(sort) ? 'desc' : 'asc' }
}
