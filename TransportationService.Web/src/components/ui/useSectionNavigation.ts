import { useCallback } from 'react'
import { useSearchParams } from 'react-router-dom'

const DEFAULT_PARAM = 'section'

/**
 * Tracks the active section of a {@link SectionedForm}, syncing it to a `?section=` URL
 * parameter so a section is deep-linkable and survives navigation. Active-section state
 * lives in the URL only — the form's field values stay lifted in the parent component,
 * so switching sections never loses data.
 */
export function useSectionNavigation(
  ids: string[],
  defaultId: string,
  opts?: { paramKey?: string },
) {
  const paramKey = opts?.paramKey ?? DEFAULT_PARAM
  const [params, setParams] = useSearchParams()
  const fromUrl = params.get(paramKey)
  const activeId = fromUrl && ids.includes(fromUrl) ? fromUrl : defaultId

  const setActive = useCallback(
    (id: string) => {
      if (!ids.includes(id)) return
      setParams(
        (prev) => {
          const next = new URLSearchParams(prev)
          next.set(paramKey, id)
          return next
        },
        { replace: true },
      )
    },
    [ids, paramKey, setParams],
  )

  return { activeId, setActive }
}

/**
 * Returns the id of the first section that owns a failing field, or null when there are
 * no errors. Drives first-error routing after a failed submit.
 */
export function firstSectionWithError(
  sections: { id: string; fieldKeys?: string[] }[],
  fieldErrors: Record<string, string> | null | undefined,
): string | null {
  if (!fieldErrors) return null
  const keys = Object.keys(fieldErrors)
  if (keys.length === 0) return null
  for (const section of sections) {
    if (section.fieldKeys?.some((k) => keys.includes(k))) return section.id
  }
  return null
}
