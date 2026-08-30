import { Button } from '../../../components/ui/Button'
import { useLocale, type TranslateFn } from '../../../i18n/localeContext'
import { fromWireDateTime } from '../../../utils/dates'
import type { TransportOrderDetail, TransportOrderStop } from '../../transport-orders/types'

function stopLine(t: TranslateFn, stop: TransportOrderStop): string {
  return stop.locationName || [stop.address, stop.city].filter(Boolean).join(', ') || stop.city || t('dossiers.route.tbd')
}

/**
 * "12-08 · 08:00–10:00" — compact day + window. C-03: the window is a UTC instant on the wire,
 * so it is projected onto the tenant zone with the same helper the order detail table uses;
 * both surfaces must show the identical hour for the identical stop.
 */
function stopTiming(t: TranslateFn, stop: TransportOrderStop): string | null {
  const from = fromWireDateTime(stop.plannedFrom)
  const to = fromWireDateTime(stop.plannedTo)
  const day = from ?? to
  if (!day) return null
  const [, month, dayOfMonth] = day.date.split('-')
  const time = from && from.time !== '00:00'
    ? (to ? `${from.time}–${to.time}` : from.time)
    : to ? t('dossiers.route.before', { time: to.time }) : null
  return `${dayOfMonth}-${month}${time ? ` · ${time}` : ''}`
}

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
