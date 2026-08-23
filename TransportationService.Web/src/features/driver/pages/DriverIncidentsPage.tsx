import { useEffect, useState, type FormEvent } from 'react'
import { describeApiError } from '../../../api/problemDetails'
import { Badge } from '../../../components/ui/Badge'
import { useToast } from '../../../components/ui/toastContext'
import { useLocale } from '../../../i18n/localeContext'
import { OfflineQueuedError } from '../offlineActions'
import { listMyTrips } from '../../my-trips/api/myTripsApi'
import type { MyTrip } from '../../my-trips/types'
import { createMyIncident, listMyIncidents, type MyIncident } from '../api/driverApi'
import { DRIVER_INCIDENT_SEVERITIES, DRIVER_INCIDENT_TYPES } from '../types'
import { formatDate, formatDateTime } from '../../../utils/dates'

/** Translation keys per code (Record<string, …>: the API serialises these as strings). */
const INCIDENT_SEVERITY_KEYS: Record<string, string> = {
  Low: 'driverApp.incidentSeverity.Low',
  Medium: 'driverApp.incidentSeverity.Medium',
  High: 'driverApp.incidentSeverity.High',
  Critical: 'driverApp.incidentSeverity.Critical',
}

const INCIDENT_STATUS_KEYS: Record<string, string> = {
  New: 'driverApp.incidentStatus.New',
  InProgress: 'driverApp.incidentStatus.InProgress',
  Resolved: 'driverApp.incidentStatus.Resolved',
  Cancelled: 'driverApp.incidentStatus.Cancelled',
}

/**
 * Driver incident reporting on the shared incident register. Works offline: a report
 * without connection lands in the action queue (idempotent replay) instead of vanishing.
 */
export function DriverIncidentsPage() {
  const { showError, showSuccess } = useToast()
  const { t } = useLocale()
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
      showSuccess(t('driverApp.incidents.reported'))
      setTitle('')
      setDescription('')
      setTripId('')
      setReloadToken((token) => token + 1)
    } catch (error) {
      if (error instanceof OfflineQueuedError) {
        showSuccess(t(error.translationKey))
        setTitle('')
        setDescription('')
      } else {
        showError(describeApiError(error, t('driverApp.incidents.reportFailed')).message)
      }
    } finally {
      setBusy(false)
    }
  }

  return (
    <div>
      <h1 className="drv-page-title">{t('driverApp.incidents.title')}</h1>
      <form className="drv-card drv-form" onSubmit={(event) => void submit(event)}>
        <label>
          {t('driverApp.incidents.titleField')}
          <input value={title} onChange={(event) => setTitle(event.target.value)} required maxLength={200} />
        </label>
        <label>
          {t('driverApp.incidents.descriptionField')}
          <textarea value={description} onChange={(event) => setDescription(event.target.value)} required rows={3} />
        </label>
        <label>
          {t('driverApp.incidents.typeField')}
          <select value={incidentType} onChange={(event) => setIncidentType(event.target.value)}>
            {DRIVER_INCIDENT_TYPES.map((type) => (
              <option key={type.value} value={type.value}>{t(type.label)}</option>
            ))}
          </select>
        </label>
        {incidentType === 'Other' && (
          <label>
            {t('driverApp.incidents.customTypeField')}
            <input value={customTypeName} onChange={(event) => setCustomTypeName(event.target.value)} required />
          </label>
        )}
        <label>
          {t('driverApp.incidents.severityField')}
          <select value={severity} onChange={(event) => setSeverity(event.target.value)}>
            {DRIVER_INCIDENT_SEVERITIES.map((option) => (
              <option key={option.value} value={option.value}>{t(option.label)}</option>
            ))}
          </select>
        </label>
        <label>
          {t('driverApp.incidents.tripField')}
          <select value={tripId} onChange={(event) => setTripId(event.target.value)}>
            <option value="">{t('driverApp.incidents.noTrip')}</option>
            {trips.map((trip) => (
              <option key={trip.id} value={trip.id}>
                {trip.tripNumber} · {formatDate(trip.tripDate)}
              </option>
            ))}
          </select>
        </label>
        <button type="submit" className="drv-submit" disabled={busy || !title.trim() || !description.trim()}>
          {busy ? t('driverApp.incidents.submitting') : t('driverApp.incidents.submit')}
        </button>
      </form>

      <h2 className="drv-page-title">{t('driverApp.incidents.myIncidents')}</h2>
      {incidents.length === 0 && <p className="drv-muted">{t('driverApp.incidents.empty')}</p>}
      <ul className="drv-list">
        {incidents.map((incident) => (
          <li key={incident.id} className="drv-card">
            <div className="drv-fact-row">
              <strong>{incident.title}</strong>
              <Badge tone={incident.severity === 'Critical' || incident.severity === 'High' ? 'danger' : 'warning'}>
                {INCIDENT_SEVERITY_KEYS[incident.severity] ? t(INCIDENT_SEVERITY_KEYS[incident.severity]) : incident.severity}
              </Badge>
            </div>
            <div className="drv-fact-row">
              <span>{INCIDENT_STATUS_KEYS[incident.status] ? t(INCIDENT_STATUS_KEYS[incident.status]) : incident.status}</span>
              <span className="drv-muted">{formatDateTime(incident.createdAt)}</span>
            </div>
          </li>
        ))}
      </ul>
    </div>
  )
}
