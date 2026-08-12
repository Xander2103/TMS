import type { BadgeTone } from '../../components/ui/Badge'

export type IncidentStatus = 'New' | 'InProgress' | 'Resolved' | 'Cancelled'

export type IncidentSeverity = 'Low' | 'Medium' | 'High' | 'Critical'

export type IncidentType =
  | 'Damage'
  | 'Delay'
  | 'Theft'
  | 'Accident'
  | 'WrongDelivery'
  | 'MissingGoods'
  | 'CustomerComplaint'
  | 'VehicleBreakdown'
  | 'Administrative'
  | 'Other'

export interface IncidentListItem {
  id: string
  title: string
  incidentType: IncidentType
  customTypeName: string | null
  status: IncidentStatus
  severity: IncidentSeverity
  customerName: string | null
  responsibleName: string | null
  dossierNumber: string | null
  dueDate: string | null
  isOverdue: boolean
  createdAt: string
}

export interface IncidentDetail {
  id: string
  title: string
  description: string
  incidentType: IncidentType
  customTypeName: string | null
  status: IncidentStatus
  severity: IncidentSeverity
  cause: string | null
  responsibleUserId: string | null
  responsibleName: string | null
  customerImpact: string | null
  operationalImpact: string | null
  financialImpact: string | null
  estimatedCost: number | null
  actualCost: number | null
  customerId: string | null
  customerName: string | null
  driverId: string | null
  driverName: string | null
  vehicleId: string | null
  vehicleLabel: string | null
  trailerId: string | null
  trailerLabel: string | null
  transportOrderId: string | null
  transportOrderNumber: string | null
  tripId: string | null
  tripNumber: string | null
  dossierId: string | null
  dossierNumber: string | null
  dueDate: string | null
  resolution: string | null
  resolvedAt: string | null
  createdAt: string
  allowedStatusChanges: IncidentStatus[]
  /** Wave 6 §1: Unknown | Customer | Own | Driver | Supplier. */
  responsibleParty: string
  responsibilityNotes: string | null
  /** Wave 6 §2: None | Proposed | Approved | Rejected. */
  chargeDecision: string
  chargeAmount: number | null
  chargeDescription: string | null
  /** Wave 6 §3: aangemaakte herleveringsorder. */
  linkedRedeliveryOrderId: string | null
  linkedRedeliveryOrderNumber: string | null
  /** Herlevering aanbevolen na een mislukte levering (RedeliveryMode Propose/Automatic). */
  redeliverySuggested: boolean
}

export interface IncidentInput {
  title: string
  description: string
  incidentType: IncidentType
  severity: IncidentSeverity
  customTypeName: string | null
  cause: string | null
  responsibleUserId: string | null
  customerImpact: string | null
  operationalImpact: string | null
  financialImpact: string | null
  estimatedCost: number | null
  actualCost: number | null
  customerId: string | null
  transportOrderId: string | null
  dossierId: string | null
  vehicleId: string | null
  dueDate: string | null
  /** Wave 6 §1. */
  responsibleParty?: string
  responsibilityNotes?: string | null
}

export const RESPONSIBLE_PARTY_LABELS: Record<string, string> = {
  Unknown: 'Onbekend',
  Customer: 'Klant',
  Own: 'Eigen organisatie',
  Driver: 'Chauffeur',
  Supplier: 'Leverancier',
}

export const CHARGE_DECISION_LABELS: Record<string, string> = {
  None: 'Geen doorrekening',
  Proposed: 'Voorgesteld',
  Approved: 'Goedgekeurd',
  Rejected: 'Afgekeurd',
}

export const INCIDENT_TYPE_LABELS: Record<IncidentType, string> = {
  Damage: 'Schade',
  Delay: 'Vertraging',
  Theft: 'Diefstal',
  Accident: 'Ongeval',
  WrongDelivery: 'Foutieve levering',
  MissingGoods: 'Ontbrekende goederen',
  CustomerComplaint: 'Klachtenmelding klant',
  VehicleBreakdown: 'Voertuigpech',
  Administrative: 'Administratief',
  Other: 'Overig',
}

export const INCIDENT_STATUS_LABELS: Record<IncidentStatus, string> = {
  New: 'Nieuw',
  InProgress: 'In behandeling',
  Resolved: 'Afgehandeld',
  Cancelled: 'Geannuleerd',
}

export const INCIDENT_STATUS_TONE: Record<IncidentStatus, BadgeTone> = {
  New: 'info',
  InProgress: 'warning',
  Resolved: 'success',
  Cancelled: 'neutral',
}

export const INCIDENT_SEVERITY_LABELS: Record<IncidentSeverity, string> = {
  Low: 'Laag',
  Medium: 'Middel',
  High: 'Hoog',
  Critical: 'Kritiek',
}

export const INCIDENT_SEVERITY_TONE: Record<IncidentSeverity, BadgeTone> = {
  Low: 'neutral',
  Medium: 'info',
  High: 'warning',
  Critical: 'danger',
}

export function incidentTypeLabel(incident: { incidentType: IncidentType; customTypeName: string | null }): string {
  return incident.incidentType === 'Other' && incident.customTypeName
    ? incident.customTypeName
    : INCIDENT_TYPE_LABELS[incident.incidentType]
}
