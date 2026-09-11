import { Button } from '../../../components/ui/Button'
import { useLocale } from '../../../i18n/localeContext'
import type { TransportOrderDetail } from '../../transport-orders/types'
import { stopLine, stopTiming } from '../routeStopText'

interface DossierRouteSummaryProps {
  /** First linked order of the dossier; null while loading or when it failed to load. */
  order: TransportOrderDetail | null
  loading: boolean
  canEdit: boolean
  onEdit: () => void
}

/** §11 Route section: two-column Laden/Lossen summary of the first linked order's stops. */
export function DossierRouteSummary({ order, loading, canEdit, onEdit }: DossierRouteSummaryProps) {
  const { t } = useLocale()
  const loadingStops = order?.stops.filter((s) => s.stopType === 'Loading') ?? []
  const unloadingStops = order?.stops.filter((s) => s.stopType === 'Unloading') ?? []

  return (
    <>
      {loading && <p className="placeholder-text">{t('dossiers.route.loading')}</p>}
      {!loading && !order && <p className="placeholder-text">{t('dossiers.route.loadFailed')}</p>}
      {!loading && order && (
        <div className="dossier-route-columns">
          <div>
            <h3>{t('orders.stopType.Loading')}</h3>
            {loadingStops.length === 0 && <p className="placeholder-text">{t('dossiers.route.tbd')}</p>}
            {loadingStops.map((stop) => (
              <p key={stop.id} className="dossier-route-stop">
                {stopLine(t, stop)}
                {stopTiming(t, stop) && <span className="dossier-route-timing">{stopTiming(t, stop)}</span>}
              </p>
            ))}
          </div>
          <div>
            <h3>{t('orders.stopType.Unloading')}</h3>
            {unloadingStops.length === 0 && <p className="placeholder-text">{t('dossiers.route.tbd')}</p>}
            {unloadingStops.map((stop) => (
              <p key={stop.id} className="dossier-route-stop">
                {stopLine(t, stop)}
                {stopTiming(t, stop) && <span className="dossier-route-timing">{stopTiming(t, stop)}</span>}
              </p>
            ))}
          </div>
        </div>
      )}
      {canEdit && order && (
        <p>
          <Button variant="secondary" onClick={onEdit}>
            {t('dossiers.route.edit')}
          </Button>
        </p>
      )}
    </>
  )
}
