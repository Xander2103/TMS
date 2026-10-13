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

/** Vertaalsleutels (i18n-wave): render via t(...LABELS[code] ?? code). */
export const RESPONSIBLE_PARTY_LABELS: Record<string, string> = {
  Unknown: 'incidents.responsibleParty.Unknown',
  Customer: 'incidents.responsibleParty.Customer',
  Own: 'incidents.responsibleParty.Own',
  Driver: 'incidents.responsibleParty.Driver',
  Supplier: 'incidents.responsibleParty.Supplier',
}

export const CHARGE_DECISION_LABELS: Record<string, string> = {
  None: 'incidents.chargeDecision.None',
  Proposed: 'incidents.chargeDecision.Proposed',
  Approved: 'incidents.chargeDecision.Approved',
  Rejected: 'incidents.chargeDecision.Rejected',
}

export const INCIDENT_TYPE_LABELS: Record<IncidentType, string> = {
  Damage: 'incidents.type.Damage',
  Delay: 'incidents.type.Delay',
  Theft: 'incidents.type.Theft',
  Accident: 'incidents.type.Accident',
  WrongDelivery: 'incidents.type.WrongDelivery',
  MissingGoods: 'incidents.type.MissingGoods',
  CustomerComplaint: 'incidents.type.CustomerComplaint',
  VehicleBreakdown: 'incidents.type.VehicleBreakdown',
  Administrative: 'incidents.type.Administrative',
  Other: 'incidents.type.Other',
}

export const INCIDENT_STATUS_LABELS: Record<IncidentStatus, string> = {
  New: 'incidents.status.New',
  InProgress: 'incidents.status.InProgress',
  Resolved: 'incidents.status.Resolved',
  Cancelled: 'incidents.status.Cancelled',
}

export const INCIDENT_STATUS_TONE: Record<IncidentStatus, BadgeTone> = {
  New: 'info',
  InProgress: 'warning',
  Resolved: 'success',
  Cancelled: 'neutral',
}

export const INCIDENT_SEVERITY_LABELS: Record<IncidentSeverity, string> = {
  Low: 'incidents.severity.Low',
  Medium: 'incidents.severity.Medium',
  High: 'incidents.severity.High',
  Critical: 'incidents.severity.Critical',
}

export const INCIDENT_SEVERITY_TONE: Record<IncidentSeverity, BadgeTone> = {
  Low: 'neutral',
  Medium: 'info',
  High: 'warning',
  Critical: 'danger',
}

/**
 * Weergavelabel voor het incidenttype: de vrije typenaam (data, nooit vertalen) bij
 * 'Other', anders het via `t` vertaalde standaardtype.
 */
export function incidentTypeLabel(
  incident: { incidentType: IncidentType; customTypeName: string | null },
  t: (key: string) => string,
): string {
  return incident.incidentType === 'Other' && incident.customTypeName
    ? incident.customTypeName
    : t(INCIDENT_TYPE_LABELS[incident.incidentType])
}
