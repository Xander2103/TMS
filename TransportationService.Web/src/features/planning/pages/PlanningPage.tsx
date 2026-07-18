import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import { createTrip, listTrips } from '../api/planningApi'
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
        if (mounted) setLoadError('De planning kon niet worden geladen.')
      })
    return () => {
      mounted = false
    }
  }, [date, statusFilter])

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
      })
      navigate(`/planning/${trip.id}`)
    } catch {
      showError('De rit kon niet worden aangemaakt.')
    } finally {
      setCreating(false)
    }
  }

  return (
    <div>
      <Breadcrumbs items={[{ label: 'Planning' }]} />
      <PageHeader
        title="Planning"
        action={
          hasPermission('planning.create') ? (
            <Button onClick={() => void handleNewTrip()} disabled={creating}>
              {creating ? 'Bezig…' : 'Nieuwe rit'}
            </Button>
          ) : undefined
        }
      />

      <div className="pl-toolbar">
        <div className="pl-datenav">
          <button type="button" className="pl-nav-btn" onClick={() => setDate((d) => shiftDate(d, -1))} aria-label="Vorige dag">
            ←
          </button>
          <input type="date" value={date} onChange={(e) => setDate(e.target.value)} aria-label="Datum" />
          <button type="button" className="pl-nav-btn" onClick={() => setDate((d) => shiftDate(d, 1))} aria-label="Volgende dag">
            →
          </button>
          <Button variant="secondary" onClick={() => setDate(todayIso())}>
            Vandaag
          </Button>
        </div>
        <select
          value={statusFilter}
          onChange={(e) => setStatusFilter(e.target.value as TripStatus | '')}
          aria-label="Statusfilter"
        >
          <option value="">Alle statussen</option>
          {TRIP_STATUSES.map((status) => (
            <option key={status} value={status}>
              {TRIP_STATUS_LABELS[status]}
            </option>
          ))}
        </select>
      </div>

      {loadError && <p className="placeholder-text">{loadError}</p>}
      {!loadError && trips === null && <p className="placeholder-text">Planning laden…</p>}
      {!loadError && trips !== null && trips.length === 0 && (
        <p className="placeholder-text">Geen ritten op deze dag.</p>
      )}

      {!loadError && trips !== null && trips.length > 0 && (
        <table className="pl-table">
          <thead>
            <tr>
              <th>Rit</th>
              <th>Chauffeur</th>
              <th>Voertuig</th>
              <th>Oplegger</th>
              <th>Opdrachten</th>
              <th>Status</th>
              <th>Conflicten</th>
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
                  <Badge tone={TRIP_STATUS_TONE[trip.status]}>{TRIP_STATUS_LABELS[trip.status]}</Badge>
                </td>
                <td>
                  {trip.blockingConflictCount > 0 ? (
                    <Badge tone="danger">⚠ {trip.blockingConflictCount}</Badge>
                  ) : (
                    <Badge tone="success">OK</Badge>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  )
}
