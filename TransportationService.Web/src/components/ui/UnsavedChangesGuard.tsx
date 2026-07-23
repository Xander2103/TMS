import { useCallback, useEffect } from 'react'
import { useBlocker } from 'react-router-dom'
import { ConfirmDialog } from './ConfirmDialog'

/**
 * Blocks in-app navigation and tab close while a form has unsaved changes.
 * Requires the data router (createBrowserRouter) set up in AppRoutes.
 *
 * Only blocks navigations that change the **pathname** (leaving the page). Same-page
 * changes to the search/hash — e.g. a form syncing UI state to a query parameter — are not
 * "leaving the page" and must proceed without the modal.
 */
export function UnsavedChangesGuard({ when }: { when: boolean }) {
  const blocker = useBlocker(
    useCallback(
      ({ currentLocation, nextLocation }) =>
        when && currentLocation.pathname !== nextLocation.pathname,
      [when],
    ),
  )

  useEffect(() => {
    if (!when) return
    function handleBeforeUnload(event: BeforeUnloadEvent) {
      event.preventDefault()
    }
    window.addEventListener('beforeunload', handleBeforeUnload)
    return () => window.removeEventListener('beforeunload', handleBeforeUnload)
  }, [when])

  if (blocker.state !== 'blocked') return null

  return (
    <ConfirmDialog
      title="Niet-opgeslagen wijzigingen"
      message="Je hebt wijzigingen die nog niet zijn opgeslagen. Weet je zeker dat je deze pagina wilt verlaten?"
      confirmLabel="Pagina verlaten"
      cancelLabel="Blijven bewerken"
      destructive
      onConfirm={() => blocker.proceed()}
      onCancel={() => blocker.reset()}
    />
  )
}
