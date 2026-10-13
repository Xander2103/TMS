import { useCallback, useEffect, useMemo, useRef, type ReactNode } from 'react'
import { DossierSectionRegistryContext, type DossierSectionApi, type DossierSectionId } from '../sectionRegistry'

const HIGHLIGHT_MS = 1600

/**
 * Typed section registry for the dossier page: replaces `document.getElementById(...)` jumps.
 * Every section registers its root element (and optionally an opener + field focuser); the
 * Attention panel navigates through `goTo(section, field)`, which opens, scrolls, focuses the
 * exact control and flashes a short destination highlight. Works for mouse and keyboard.
 */
export function DossierSectionsProvider({ children }: { children: ReactNode }) {
  const sections = useRef(new Map<DossierSectionId, DossierSectionApi>())
  const highlightTimer = useRef<number | null>(null)

  const register = useCallback((id: DossierSectionId, api: DossierSectionApi) => {
    sections.current.set(id, api)
    return () => {
      if (sections.current.get(id) === api) sections.current.delete(id)
    }
  }, [])

  const goTo = useCallback((id: DossierSectionId, field: string | null = null) => {
    const api = sections.current.get(id)
    if (!api) return false
    api.open?.()
    api.element.scrollIntoView?.({ behavior: 'smooth', block: 'start' })
    const handled = api.focusField?.(field) ?? false
    if (!handled) {
      const heading = api.element.querySelector<HTMLElement>('h2, summary') ?? api.element
      if (!heading.hasAttribute('tabindex')) heading.tabIndex = -1
      heading.focus({ preventScroll: true })
    }
    for (const other of sections.current.values()) other.element.removeAttribute('data-highlight')
    api.element.setAttribute('data-highlight', '')
    if (highlightTimer.current) window.clearTimeout(highlightTimer.current)
    highlightTimer.current = window.setTimeout(() => api.element.removeAttribute('data-highlight'), HIGHLIGHT_MS)
    return true
  }, [])

  useEffect(
    () => () => {
      if (highlightTimer.current) window.clearTimeout(highlightTimer.current)
    },
    [],
  )

  const value = useMemo(() => ({ register, goTo }), [register, goTo])
  return <DossierSectionRegistryContext.Provider value={value}>{children}</DossierSectionRegistryContext.Provider>
}
