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

function seed(key: string, activeModuleId: string | null): Set<string> {
  const stored = readStored(key)
  if (stored) return new Set(stored)
  return new Set(activeModuleId ? [activeModuleId] : [])
}

/**
 * Per-user set of expanded module ids. Persists to localStorage keyed by user id so two
 * accounts on the same browser keep independent state. The active module always expands.
 */
export function useExpandedModules(userId: string | null, activeModuleId: string | null) {
  const key = storageKey(userId)
  const [expanded, setExpanded] = useState<Set<string>>(() => seed(key, activeModuleId))
  const [prevKey, setPrevKey] = useState(key)

  // Re-seed synchronously when the user (key) changes — before any effect runs — so the
  // persist effect can never write one user's expand state under another user's key.
  if (key !== prevKey) {
    setPrevKey(key)
    setExpanded(seed(key, activeModuleId))
  }

  // Persist on every change (cheap; the set is tiny).
  useEffect(() => {
    try {
      window.localStorage.setItem(key, JSON.stringify([...expanded]))
    } catch {
      /* storage unavailable — expansion just won't persist */
    }
  }, [key, expanded])

  // Auto-expand the active module when navigation changes it. Same-ref return avoids churn.
  useEffect(() => {
    if (!activeModuleId) return
    setExpanded((prev) => (prev.has(activeModuleId) ? prev : new Set(prev).add(activeModuleId)))
  }, [activeModuleId])

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
