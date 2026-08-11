import { useEffect, useState, type FormEvent } from 'react'
import { describeApiError } from '../../../api/problemDetails'
import { Badge } from '../../../components/ui/Badge'
import { useToast } from '../../../components/ui/toastContext'
import { OfflineQueuedError } from '../offlineActions'
import { listMyTrips } from '../../my-trips/api/myTripsApi'
import type { MyTrip } from '../../my-trips/types'
import { createMyIncident, listMyIncidents, type MyIncident } from '../api/driverApi'
import { DRIVER_INCIDENT_SEVERITIES, DRIVER_INCIDENT_TYPES } from '../types'
import {
  INCIDENT_SEVERITY_LABELS,
  INCIDENT_STATUS_LABELS,
  type IncidentSeverity,
  type IncidentStatus,
} from '../../incidents/types'

/**
 * Driver incident reporting on the shared incident register. Works offline: a report
 * without connection lands in the action queue (idempotent replay) instead of vanishing.
 */
export function DriverIncidentsPage() {
  const { showError, showSuccess } = useToast()
  const [incidents, setIncidents] = useState<MyIncident[]>([])
  const [trips, setTrips] = useState<MyTrip[]>([])
  const [reloadToken, setReloadToken] = useState(0)
  const [busy, setBusy] = useState(false)

  const [title, setTitle] = useState('')
  const [description, setDescription] = useState('')
  const [incidentType, setIncidentType] = useState('VehicleBreakdown')
  const [customTypeName, setCustomTypeName] = useState('')
  const [severity, setSeverity] = useState('Medium')
  const [tripId, setTripId] = useState('')

  useEffect(() => {
    let cancelled = false
    listMyIncidents()
      .then((data) => {
        if (!cancelled) setIncidents(data)
      })
      .catch(() => undefined)
    listMyTrips()
      .then((data) => {
        if (!cancelled) setTrips(data)
      })
      .catch(() => undefined)
    return () => {
      cancelled = true
    }
  }, [reloadToken])

  async function submit(event: FormEvent) {
    event.preventDefault()
    setBusy(true)
    try {
      await createMyIncident({
        title: title.trim(),
        description: description.trim(),
        incidentType,
        severity,
        customTypeName: incidentType === 'Other' ? customTypeName.trim() || null : null,
        tripId: tripId || null,
      })
      showSuccess('Incident gemeld.')
      setTitle('')
      setDescription('')
      setTripId('')
      setReloadToken((t) => t + 1)
    } catch (error) {
      if (error instanceof OfflineQueuedError) {
        showSuccess(error.message)
        setTitle('')
        setDescription('')
      } else {
        showError(describeApiError(error, 'Melden mislukt.').message)
      }
    } finally {
      setBusy(false)
    }
  }

  return (
    <div>
      <h1 className="drv-page-title">Incident melden</h1>
      <form className="drv-card drv-form" onSubmit={(event) => void submit(event)}>
        <label>
          Titel
          <input value={title} onChange={(event) => setTitle(event.target.value)} required maxLength={200} />
        </label>
        <label>
          Wat is er gebeurd?
          <textarea value={description} onChange={(event) => setDescription(event.target.value)} required rows={3} />
        </label>
        <label>
          Type
          <select value={incidentType} onChange={(event) => setIncidentType(event.target.value)}>
            {DRIVER_INCIDENT_TYPES.map((type) => (
              <option key={type.value} value={type.value}>{type.label}</option>
            ))}
          </select>
        </label>
        {incidentType === 'Other' && (
          <label>
            Omschrijf het type
            <input value={customTypeName} onChange={(event) => setCustomTypeName(event.target.value)} required />
          </label>
        )}
        <label>
          Ernst
          <select value={severity} onChange={(event) => setSeverity(event.target.value)}>
            {DRIVER_INCIDENT_SEVERITIES.map((option) => (
              <option key={option.value} value={option.value}>{option.label}</option>
            ))}
          </select>
        </label>
        <label>
          Rit (optioneel)
          <select value={tripId} onChange={(event) => setTripId(event.target.value)}>
            <option value="">Geen rit</option>
            {trips.map((trip) => (
              <option key={trip.id} value={trip.id}>
                {trip.tripNumber} · {new Date(`${trip.tripDate}T00:00:00`).toLocaleDateString('nl-BE')}
              </option>
            ))}
          </select>
        </label>
        <button type="submit" className="drv-submit" disabled={busy || !title.trim() || !description.trim()}>
          {busy ? 'Versturen…' : 'Incident melden'}
        </button>
      </form>

      <h2 className="drv-page-title">Mijn incidenten</h2>
      {incidents.length === 0 && <p className="drv-muted">Nog geen incidenten gemeld.</p>}
      <ul className="drv-list">
        {incidents.map((incident) => (
          <li key={incident.id} className="drv-card">
            <div className="drv-fact-row">
              <strong>{incident.title}</strong>
              <Badge tone={incident.severity === 'Critical' || incident.severity === 'High' ? 'danger' : 'warning'}>
                {INCIDENT_SEVERITY_LABELS[incident.severity as IncidentSeverity] ?? incident.severity}
              </Badge>
            </div>
            <div className="drv-fact-row">
              <span>{INCIDENT_STATUS_LABELS[incident.status as IncidentStatus] ?? incident.status}</span>
              <span className="drv-muted">{new Date(incident.createdAt).toLocaleString('nl-BE')}</span>
            </div>
          </li>
        ))}
      </ul>
    </div>
  )
}
