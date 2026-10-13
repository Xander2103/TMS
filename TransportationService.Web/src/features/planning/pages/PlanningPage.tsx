import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import { useLocale } from '../../../i18n/localeContext'
import { createTrip, getPlanningProposals, listTrips, type PlanningProposals, type TourProposal } from '../api/planningApi'
import { describeApiError } from '../../../api/problemDetails'
import { TRIP_STATUS_LABELS, TRIP_STATUS_TONE, TRIP_STATUSES, type TripListItem, type TripStatus } from '../types'
import './planning.css'

function todayIso(): string {
  return new Date().toISOString().slice(0, 10)
}

function shiftDate(iso: string, days: number): string {
  const date = new Date(`${iso}T00:00:00Z`)
  date.setUTCDate(date.getUTCDate() + days)
  return date.toISOString().slice(0, 10)
}

/** Day-based planning board: all trips of the selected date with live conflict counts. */
export function PlanningPage() {
  const navigate = useNavigate()
  const { hasPermission } = useAuth()
  const { showError } = useToast()
  const { t } = useLocale()

  const [date, setDate] = useState(todayIso)
  const [statusFilter, setStatusFilter] = useState<TripStatus | ''>('')
  const [trips, setTrips] = useState<TripListItem[] | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [creating, setCreating] = useState(false)

  useEffect(() => {
    let mounted = true
    listTrips({ from: date, to: date, status: statusFilter || undefined })
      .then((data) => {
        if (!mounted) return
        setTrips(data)
        setLoadError(null)
      })
      .catch(() => {
        if (mounted) setLoadError(t('planning.list.loadError'))
      })
    return () => {
      mounted = false
    }
  }, [date, statusFilter, t])

  async function handleNewTrip() {
    setCreating(true)
    try {
      const trip = await createTrip({
        tripDate: date,
        driverId: null,
        vehicleId: null,
        trailerId: null,
        plannedStart: null,
        plannedEnd: null,
        notes: null,
        orderIds: [],
        plannedDistanceKm: null,
        plannedEmptyKm: null,
      })
      navigate(`/planning/${trip.id}`)
    } catch {
      showError(t('planning.list.createFailed'))
    } finally {
      setCreating(false)
    }
  }

  return (
    <div>
      <Breadcrumbs items={[{ label: t('planning.title') }]} />
      <PageHeader
        title={t('planning.title')}
        action={
          hasPermission('planning.create') ? (
            <Button onClick={() => void handleNewTrip()} disabled={creating}>
              {creating ? t('planning.list.busy') : t('planning.list.newTrip')}
            </Button>
          ) : undefined
        }
      />

      <div className="pl-toolbar">
        <div className="pl-datenav">
          <button type="button" className="pl-nav-btn" onClick={() => setDate((d) => shiftDate(d, -1))} aria-label={t('planning.list.previousDay')}>
            ←
          </button>
          <input type="date" value={date} onChange={(e) => setDate(e.target.value)} aria-label={t('planning.list.dateLabel')} />
          <button type="button" className="pl-nav-btn" onClick={() => setDate((d) => shiftDate(d, 1))} aria-label={t('planning.list.nextDay')}>
            →
          </button>
          <Button variant="secondary" onClick={() => setDate(todayIso())}>
            {t('planning.list.today')}
          </Button>
        </div>
        <select
          value={statusFilter}
          onChange={(e) => setStatusFilter(e.target.value as TripStatus | '')}
          aria-label={t('planning.list.statusFilterLabel')}
        >
          <option value="">{t('planning.list.allStatuses')}</option>
          {TRIP_STATUSES.map((status) => (
            <option key={status} value={status}>
              {t(TRIP_STATUS_LABELS[status])}
            </option>
          ))}
        </select>
      </div>

      {loadError && <p className="placeholder-text">{loadError}</p>}
      {!loadError && trips === null && <p className="placeholder-text">{t('planning.list.loading')}</p>}
      {!loadError && trips !== null && trips.length === 0 && (
        <p className="placeholder-text">{t('planning.list.empty')}</p>
      )}

      {!loadError && trips !== null && trips.length > 0 && (
        <table className="pl-table">
          <thead>
            <tr>
              <th>{t('planning.list.colTrip')}</th>
              <th>{t('planning.list.colDriver')}</th>
              <th>{t('planning.list.colVehicle')}</th>
              <th>{t('planning.list.colTrailer')}</th>
              <th>{t('planning.list.colOrders')}</th>
              <th>{t('planning.list.colStatus')}</th>
              <th>{t('planning.list.colConflicts')}</th>
            </tr>
          </thead>
          <tbody>
            {trips.map((trip) => (
              <tr key={trip.id} className="pl-row" onClick={() => navigate(`/planning/${trip.id}`)}>
                <td>
                  <code>{trip.tripNumber}</code>
                </td>
                <td>{trip.driverName ?? '—'}</td>
                <td>{trip.vehicleNumber ? `${trip.vehicleNumber} (${trip.vehicleLicensePlate})` : '—'}</td>
                <td>{trip.trailerNumber ?? '—'}</td>
                <td>{trip.orderCount}</td>
                <td>
                  <Badge tone={TRIP_STATUS_TONE[trip.status]}>{t(TRIP_STATUS_LABELS[trip.status])}</Badge>
                </td>
                <td>
                  {trip.blockingConflictCount > 0 ? (
                    <Badge tone="danger">⚠ {trip.blockingConflictCount}</Badge>
                  ) : (
                    <Badge tone="success">{t('planning.list.conflictsOk')}</Badge>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      <TourProposalsPanel />
    </div>
  )
}

/**
 * Wave 7: transparante ritvoorstellen — te plannen orders per leverzone (zelfde zoneconcept
 * als de prijzen), achterstand eerst, elke uitsluiting met reden. "Maak rit" gebruikt de
 * gewone rit-aanmaak, dus alle conflict- en rechtenlogica geldt onverkort.
 */
function TourProposalsPanel() {
  const navigate = useNavigate()
  const { hasPermission } = useAuth()
  const { showSuccess, showError } = useToast()
  const { t } = useLocale()
  const canCreate = hasPermission('planning.create')

  const [date, setDate] = useState(() => new Date().toISOString().slice(0, 10))
  const [proposals, setProposals] = useState<PlanningProposals | null>(null)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    getPlanningProposals(date)
      .then(setProposals)
      .catch(() => setProposals(null))
  }, [date])

  async function accept(proposal: TourProposal) {
    setBusy(true)
    try {
      const trip = await createTrip({
        tripDate: date, driverId: null, vehicleId: null, trailerId: null,
        plannedStart: null, plannedEnd: null,
        notes: t('planning.proposals.noteTemplate', { zone: proposal.zoneCode }),
        orderIds: proposal.orders.map((o) => o.transportOrderId),
        plannedDistanceKm: null, plannedEmptyKm: null,
      })
      showSuccess(t('planning.proposals.created', { tripNumber: trip.tripNumber, count: proposal.orders.length }))
      navigate(`/trips/${trip.id}`)
    } catch (err) {
      showError(describeApiError(err, t('planning.list.createFailed')).message)
    } finally {
      setBusy(false)
    }
  }

  return (
    <section className="ui-form-section">
      <div className="wh-trace-bar">
        <h2 style={{ margin: 0 }}>{t('planning.proposals.title')}</h2>
        <input type="date" value={date} onChange={(e) => setDate(e.target.value)} aria-label={t('planning.proposals.dateLabel')} />
      </div>
      {proposals === null && <p className="placeholder-text">{t('planning.proposals.loading')}</p>}
      {proposals !== null && proposals.proposals.length === 0 && (
        <p className="placeholder-text">{t('planning.proposals.empty')}</p>
      )}
      {proposals?.proposals.map((proposal) => (
        <div key={proposal.zoneCode + proposal.zoneName} className="wh-card">
          <div className="wh-card-head">
            <div>
              <h3 style={{ margin: 0 }}>
                {proposal.zoneName} {proposal.zoneCode !== '—' && <code>{proposal.zoneCode}</code>}
              </h3>
              <p className="wh-muted">
                {t('planning.proposals.orders', { count: proposal.orders.length })} · {proposal.totalWeightKg.toFixed(0)} kg
                {proposal.totalLoadingMeters > 0 && ` · ${proposal.totalLoadingMeters.toFixed(1)} ldm`}
                {proposal.totalPallets > 0 && ` · ${t('planning.proposals.pallets', { count: proposal.totalPallets })}`}
              </p>
            </div>
            {canCreate && proposal.zoneCode !== '—' && (
              <Button variant="secondary" disabled={busy} onClick={() => void accept(proposal)}>
                {t('planning.proposals.makeTrip')}
              </Button>
            )}
          </div>
          {proposal.explanations.map((line, index) => (
            <p key={index} className="wh-muted">{line}</p>
          ))}
          <div>
            {proposal.orders.map((order) => (
              <div key={order.transportOrderId} style={{ marginBottom: 4 }}>
                {order.overdue && <Badge tone="warning">{t('planning.proposals.overdue')}</Badge>}{' '}
                <code>{order.orderNumber}</code> {order.deliveryCity ?? ''} {order.deliveryPostalCode ?? ''}
                {order.constraints.map((constraint, index) => (
                  <p key={index} className="wh-muted" style={{ margin: '2px 0 0 16px' }}>
                    <Badge tone="warning">{t('planning.proposals.constraint')}</Badge> {constraint}
                  </p>
                ))}
              </div>
            ))}
          </div>
        </div>
      ))}
      {proposals !== null && proposals.excluded.length > 0 && (
        <div className="wh-card">
          <h3 style={{ margin: 0 }}>{t('planning.proposals.notProposed')}</h3>
          {proposals.excluded.map((reason, index) => (
            <p key={index} className="wh-muted">{reason}</p>
          ))}
        </div>
      )}
    </section>
  )
}
