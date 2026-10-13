import { createContext, useCallback, useContext, useEffect, useRef } from 'react'
import type { ReadinessSection } from './types'

export type DossierSectionId = ReadinessSection | 'documenten' | 'notities' | 'meer'

/**
 * What a section offers to the navigator. `focusField` receives the readiness `field` key
 * (e.g. "stops.loading", "stops.plannedFrom", "price") and returns true when it moved focus
 * to a matching control; the navigator then skips the generic heading focus.
 */
export interface DossierSectionApi {
  element: HTMLElement
  /** Opens a collapsed container (details) before scrolling. */
  open?: () => void
  /** Moves keyboard focus to the control that resolves `field`; return false when not handled. */
  focusField?: (field: string | null) => boolean
}

export interface DossierSectionRegistry {
  register: (id: DossierSectionId, api: DossierSectionApi) => () => void
  goTo: (id: DossierSectionId, field?: string | null) => boolean
}

/** Provided by `DossierSectionsProvider`; null outside the dossier page. */
export const DossierSectionRegistryContext = createContext<DossierSectionRegistry | null>(null)

/** Navigator for Attention actions and menu jumps. Outside the provider it is a no-op. */
export function useDossierNavigator(): DossierSectionRegistry['goTo'] {
  const registry = useContext(DossierSectionRegistryContext)
  return registry?.goTo ?? (() => false)
}

/**
 * Registers a section root. The optional `open`/`focusField` handlers are read through a ref at
 * call time, so a re-render never re-registers; the returned callback ref goes on the section
 * element.
 */
export function useRegisterDossierSection(
  id: DossierSectionId,
  handlers: Pick<DossierSectionApi, 'open' | 'focusField'> = {},
): (element: HTMLElement | null) => void {
  const registry = useContext(DossierSectionRegistryContext)
  const handlersRef = useRef(handlers)
  useEffect(() => {
    handlersRef.current = handlers
  })
  const unregister = useRef<(() => void) | null>(null)

  return useCallback(
    (element: HTMLElement | null) => {
      unregister.current?.()
      unregister.current = null
      if (!element || !registry) return
      unregister.current = registry.register(id, {
        element,
        open: () => handlersRef.current.open?.(),
        focusField: (field) => handlersRef.current.focusField?.(field) ?? false,
      })
    },
    [id, registry],
  )
}
