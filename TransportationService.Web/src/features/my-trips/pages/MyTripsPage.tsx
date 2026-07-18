import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { Badge } from '../../../components/ui/Badge'
import { TRIP_STATUS_LABELS, TRIP_STATUS_TONE } from '../../planning/types'
import { listMyTrips } from '../api/myTripsApi'
import type { MyTrip } from '../types'
import './my-trips.css'

/** Driver view: own upcoming trips (planned + in progress + recently completed). */
export function MyTripsPage() {
  const navigate = useNavigate()
  const [trips, setTrips] = useState<MyTrip[] | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)

  useEffect(() => {
    let mounted = true
    listMyTrips()
      .then((data) => {
        if (!mounted) return
        setTrips(data)
        setLoadError(null)
      })
      .catch(() => {
        if (mounted) setLoadError('Je ritten konden niet worden geladen.')
      })
    return () => {
      mounted = false
    }
  }, [])

  return (
    <div>
      <Breadcrumbs items={[{ label: 'Mijn ritten' }]} />
      <PageHeader title="Mijn ritten" subtitle="Jouw geplande ritten voor de komende week." />

      {loadError && <p className="placeholder-text">{loadError}</p>}
      {!loadError && trips === null && <p className="placeholder-text">Ritten laden…</p>}
      {!loadError && trips !== null && trips.length === 0 && (
        <p className="placeholder-text">
          Geen ritten gepland. Ritten verschijnen hier zodra de planner ze aan jou toewijst.
        </p>
      )}

      {!loadError && trips !== null && trips.length > 0 && (
        <div className="mt-cards">
          {trips.map((trip) => (
            <button key={trip.id} type="button" className="mt-card" onClick={() => navigate(`/my-trips/${trip.id}`)}>
              <div className="mt-card-head">
                <code>{trip.tripNumber}</code>
                <Badge tone={TRIP_STATUS_TONE[trip.status]}>{TRIP_STATUS_LABELS[trip.status]}</Badge>
              </div>
              <div className="mt-card-date">{trip.tripDate}</div>
              <div className="mt-card-meta">
                {trip.vehicleNumber ? `${trip.vehicleNumber} (${trip.vehicleLicensePlate})` : 'Geen voertuig'}
                {trip.trailerNumber ? ` + ${trip.trailerNumber}` : ''}
              </div>
              <div className="mt-card-progress">
                {trip.orderCount} opdracht(en) · {trip.completedStopCount}/{trip.stopCount} stops afgehandeld
              </div>
            </button>
          ))}
        </div>
      )}
    </div>
  )
}
