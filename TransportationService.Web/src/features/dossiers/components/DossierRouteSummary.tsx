import { Button } from '../../../components/ui/Button'
import { useLocale } from '../../../i18n/localeContext'
import type { TransportOrderDetail, TransportOrderStop } from '../../transport-orders/types'
import { stopLine, stopTiming } from '../routeStopText'

interface DossierRouteSummaryProps {
  /** First linked order of the dossier; null while loading or when it failed to load. */
  order: TransportOrderDetail | null
  loading: boolean
  canEdit: boolean
  onEdit: () => void
}

/**
 * §11 Route section: two-column Laden/Lossen summary of the first linked order's stops. An
 * on-site lifting job (D2) has no route — it shows its work site ("Werf") and the work instead,
 * never an empty Laden/Lossen pair that would read as missing data.
 */
export function DossierRouteSummary({ order, loading, canEdit, onEdit }: DossierRouteSummaryProps) {
  const { t } = useLocale()
  const loadingStops = order?.stops.filter((s) => s.stopType === 'Loading') ?? []
  const unloadingStops = order?.stops.filter((s) => s.stopType === 'Unloading') ?? []
  const siteStops = order?.stops.filter((s) => s.stopType === 'Site') ?? []
  const onSite = order?.craneJobKind === 'OnSiteLifting' || (siteStops.length > 0 && loadingStops.length + unloadingStops.length === 0)

  const stopList = (stops: TransportOrderStop[]) => (
    <>
      {stops.length === 0 && <p className="placeholder-text">{t('dossiers.route.tbd')}</p>}
      {stops.map((stop) => (
        <p key={stop.id} className="dossier-route-stop">
          {stopLine(t, stop)}
          {stopTiming(t, stop) && <span className="dossier-route-timing">{stopTiming(t, stop)}</span>}
        </p>
      ))}
    </>
  )

  return (
    <>
      {loading && <p className="placeholder-text">{t('dossiers.route.loading')}</p>}
      {!loading && !order && <p className="placeholder-text">{t('dossiers.route.loadFailed')}</p>}
      {!loading && order && onSite && (
        <div className="dossier-route-columns">
          <div>
            <h3>{t('stopEditor.stopType.Site')}</h3>
            {stopList(siteStops)}
          </div>
          <div>
            <h3>{t('stopEditor.crane.workDescription')}</h3>
            <p className={order.workDescription ? 'dossier-route-stop' : 'placeholder-text'}>
              {order.workDescription || t('dossiers.route.tbd')}
            </p>
          </div>
        </div>
      )}
      {!loading && order && !onSite && (
        <div className="dossier-route-columns">
          <div>
            <h3>{t('orders.stopType.Loading')}</h3>
            {stopList(loadingStops)}
          </div>
          <div>
            <h3>{t('orders.stopType.Unloading')}</h3>
            {stopList(unloadingStops)}
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
