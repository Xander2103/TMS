import { useEffect, useState } from 'react'

export type LabelResolver = (id: string, signal: AbortSignal) => Promise<string | null>

/**
 * Display label for an id the screen only knows as an id (a `?customer=…` filter restored from
 * the URL, a stored reference) — resolved once per id through a one-off detail call. Async
 * pickers (`SearchableSelect onSearch`) show it as `selectedLabel` until the user picks anew;
 * `known` short-circuits the lookup when the host already has the name (the order's customer).
 * Returns undefined while unresolved or when the lookup fails (the picker then shows the id-less
 * placeholder, never a raw guid).
 */
export function useResolvedLabel(id: string | null | undefined, resolve: LabelResolver, known?: string | null): string | undefined {
  const [resolved, setResolved] = useState<{ id: string; label: string | null } | null>(null)
  const wanted = id ? id : null
  const needsLookup = wanted !== null && !known && resolved?.id !== wanted

  useEffect(() => {
    if (!needsLookup || wanted === null) return
    const controller = new AbortController()
    resolve(wanted, controller.signal)
      .then((label) => {
        if (!controller.signal.aborted) setResolved({ id: wanted, label })
      })
      .catch(() => {
        if (!controller.signal.aborted) setResolved({ id: wanted, label: null })
      })
    return () => controller.abort()
    // The resolver is a module-level function at every call site; re-running on identity
    // changes would refetch on each render for inline arrows.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [needsLookup, wanted])

  if (wanted === null) return undefined
  if (known) return known
  return resolved?.id === wanted ? (resolved.label ?? undefined) : undefined
}
