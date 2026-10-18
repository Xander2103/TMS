import { useParams } from 'react-router-dom'
import { NotFoundPage } from '../../../components/feedback/NotFoundPage'
import { RequirePermission } from '../../../routes/RequirePermission'
import { LookupManager } from '../components/LookupManager'
import { findLookupResource } from '../lookupRegistry'

/**
 * Resolves the :resource route segment to a lookup config and renders its manager. SEC-M5: the
 * route is guarded per lookup with the registry's view|manage rights (the same pair the sidebar
 * entry shows it for), so a direct URL answers "Geen toegang" instead of a list that fails to load.
 */
export function LookupPage() {
  const { resource } = useParams<{ resource: string }>()
  const config = findLookupResource(resource)

  if (!config) {
    return <NotFoundPage />
  }

  // key remounts the manager (resetting filters/paging) when switching between lookups.
  return (
    <RequirePermission permissions={[config.viewPermission, config.managePermission]}>
      <LookupManager key={config.slug} config={config} />
    </RequirePermission>
  )
}
