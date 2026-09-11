import type { Ref } from 'react'
import { RetainedHeight } from '../../../../components/ui/RetainedHeight'
import { useLocale } from '../../../../i18n/localeContext'
import { useDossierWorkspace } from '../../dossierWorkspace'
import { useRegisterDossierSection } from '../../sectionRegistry'
import { DossierOrderSwitcher } from '../DossierOrderSwitcher'
import { DossierRouteEditor, type DossierRouteEditorHandle } from '../DossierRouteEditor'

/**
 * Route workspace: the existing inline route editor (autocomplete, quick-create, ordered stops,
 * explicit save, dirty state, empty-stop confirmation, version handling) — unchanged — with the
 * transport-order switcher above it and the loading-height guard around it.
 */
export function DossierRouteSection({ editorRef }: { editorRef: Ref<DossierRouteEditorHandle> }) {
  const { t } = useLocale()
  const ws = useDossierWorkspace()
  const register = useRegisterDossierSection('route', { focusField: (field) => ws.focusRouteField(field) })
  const hasRoute = ws.activities.some((a) => a.hasStops)

  return (
    <section id="sectie-route" className="dossier-section dossier-workspace" aria-label={t('dossiers.detail.routeTitle')} ref={register}>
      <h2 tabIndex={-1}>{t('dossiers.detail.routeTitle')}</h2>
      {!hasRoute && <p className="placeholder-text">{t('dossiers.overview.routeNoTransport')}</p>}
      {hasRoute && (
        <>
          <DossierOrderSwitcher
            activities={ws.transportActivities}
            selectedActivityId={ws.routeActivity?.id ?? ''}
            onSelect={ws.selectActivity}
            locked={ws.routeDirty}
          />
          <RetainedHeight retain={ws.firstOrderLoading} className="dossier-section-body">
            <DossierRouteEditor
              ref={editorRef}
              dossier={ws.dossier}
              activity={ws.routeActivity}
              order={ws.firstOrder}
              loading={ws.firstOrderLoading}
              canEdit={ws.canEditRoute}
              canCreateLocations={ws.canCreateLocations}
              onOrderSaved={ws.handleOrderSaved}
              onDossierUpdated={ws.applyDossier}
              onConflict={ws.handleConflict}
              onDirtyChange={ws.setRouteDirty}
              onRetryLoad={ws.retryOrderLoad}
            />
          </RetainedHeight>
        </>
      )}
    </section>
  )
}
