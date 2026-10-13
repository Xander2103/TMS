import type { MyTrip } from '../my-trips/types'
import type { EtaSource } from '../operations/types'

export interface MyDashboard {
  currentTrip: MyTrip | null
  nextTrip: MyTrip | null
  nextStopCity: string | null
  nextStopLocationName: string | null
  nextStopPlannedFrom: string | null
  nextStopEta: string | null
  nextStopEtaSource: EtaSource | null
  openStopCount: number
  unresolvedExceptionCount: number
  activeIncidentCount: number
  todayTripCount: number
}

export interface DriverDocument {
  id: string
  source: 'Vehicle' | 'Trailer'
  assetNumber: string
  documentType: string
  customTypeName: string | null
  documentNumber: string | null
  expiryDate: string | null
  fileAvailable: boolean
}

/** Translation keys per document type; render sites resolve them via t(). */
export const DRIVER_DOCUMENT_TYPE_LABELS: Record<string, string> = {
  Registration: 'driverApp.documentType.Registration',
  Insurance: 'driverApp.documentType.Insurance',
  TechnicalInspection: 'driverApp.documentType.TechnicalInspection',
  Conformity: 'driverApp.documentType.Conformity',
  CraneInspection: 'driverApp.documentType.CraneInspection',
  RefrigerationCertificate: 'driverApp.documentType.RefrigerationCertificate',
  AdrCertificate: 'driverApp.documentType.AdrCertificate',
  Other: 'driverApp.documentType.Other',
}

export interface DriverIncidentInput {
  title: string
  description: string
  incidentType: string
  severity: string
  customTypeName?: string | null
  tripId?: string | null
  vehicleId?: string | null
  trailerId?: string | null
  clientRequestId?: string
}

/** `label` values are translation keys; render sites resolve them via t(). */
export const DRIVER_INCIDENT_TYPES: { value: string; label: string }[] = [
  { value: 'VehicleBreakdown', label: 'driverApp.incidentType.VehicleBreakdown' },
  { value: 'Accident', label: 'driverApp.incidentType.Accident' },
  { value: 'Damage', label: 'driverApp.incidentType.Damage' },
  { value: 'Delay', label: 'driverApp.incidentType.Delay' },
  { value: 'Theft', label: 'driverApp.incidentType.Theft' },
  { value: 'Other', label: 'driverApp.incidentType.Other' },
]

/** `label` values are translation keys; render sites resolve them via t(). */
export const DRIVER_INCIDENT_SEVERITIES: { value: string; label: string }[] = [
  { value: 'Low', label: 'driverApp.incidentSeverity.Low' },
  { value: 'Medium', label: 'driverApp.incidentSeverity.Medium' },
  { value: 'High', label: 'driverApp.incidentSeverity.High' },
  { value: 'Critical', label: 'driverApp.incidentSeverity.Critical' },
]
