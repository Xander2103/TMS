import { useParams } from 'react-router-dom'
import { NotFoundPage } from '../../../components/feedback/NotFoundPage'
import { LookupManager } from '../components/LookupManager'
import { findLookupResource } from '../lookupRegistry'

/** Resolves the :resource route segment to a lookup config and renders its manager. */
export function LookupPage() {
  const { resource } = useParams<{ resource: string }>()
  const config = findLookupResource(resource)

  if (!config) {
    return <NotFoundPage />
  }

  // key remounts the manager (resetting filters/paging) when switching between lookups.
  return <LookupManager key={config.slug} config={config} />
}
