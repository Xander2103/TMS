import { Fragment } from 'react'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { useLocale } from '../../../i18n/localeContext'
import { formatDateTime } from '../../../utils/dates'
import { STOP_TYPE_LABELS, type TransportOrderStop } from '../types'
import { useOrderDetail } from './orderDetailContext'

function formatWindow(from: string | null, to: string | null): string {
  if (from && to) return `${formatDateTime(from)} – ${formatDateTime(to)}`
  if (from) return `vanaf ${formatDateTime(from)}`
  if (to) return `tot ${formatDateTime(to)}`
  return '—'
}

/** §15 badge ("Vóór 10:00") for a stop's simple time requirement; '' when none. */
function stopTimeRequirementBadge(stop: TransportOrderStop): string {
  const from = stop.timeRequirementFrom?.slice(0, 5)
  const to = stop.timeRequirementTo?.slice(0, 5)
  switch (stop.timeRequirement) {
    case 'Before':
      return to ? `Vóór ${to}` : ''
    case 'After':
      return from ? `Na ${from}` : ''
    case 'Window':
      return from && to ? `${from}–${to}` : ''
    default:
      return ''
  }
}

/** Stops workspace: the route sequence with every planning column, unchanged data and actions. */
export function OrderStopsSection() {
  const { t } = useLocale()
  const { order, planEditable, busy, editing, setPlanStop } = useOrderDetail()

  return (
    <div className="tod-section" id="sectie-stops">
      <section className="tod-panel">
        <h2>
          Stops <span className="tod-card-meta">{order.stops.length} {order.stops.length === 1 ? 'stop' : 'stops'}</span>
        </h2>
        {/* D2: an on-site lifting job is its work site + the work to do there — shown with the stop. */}
        {order.craneJobKind === 'OnSiteLifting' && (
          <p className="tod-muted">
            <strong>{t('stopEditor.crane.workDescription')}:</strong> {order.workDescription || '—'}
          </p>
        )}
        {order.stops.length === 0 && <p className="tod-muted">Nog geen stops ingevuld. Bewerk de opdracht om de route toe te voegen.</p>}
        {order.stops.length > 0 && (
          <div className="tod-table-wrap">
            <table className="to-stops-table tod-table tod-stops-table">
              <thead>
                <tr>
                  <th>#</th>
                  <th>Type</th>
                  <th>Locatie</th>
                  <th>Adres</th>
                  <th>Gepland</th>
                  <th>Tijdseis</th>
                  <th>Gevraagd</th>
                  <th>Bevestigd</th>
                  <th>Afspraak</th>
                  <th>Referentie</th>
                  {planEditable && <th aria-label="Acties" />}
                </tr>
              </thead>
              <tbody>
                {order.stops.map((stop) => (
                  <Fragment key={stop.id}>
                    <tr>
                      <td>{stop.sequence}</td>
                      <td>
                        {/* D2: a work-site stop of an on-site lifting job reads "Werf" — never a loading/unloading stop. */}
                        <Badge tone={stop.stopType === 'Loading' ? 'info' : stop.stopType === 'Site' ? 'warning' : 'success'}>
                          {t(STOP_TYPE_LABELS[stop.stopType])}
                        </Badge>
                      </td>
                      <td title={stop.instructions ?? undefined}>
                        {(stop.warnings?.length ?? 0) > 0 && (
                          <span className="to-stop-warning-marker" title="Waarschuwing openingsuren" aria-label="Waarschuwing">
                            ⚠{' '}
                          </span>
                        )}
                        <strong>{stop.locationName}</strong>
                        {stop.locationCode && <span className="to-loc-code"> ({stop.locationCode})</span>}
                        {(stop.gate || stop.dock) && (
                          <div className="to-stop-site">
                            {[stop.gate ? `Poort: ${stop.gate}` : null, stop.dock ? `Kade/dok: ${stop.dock}` : null]
                              .filter(Boolean)
                              .join(' · ')}
                          </div>
                        )}
                      </td>
                      <td>{[stop.address, [stop.postalCode, stop.city].filter(Boolean).join(' ')].filter(Boolean).join(', ') || '—'}</td>
                      <td>{formatWindow(stop.plannedFrom, stop.plannedTo)}</td>
                      <td>
                        {stopTimeRequirementBadge(stop) ? (
                          <Badge tone="info">{stopTimeRequirementBadge(stop)}</Badge>
                        ) : (
                          '—'
                        )}
                      </td>
                      <td>{formatWindow(stop.requestedFrom, stop.requestedTo)}</td>
                      <td>{formatWindow(stop.confirmedFrom, stop.confirmedTo)}</td>
                      <td>
                        {stop.appointmentRequired ? (
                          <Badge tone="warning">Afspraak{stop.appointmentReference ? ` · ${stop.appointmentReference}` : ''}</Badge>
                        ) : (
                          '—'
                        )}
                      </td>
                      <td>{stop.reference ?? '—'}</td>
                      {planEditable && (
                        <td>
                          <Button variant="ghost" onClick={() => setPlanStop(stop)} disabled={busy || editing}>
                            Venster
                          </Button>
                        </td>
                      )}
                    </tr>
                    {(stop.warnings?.length ?? 0) > 0 && (
                      <tr className="to-stop-warning-row">
                        <td colSpan={planEditable ? 11 : 10}>
                          {stop.warnings!.map((warning, index) => (
                            <p key={index} className="to-stop-warning" role="note">
                              ⚠ {warning}
                            </p>
                          ))}
                        </td>
                      </tr>
                    )}
                  </Fragment>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </div>
  )
}
