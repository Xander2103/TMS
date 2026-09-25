import { useEffect, useState } from 'react'
import { pickAddresses, type AddressPickerOption } from '../api/customerAddressesApi'

/**
 * The ONE address search of the app (master sprint 2026-09-21, D3): GET /api/addresses/picker,
 * tenant-wide with the dossier customer's own addresses first. Both the stop's address-search
 * field and its street field run on this hook, so they can never disagree about what exists.
 *
 * GPS-style behaviour: nothing happens below `minChars`, typing is debounced, and a response can
 * only land while the query it was fired for is still the current one — every query change
 * aborts the request in flight AND marks it superseded, so a slow "Aven" can never overwrite a
 * fast "Avenue Sab" (the same abort + staleness guard `SearchableSelect` has in its async mode).
 */

export type AddressSearchStatus = 'idle' | 'loading' | 'done' | 'error'

export interface UseAddressSearchOptions {
  /** Rank this customer's addresses first. */
  customerId?: string | null
  /** Minimum trimmed query length before anything is requested. Default 2. */
  minChars?: number
  /** Also search on an EMPTY query (= the customer's own and recently used addresses). Default false. */
  suggestWhenEmpty?: boolean
  /** Debounce per query change. Default 250 ms. */
  debounceMs?: number
  /** Maximum number of suggestions (asked from the server and enforced here). Default 8. */
  take?: number
}

export interface AddressSearchResult {
  results: AddressPickerOption[]
  status: AddressSearchStatus
  /** True once the query is long enough to search at all. */
  active: boolean
}

interface SearchState {
  query: string
  results: AddressPickerOption[]
  status: 'done' | 'error'
}

function isAbortError(error: unknown): boolean {
  return typeof error === 'object' && error !== null && (error as { name?: unknown }).name === 'AbortError'
}

export function useAddressSearch(
  query: string,
  enabled: boolean,
  { customerId, minChars = 2, suggestWhenEmpty = false, debounceMs = 250, take = 8 }: UseAddressSearchOptions = {},
): AddressSearchResult {
  const trimmed = query.trim()
  const active = enabled && (trimmed.length >= minChars || (suggestWhenEmpty && trimmed === ''))
  const [state, setState] = useState<SearchState | null>(null)

  useEffect(() => {
    if (!active) return
    const controller = new AbortController()
    // One flag per query: set the moment the query moves on, checked before anything is written.
    let superseded = false
    const timer = setTimeout(() => {
      pickAddresses({ customerId, search: trimmed, take, signal: controller.signal }).then(
        (results) => {
          if (superseded) return
          setState({ query: trimmed, results: results.slice(0, take), status: 'done' })
        },
        (error: unknown) => {
          if (superseded || isAbortError(error)) return
          setState({ query: trimmed, results: [], status: 'error' })
        },
      )
      // Opening the empty field is one deliberate click — nothing to debounce.
    }, trimmed === '' ? 0 : debounceMs)
    return () => {
      // The query moved on (or the field closed/unmounted): whatever is pending or in flight
      // for the OLD query may no longer write anything.
      superseded = true
      clearTimeout(timer)
      controller.abort()
    }
  }, [active, trimmed, customerId, take, debounceMs])

  if (!active) return { results: [], status: 'idle', active }
  if (state && state.query === trimmed) return { results: state.results, status: state.status, active }
  // Still waiting for THIS query. While the planner keeps typing the same word the previous rows
  // stay visible (no flicker); rows of an unrelated query are never shown under a new one.
  const related =
    state !== null && state.status === 'done' && state.query !== '' && trimmed !== '' &&
    (trimmed.startsWith(state.query) || state.query.startsWith(trimmed))
  return { results: related ? state.results : [], status: 'loading', active }
}
