import { useState } from 'react'

export type ListDensity = 'compact' | 'normal'

const PREFIX = 'ts.list.density.'

/** B14: a list's density, remembered per browser and per list (a convenience, never a filter). */
export function useListDensity(listKey: string, fallback: ListDensity = 'normal'): [ListDensity, (value: ListDensity) => void] {
  const [density, setDensity] = useState<ListDensity>(() => {
    try {
      const stored = window.localStorage.getItem(PREFIX + listKey)
      return stored === 'compact' || stored === 'normal' ? stored : fallback
    } catch {
      return fallback
    }
  })
  function update(value: ListDensity) {
    setDensity(value)
    try {
      window.localStorage.setItem(PREFIX + listKey, value)
    } catch {
      /* best-effort convenience */
    }
  }
  return [density, update]
}
