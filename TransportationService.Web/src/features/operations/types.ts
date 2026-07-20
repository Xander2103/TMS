import type { TripStatus } from '../planning/types'

export type LocationSource =
  | 'LiveGps'
  | 'LastKnownGps'
  | 'ScanLocation'
  | 'StopLocation'
  | 'PlannedLocation'
  | 'Unavailable'

export const LOCATION_SOURCE_LABELS: Record<LocationSource, string> = {
  LiveGps: 'Live GPS',
  LastKnownGps: 'Laatst gekende GPS',
  ScanLocation: 'Scanlocatie',
  StopLocation: 'Laatste stop',
  PlannedLocation: 'Geplande stop',
  Unavailable: 'Geen locatie',
}

export type EtaSource = 'Heuristic' | 'Provider' | 'DispatcherOverride'

/** Honest source labelling: a heuristic or manual ETA never presents itself as live route data. */
export const ETA_SOURCE_LABELS: Record<EtaSource, string> = {
  Heuristic: 'raming',
  Provider: 'routeprovider',
  DispatcherOverride: 'handmatig',
}

export type EtaStatus = 'OnTime' | 'AtRisk' | 'Late'

export const ETA_STATUS_META: Record<EtaStatus, { label: string; tone: 'success' | 'warning' | 'danger' }> = {
  OnTime: { label: 'Op tijd', tone: 'success' },
  AtRisk: { label: 'Risico', tone: 'warning' },
  Late: { label: 'Te laat', tone: 'danger' },
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
}

export interface OperationsCounters {
  activeTrips: number
  delayedTrips: number
  openExceptions: number
  openCriticalIncidents: number
  missingPods: number
  activeAlerts: number
  criticalAlerts: number
}

export interface OperationsOverview {
  generatedAt: string
  counters: OperationsCounters
  trips: OperationsTrip[]
}

export type AlertSeverity = 'Information' | 'Warning' | 'Critical'
export type AlertStatus = 'Active' | 'Acknowledged' | 'Resolved'

export const ALERT_SEVERITY_META: Record<AlertSeverity, { label: string; tone: 'info' | 'warning' | 'danger' }> = {
  Information: { label: 'Info', tone: 'info' },
  Warning: { label: 'Waarschuwing', tone: 'warning' },
  Critical: { label: 'Kritiek', tone: 'danger' },
}

export const ALERT_CATEGORY_LABELS: Record<string, string> = {
  Delay: 'Vertraging',
  Pod: 'Afleverbewijs',
  Incident: 'Incident',
  Exception: 'Afwijking',
  Maintenance: 'Onderhoud',
  Inspection: 'Keuring',
  Document: 'Document',
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

/** Compact "x min" / "1 u 20 min" delay formatting for the trips table. */
export function formatDelay(minutes: number | null): string | null {
  if (minutes === null || minutes <= 0) return null
  if (minutes < 60) return `${minutes} min`
  const hours = Math.floor(minutes / 60)
  const rest = minutes % 60
  return rest === 0 ? `${hours} u` : `${hours} u ${rest} min`
}
