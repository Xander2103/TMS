import { useCallback, useState } from 'react'

/**
 * Tracks the active section of a {@link SectionedForm} in local component state.
 *
 * Section switching is *internal form navigation*, not application navigation: it must never
 * mutate the router (no `useSearchParams`/`navigate`), because a dirty form's
 * {@link UnsavedChangesGuard} blocks router navigations and would otherwise pop the
 * "unsaved changes" modal on every section switch. Field values stay lifted in the parent
 * form component, so switching sections never loses data.
 */
export function useSectionNavigation(ids: string[], defaultId: string) {
  const [activeId, setActiveId] = useState(defaultId)
  // Clamp on read so a section removed between renders (e.g. mode change) falls back cleanly.
  const current = ids.includes(activeId) ? activeId : defaultId

  const setActive = useCallback(
    (id: string) => {
      if (ids.includes(id)) setActiveId(id)
    },
    [ids],
  )

  return { activeId: current, setActive }
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
