import { useCallback, useEffect, useState } from 'react'

function storageKey(userId: string | null): string {
  return `nav.expanded.${userId ?? 'anon'}.v1`
}

function readStored(key: string): string[] | null {
  try {
    const raw = window.localStorage.getItem(key)
    if (!raw) return null
    const parsed = JSON.parse(raw)
    return Array.isArray(parsed) ? (parsed as string[]) : null
  } catch {
    return null
  }
}

/** Stored expanded ids (if any) unioned with the active module, which always starts expanded. */
function seed(key: string, activeModuleId: string | null): Set<string> {
  const stored = readStored(key)
  const base = stored ? new Set(stored) : new Set<string>()
  if (activeModuleId) base.add(activeModuleId)
  return base
}

/**
 * Per-user set of expanded module ids. Persists to localStorage keyed by user id so two
 * accounts on the same browser keep independent state. The active module auto-expands on
 * navigation; the user can still collapse it afterwards until they navigate again.
 *
 * State is adjusted during render (React's "storing information from previous renders"
 * pattern) rather than in an effect, so navigation never triggers a cascading re-render.
 */
export function useExpandedModules(userId: string | null, activeModuleId: string | null) {
  const key = storageKey(userId)
  const [expanded, setExpanded] = useState<Set<string>>(() => seed(key, activeModuleId))
  const [prevKey, setPrevKey] = useState(key)
  const [prevActive, setPrevActive] = useState(activeModuleId)

  if (key !== prevKey) {
    // User switched: re-seed from the new user's storage so we never write one user's
    // state under another's key.
    setPrevKey(key)
    setPrevActive(activeModuleId)
    setExpanded(seed(key, activeModuleId))
  } else if (activeModuleId !== prevActive) {
    // Navigation changed the active module: expand it without collapsing the others.
    setPrevActive(activeModuleId)
    if (activeModuleId) {
      setExpanded((prev) => (prev.has(activeModuleId) ? prev : new Set(prev).add(activeModuleId)))
    }
  }

  // Persist on every change (cheap; the set is tiny). Writing to localStorage is a legit
  // external-system sync, unlike the setState relocated into render above.
  useEffect(() => {
    try {
      window.localStorage.setItem(key, JSON.stringify([...expanded]))
    } catch {
      /* storage unavailable — expansion just won't persist */
    }
  }, [key, expanded])

  const toggle = useCallback((id: string) => {
    setExpanded((prev) => {
      const next = new Set(prev)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })
  }, [])

  return { isExpanded: (id: string) => expanded.has(id), toggle }
}
