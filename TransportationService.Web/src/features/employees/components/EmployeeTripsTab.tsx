import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { Badge } from '../../../components/ui/Badge'
import { useLocale } from '../../../i18n/localeContext'
import { listTrips } from '../../planning/api/planningApi'
import { TRIP_STATUS_LABELS, TRIP_STATUS_TONE, type TripListItem } from '../../planning/types'
import { toIsoDate } from '../../employee-planning/types'
import '../../planning/pages/planning.css'

/**
 * Trip history for an employee with a driver profile: 90 days back, 30 days ahead.
 * Rows deep-link to the trip detail (the tab itself is gated on planning.view).
 */
export function EmployeeTripsTab({ driverId }: { driverId: string }) {
  const navigate = useNavigate()
  const { t } = useLocale()
  const [trips, setTrips] = useState<TripListItem[] | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let mounted = true
    const today = new Date()
    const from = toIsoDate(new Date(today.getTime() - 90 * 86_400_000))
    const to = toIsoDate(new Date(today.getTime() + 30 * 86_400_000))
    listTrips({ from, to, driverId })
      .then((rows) => {
        if (mounted) setTrips(rows)
      })
      .catch(() => {
        if (mounted) setError('employees.tripsTab.loadFailed')
      })
    return () => {
      mounted = false
    }
  }, [driverId])

  if (error) return <p className="placeholder-text">{t(error)}</p>
  if (trips === null) return <p className="placeholder-text">{t('employees.tripsTab.loading')}</p>
  if (trips.length === 0) return <p className="placeholder-text">{t('employees.tripsTab.empty')}</p>

  return (
    <table className="pl-table">
      <thead>
        <tr>
          <th>{t('employees.tripsTab.columnTrip')}</th>
          <th>{t('employees.tripsTab.columnDate')}</th>
          <th>{t('employees.tripsTab.columnStatus')}</th>
          <th>{t('employees.tripsTab.columnVehicle')}</th>
          <th>{t('employees.tripsTab.columnOrders')}</th>
        </tr>
      </thead>
      <tbody>
        {trips.map((trip) => (
          <tr key={trip.id} className="pl-row" onClick={() => navigate(`/planning/${trip.id}`)}>
            <td>{trip.tripNumber}</td>
            <td>{trip.tripDate}</td>
            <td>
              <Badge tone={TRIP_STATUS_TONE[trip.status]}>{TRIP_STATUS_LABELS[trip.status]}</Badge>
            </td>
            <td>{trip.vehicleNumber ? `${trip.vehicleNumber} · ${trip.vehicleLicensePlate ?? ''}` : '—'}</td>
            <td>{trip.orderCount}</td>
          </tr>
        ))}
      </tbody>
    </table>
  )
}
