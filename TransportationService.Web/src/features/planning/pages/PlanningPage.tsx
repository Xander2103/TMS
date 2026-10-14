import { useEffect, useMemo, useState, type ReactNode } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { SearchableSelect } from '../../../components/ui/SearchableSelect'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import { useLocale } from '../../../i18n/localeContext'
import { createTrip, getPlanningProposals, listTrips, type PlanningProposals, type TourProposal } from '../api/planningApi'
import { describeApiError } from '../../../api/problemDetails'
import { TRIP_STATUS_LABELS, TRIP_STATUS_TONE, TRIP_STATUSES, type TripListItem, type TripStatus } from '../types'
import { buildDriverOptions } from '../resourceOptions'
import { usePlanningResources } from '../usePlanningResources'
import { addDaysIso, groupTrips, localIsoDate, OVERDUE_LOOKBACK_DAYS } from '../tripGroups'
import { formatDate } from '../../../utils/dates'
import { formatInteger } from '../../../utils/numbers'
import './planning.css'

function todayIso(): string {
  return localIsoDate()
}

function shiftDate(iso: string, days: number): string {
  return addDaysIso(iso, days)
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
  // B20: the operational groups load independently of the chosen day.
  const [running, setRunning] = useState<TripListItem[]>([])
  const [past, setPast] = useState<TripListItem[]>([])
  const [loadError, setLoadError] = useState<string | null>(null)
  const [creating, setCreating] = useState(false)
  const today = todayIso()

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

  useEffect(() => {
    let mounted = true
    Promise.all([
      listTrips({ status: 'InProgress' }).catch(() => [] as TripListItem[]),
      listTrips({ from: addDaysIso(today, -OVERDUE_LOOKBACK_DAYS), to: addDaysIso(today, -1) }).catch(() => [] as TripListItem[]),
    ]).then(([runningTrips, pastTrips]) => {
      if (!mounted) return
      setRunning(runningTrips)
      setPast(pastTrips)
    })
    return () => {
      mounted = false
    }
  }, [today])

  const groups = useMemo(() => groupTrips(trips ?? [], running, past, today), [trips, running, past, today])
  const dayLabel = date === today ? t('planning.groups.today') : formatDate(date)

  // B06: a new trip is created deliberately — date (defaults to the shown day) and an optional
  // driver are asked first; cancelling creates nothing and a double click creates one trip.
  const [newTripOpen, setNewTripOpen] = useState(false)
  const [newTripDate, setNewTripDate] = useState(date)
  const [newTripDriverId, setNewTripDriverId] = useState<string | null>(null)
  const { drivers, status: resourceStatus } = usePlanningResources(newTripOpen)
  const driverOptions = useMemo(() => buildDriverOptions(drivers, t), [drivers, t])

  function openNewTrip() {
    setNewTripDate(date)
    setNewTripDriverId(null)
    setNewTripOpen(true)
  }

  async function handleNewTrip() {
    if (creating || !newTripDate) return
    setCreating(true)
    try {
      const trip = await createTrip({
        tripDate: newTripDate,
        driverId: newTripDriverId,
        vehicleId: null,
        trailerId: null,
        plannedStart: null,
        plannedEnd: null,
        notes: null,
        orderIds: [],
        plannedDistanceKm: null,
        plannedEmptyKm: null,
      })
      setNewTripOpen(false)
      navigate(`/planning/${trip.id}`)
    } catch (err) {
      showError(describeApiError(err, t('planning.list.createFailed')).message)
    } finally {
      setCreating(false)
    }
  }

  return (
    <div>
      <PageHeader
        title={t('planning.title')}
        action={
          hasPermission('planning.create') ? (
            <Button onClick={openNewTrip} disabled={creating}>
              {t('planning.list.newTrip')}
            </Button>
          ) : undefined
        }
      />

      {newTripOpen && (
        <Modal
          title={t('planning.list.newTripTitle')}
          onClose={() => setNewTripOpen(false)}
          busy={creating}
          footer={
            <>
              <Button variant="secondary" onClick={() => setNewTripOpen(false)} disabled={creating}>
                {t('ui.actions.cancel')}
              </Button>
              <Button onClick={() => void handleNewTrip()} disabled={creating || !newTripDate}>
                {creating ? t('planning.list.busy') : t('planning.list.createTrip')}
              </Button>
            </>
          }
        >
          <FormField label={t('planning.list.dateLabel')} htmlFor="new-trip-date">
            <input id="new-trip-date" type="date" value={newTripDate} onChange={(e) => setNewTripDate(e.target.value)} disabled={creating} />
          </FormField>
          <FormField label={t('planning.list.driverLabel')} htmlFor="new-trip-driver" hint={t('planning.list.driverOptional')}>
            <SearchableSelect
              id="new-trip-driver"
              value={newTripDriverId}
              onChange={setNewTripDriverId}
              options={driverOptions}
              isLoading={resourceStatus.drivers === 'loading'}
              errorMessage={resourceStatus.drivers === 'error' ? t('planning.detail.driversLoadError') : null}
              placeholder={t('planning.list.driverPlaceholder')}
              disabled={creating}
            />
          </FormField>
        </Modal>
      )}

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

      {!loadError && trips !== null && (
        <table className="pl-table pl-grouped">
          <thead>
            <tr>
              <th>{t('planning.list.colTrip')}</th>
              <th>{t('planning.list.colDate')}</th>
              <th>{t('planning.list.colDriver')}</th>
              <th>{t('planning.list.colVehicle')}</th>
              <th>{t('planning.list.colTrailer')}</th>
              <th className="pl-num">{t('planning.list.colOrders')}</th>
              <th>{t('planning.list.colStatus')}</th>
              <th>{t('planning.list.colConflicts')}</th>
            </tr>
          </thead>
          {groups.running.length > 0 && (
            <TripGroup title={t('planning.groups.running')} hint={t('planning.groups.runningHint')} tone="running" count={groups.running.length}>
              {groups.running.map(({ trip, startedBefore }) => (
                <TripRow key={trip.id} trip={trip} note={startedBefore ? t('planning.groups.startedOn', { date: formatDate(trip.tripDate) }) : undefined} />
              ))}
            </TripGroup>
          )}
          {groups.overdue.length > 0 && (
            <TripGroup title={t('planning.groups.overdue')} hint={t('planning.groups.overdueHint', { count: OVERDUE_LOOKBACK_DAYS })} tone="overdue" count={groups.overdue.length}>
              {groups.overdue.map((trip) => (
                <TripRow key={trip.id} trip={trip} note={t('planning.groups.notDeparted')} />
              ))}
            </TripGroup>
          )}
          <TripGroup title={dayLabel} tone="day" count={groups.day.length}>
            {groups.day.map((trip) => (
              <TripRow key={trip.id} trip={trip} />
            ))}
            {groups.day.length === 0 && (
              <tr className="pl-empty-row">
                <td colSpan={8}>{groups.movedUp > 0 ? t('planning.groups.dayOnlyAbove') : t('planning.list.empty')}</td>
              </tr>
            )}
            {groups.day.length > 0 && groups.movedUp > 0 && (
              <tr className="pl-empty-row">
                <td colSpan={8}>{t('planning.groups.movedUp', { count: groups.movedUp })}</td>
              </tr>
            )}
          </TripGroup>
        </table>
      )}

      <TourProposalsPanel date={date} />
    </div>
  )
}

/** One group of the Ritlijst: a header row with its name, count and what it means. */
function TripGroup({ title, hint, tone, count, children }: { title: string; hint?: string; tone: 'running' | 'overdue' | 'day'; count: number; children: ReactNode }) {
  return (
    <tbody className={`pl-group pl-group-${tone}`}>
      <tr className="pl-group-head">
        <th colSpan={8} scope="rowgroup">
          <span className="pl-group-title">{title}</span>
          <span className="pl-group-count">{count}</span>
          {hint && <span className="pl-group-hint">{hint}</span>}
        </th>
      </tr>
      {children}
    </tbody>
  )
}

function TripRow({ trip, note }: { trip: TripListItem; note?: string }) {
  const navigate = useNavigate()
  const { t } = useLocale()
  return (
    <tr className="pl-row" onClick={() => navigate(`/planning/${trip.id}`)}>
      <td>
        <Link to={`/planning/${trip.id}`} className="pl-trip-link" onClick={(event) => event.stopPropagation()}>
          {trip.tripNumber}
        </Link>
        {note && <span className="pl-row-note">{note}</span>}
      </td>
      <td className="pl-nowrap">{formatDate(trip.tripDate)}</td>
      <td>{trip.driverName ?? <span className="pl-missing">{t('planning.list.noDriver')}</span>}</td>
      <td>{trip.vehicleNumber ? `${trip.vehicleNumber} (${trip.vehicleLicensePlate})` : <span className="pl-missing">—</span>}</td>
      <td>{trip.trailerNumber ?? '—'}</td>
      <td className="pl-num">{trip.orderCount}</td>
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
  )
}

/**
 * B20 — ritvoorstellen as an actual suggestion list for the chosen day: per delivery zone WHAT is
 * suggested (n orders to combine), WHY (zone, weight, overdue work) and ONE action ("Maak rit",
 * through the normal trip creation, so every conflict and permission rule applies). The order
 * list and the explanations fold open on demand. Data gaps that keep an order out of a proposal
 * (missing unloading stop, no zone) are only shown to users who can fix the order.
 */
function TourProposalsPanel({ date }: { date: string }) {
  const navigate = useNavigate()
  const { hasPermission, hasAnyPermission } = useAuth()
  const { showSuccess, showError } = useToast()
  const { t } = useLocale()
  const canCreate = hasPermission('planning.create')
  const canFixOrders = hasAnyPermission(['orders.edit', 'orders.manage'])

  const [proposals, setProposals] = useState<PlanningProposals | null>(null)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    let mounted = true
    getPlanningProposals(date)
      .then((data) => mounted && setProposals(data))
      .catch(() => mounted && setProposals(null))
    return () => {
      mounted = false
    }
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
      navigate(`/planning/${trip.id}`)
    } catch (err) {
      showError(describeApiError(err, t('planning.list.createFailed')).message)
    } finally {
      setBusy(false)
    }
  }

  // Only proposals that can become a trip (a known zone) are suggestions; the rest is data to fix.
  const suggestions = (proposals?.proposals ?? []).filter((proposal) => proposal.zoneCode !== '—')
  const unzoned = (proposals?.proposals ?? []).filter((proposal) => proposal.zoneCode === '—')
  const gaps = [
    ...(proposals?.excluded ?? []),
    ...unzoned.flatMap((proposal) => proposal.orders.map((order) => t('planning.proposals.noZone', { order: order.orderNumber }))),
  ]
  if (proposals === null || (suggestions.length === 0 && (!canFixOrders || gaps.length === 0))) return null

  return (
    <section className="pl-proposals" aria-labelledby="pl-proposals-title">
      <h2 id="pl-proposals-title">{t('planning.proposals.title')}</h2>
      <p className="pl-proposals-intro">{t('planning.proposals.intro', { date: formatDate(date) })}</p>
      {suggestions.length > 0 && (
        <ul className="pl-suggestions">
          {suggestions.map((proposal) => {
            const overdue = proposal.orders.filter((order) => order.overdue).length
            const constrained = proposal.orders.filter((order) => order.constraints.length > 0).length
            return (
              <li key={proposal.zoneCode + proposal.zoneName} className="pl-suggestion">
                <div className="pl-suggestion-main">
                  <strong>{proposal.zoneName}</strong>
                  <span className="pl-suggestion-what">
                    {t('planning.proposals.combine', { count: proposal.orders.length })}
                    {' · '}{formatInteger(proposal.totalWeightKg)} kg
                    {proposal.totalLoadingMeters > 0 && ` · ${proposal.totalLoadingMeters.toFixed(1)} ldm`}
                    {proposal.totalPallets > 0 && ` · ${t('planning.proposals.pallets', { count: proposal.totalPallets })}`}
                  </span>
                  {(overdue > 0 || constrained > 0) && (
                    <span className="pl-suggestion-flags">
                      {overdue > 0 && <Badge tone="warning">{t('planning.proposals.overdueCount', { count: overdue })}</Badge>}
                      {constrained > 0 && <Badge tone="info">{t('planning.proposals.constraintCount', { count: constrained })}</Badge>}
                    </span>
                  )}
                </div>
                {canCreate && (
                  <Button variant="secondary" disabled={busy} onClick={() => void accept(proposal)}>
                    {t('planning.proposals.makeTrip')}
                  </Button>
                )}
                <details className="pl-suggestion-details">
                  <summary>{t('planning.proposals.why')}</summary>
                  {proposal.explanations.map((line, index) => (
                    <p key={index} className="pl-muted">{line}</p>
                  ))}
                  <ul>
                    {proposal.orders.map((order) => (
                      <li key={order.transportOrderId}>
                        <Link to={`/transport-orders/${order.transportOrderId}`}>{order.orderNumber}</Link>
                        {' '}{order.customerName}
                        {order.deliveryCity && ` · ${order.deliveryCity}`}
                        {order.overdue && <> {' '}<Badge tone="warning">{t('planning.proposals.overdue')}</Badge></>}
                        {order.constraints.map((constraint, index) => (
                          <span key={index} className="pl-muted pl-constraint">{constraint}</span>
                        ))}
                      </li>
                    ))}
                  </ul>
                </details>
              </li>
            )
          })}
        </ul>
      )}
      {suggestions.length === 0 && <p className="placeholder-text">{t('planning.proposals.empty')}</p>}
      {canFixOrders && gaps.length > 0 && (
        <details className="pl-proposals-gaps">
          <summary>{t('planning.proposals.gaps', { count: gaps.length })}</summary>
          {gaps.map((reason, index) => (
            <p key={index} className="pl-muted">{reason}</p>
          ))}
        </details>
      )}
    </section>
  )
}
