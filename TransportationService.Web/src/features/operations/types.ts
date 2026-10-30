import { getActiveLocale } from '../../i18n/activeLocale'
import type { TripStatus } from '../planning/types'

export type LocationSource =
  | 'LiveGps'
  | 'LastKnownGps'
  | 'ScanLocation'
  | 'StopLocation'
  | 'PlannedLocation'
  | 'Unavailable'

/** Vertaalsleutels (i18n-wave): render via t(LOCATION_SOURCE_LABELS[source]). */
export const LOCATION_SOURCE_LABELS: Record<LocationSource, string> = {
  LiveGps: 'operations.locationSource.LiveGps',
  LastKnownGps: 'operations.locationSource.LastKnownGps',
  ScanLocation: 'operations.locationSource.ScanLocation',
  StopLocation: 'operations.locationSource.StopLocation',
  PlannedLocation: 'operations.locationSource.PlannedLocation',
  Unavailable: 'operations.locationSource.Unavailable',
}

export type EtaSource = 'Heuristic' | 'Provider' | 'DispatcherOverride'

/** Honest source labelling: a heuristic or manual ETA never presents itself as live route data. */
export const ETA_SOURCE_LABELS: Record<EtaSource, string> = {
  Heuristic: 'operations.etaSource.Heuristic',
  Provider: 'operations.etaSource.Provider',
  DispatcherOverride: 'operations.etaSource.DispatcherOverride',
}

export type EtaStatus = 'OnTime' | 'AtRisk' | 'Late'

export const ETA_STATUS_META: Record<EtaStatus, { label: string; tone: 'success' | 'warning' | 'danger' }> = {
  OnTime: { label: 'operations.etaStatus.OnTime', tone: 'success' },
  AtRisk: { label: 'operations.etaStatus.AtRisk', tone: 'warning' },
  Late: { label: 'operations.etaStatus.Late', tone: 'danger' },
}

export interface TripPosition {
  source: LocationSource
  latitude: number | null
  longitude: number | null
  timestamp: string | null
  description: string | null
}

export interface OperationsStop {
  transportOrderStopId: string
  city: string | null
  locationName: string | null
  status: string
  plannedFrom: string | null
  plannedTo: string | null
  currentEta: string | null
  etaSource: EtaSource | null
  etaStatus: EtaStatus | null
}

export interface OperationsTrip {
  id: string
  tripNumber: string
  tripDate: string
  status: TripStatus
  driverName: string | null
  vehicleNumber: string | null
  trailerNumber: string | null
  stopCount: number
  completedStopCount: number
  currentStop: OperationsStop | null
  nextStop: OperationsStop | null
  etaStatus: EtaStatus | null
  etaSource: EtaSource | null
  delayMinutes: number | null
  position: TripPosition
  lastScanAt: string | null
  lastScanResult: string | null
  openExceptionCount: number
  missingPodCount: number
  /** Orders on the trip in sequence (B21). Absent on older payloads. */
  orders?: OperationsTripOrder[]
}

export interface OperationsTripOrder {
  id: string
  orderNumber: string
  customerName: string | null
  dossierId: string | null
  dossierNumber: string | null
}

export interface OperationsCounters {
  activeTrips: number
  delayedTrips: number
  openExceptions: number
  openCriticalIncidents: number
  missingPods: number
  activeAlerts: number
  criticalAlerts: number
  /** UX-008: failed drops whose incident is still open. */
  failedDeliveries?: number
}

/** UX-008: a failed drop as a work item; lives on the incident the failure already created. */
export interface OperationsFailedDelivery {
  incidentId: string
  orderId: string | null
  orderNumber: string | null
  tripId: string | null
  tripNumber: string | null
  customerName: string | null
  dossierId: string | null
  dossierNumber: string | null
  description: string
  occurredAt: string
  redeliveryOrderId: string | null
  redeliveryOrderNumber: string | null
}

export interface OperationsOverview {
  generatedAt: string
  counters: OperationsCounters
  trips: OperationsTrip[]
  failedDeliveries?: OperationsFailedDelivery[] | null
}

export type AlertSeverity = 'Information' | 'Warning' | 'Critical'
export type AlertStatus = 'Active' | 'Acknowledged' | 'Resolved'

export const ALERT_SEVERITY_META: Record<AlertSeverity, { label: string; tone: 'info' | 'warning' | 'danger' }> = {
  Information: { label: 'operations.alertSeverity.Information', tone: 'info' },
  Warning: { label: 'operations.alertSeverity.Warning', tone: 'warning' },
  Critical: { label: 'operations.alertSeverity.Critical', tone: 'danger' },
}

export const ALERT_CATEGORY_LABELS: Record<string, string> = {
  Delay: 'operations.alertCategory.Delay',
  Pod: 'operations.alertCategory.Pod',
  Incident: 'operations.alertCategory.Incident',
  Exception: 'operations.alertCategory.Exception',
  Maintenance: 'operations.alertCategory.Maintenance',
  Inspection: 'operations.alertCategory.Inspection',
  Document: 'operations.alertCategory.Document',
}

export interface OperationalAlert {
  id: string
  severity: AlertSeverity
  category: string
  source: string
  title: string
  message: string
  linkPath: string | null
  relatedEntityType: string | null
  relatedEntityId: string | null
  status: AlertStatus
  assignedUserId: string | null
  assignedUserName: string | null
  acknowledgedByUserId: string | null
  acknowledgedByName: string | null
  acknowledgedAt: string | null
  resolvedAt: string | null
  createdAt: string
  lastSeenAt: string
}

/**
 * Compact "x min" / "1 u 20 min" delay formatting for the trips table. The hour unit
 * follows the active UI language (NL "u", FR/EN "h"); "min" is shared by all three.
 */
/**
 * Delay in the dispatcher's words (B21): "12 min", "2 u 15 min", "13 dagen", "1 dag 4 u".
 * Past one day the minutes no longer matter; "+328 u 43 min" told nobody anything.
 */
export function formatDelay(minutes: number | null): string | null {
  if (minutes === null || minutes <= 0) return null
  const locale = getActiveLocale()
  const hourUnit = locale === 'nl' ? 'u' : 'h'
  if (minutes < 60) return `${minutes} min`
  const totalHours = Math.floor(minutes / 60)
  if (totalHours < 24) {
    const rest = minutes % 60
    return rest === 0 ? `${totalHours} ${hourUnit}` : `${totalHours} ${hourUnit} ${rest} min`
  }
  const days = Math.floor(totalHours / 24)
  const hours = totalHours % 24
  const dayWord = locale === 'fr' ? (days === 1 ? 'jour' : 'jours') : locale === 'en' ? (days === 1 ? 'day' : 'days') : (days === 1 ? 'dag' : 'dagen')
  return hours > 0 && days < 3 ? `${days} ${dayWord} ${hours} ${hourUnit}` : `${days} ${dayWord}`
}
