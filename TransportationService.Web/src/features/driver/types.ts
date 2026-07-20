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

export const DRIVER_DOCUMENT_TYPE_LABELS: Record<string, string> = {
  Registration: 'Inschrijving',
  Insurance: 'Verzekering',
  TechnicalInspection: 'Technische keuring',
  Conformity: 'Gelijkvormigheid',
  CraneInspection: 'Kraankeuring',
  RefrigerationCertificate: 'Koelcertificaat',
  AdrCertificate: 'ADR-certificaat',
  Other: 'Ander document',
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

export const DRIVER_INCIDENT_TYPES: { value: string; label: string }[] = [
  { value: 'VehicleBreakdown', label: 'Voertuigpech' },
  { value: 'Accident', label: 'Ongeval' },
  { value: 'Damage', label: 'Schade' },
  { value: 'Delay', label: 'Vertraging' },
  { value: 'Theft', label: 'Diefstal' },
  { value: 'Other', label: 'Anders…' },
]

export const DRIVER_INCIDENT_SEVERITIES: { value: string; label: string }[] = [
  { value: 'Low', label: 'Laag' },
  { value: 'Medium', label: 'Gemiddeld' },
  { value: 'High', label: 'Hoog' },
  { value: 'Critical', label: 'Kritiek' },
]
