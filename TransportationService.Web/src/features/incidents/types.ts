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
  orderNumber?: string | null
  tripNumber?: string | null
  driverName?: string | null
  vehicleLabel?: string | null
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
  /** Office | Driver | FailedDelivery | Exception. */
  source?: IncidentSource
  sourceExceptionId?: string | null
  sourceExceptionLabel?: string | null
  resolvedByUserId?: string | null
  resolvedByName?: string | null
  /** Klantgerichte tekst — de omschrijving blijft intern. */
  customerMessage?: string | null
  damageReportId?: string | null
  damageReportSeverity?: string | null
  damageReportStatus?: string | null
  canCreateDamageReport?: boolean
  vehicleSafetyIssue?: boolean
  /** Rit/chauffeur/oplegger liggen vast (gemeld door de chauffeur, de automatisering of een afwijking). */
  executionLinksLocked?: boolean
  /** Dossiers die al bevestigd waren toen dit incident gemeld werd. */
  confirmedDossierNumbers?: string[]
}

export type IncidentSource = 'Office' | 'Driver' | 'FailedDelivery' | 'Exception'

/**
 * EX-01: het kantoor werkt per kaart bij. Enkel de meegestuurde secties worden toegepast — een
 * kaart die de ernst wijzigt kan nooit de rit, chauffeur of andere koppelingen wissen.
 */
export interface IncidentUpdate {
  report?: {
    title: string
    description: string
    incidentType: IncidentType
    severity: IncidentSeverity
    customTypeName: string | null
    cause: string | null
    responsibleUserId: string | null
    dueDate: string | null
  }
  links?: {
    customerId: string | null
    dossierId: string | null
    transportOrderId: string | null
    vehicleId: string | null
    /** Uitvoeringskoppelingen: enkel invullen wanneer ze nog leeg zijn. */
    trailerId?: string | null
    tripId?: string | null
    driverId?: string | null
  }
  impact?: {
    customerImpact: string | null
    operationalImpact: string | null
    financialImpact: string | null
    estimatedCost: number | null
    actualCost: number | null
  }
  responsibility?: { responsibleParty: string; responsibilityNotes: string | null }
  communication?: { customerMessage: string | null }
}

export interface AssignableUser {
  id: string
  displayName: string
  roleName: string | null
}

export const DAMAGE_SEVERITIES = ['Minor', 'Moderate', 'Severe', 'TotalLoss'] as const
export type LinkedDamageSeverity = (typeof DAMAGE_SEVERITIES)[number]

/** Voorstel voor de ernst van de schademelding (zelfde regel als de server). */
export function suggestedDamageSeverity(severity: string): LinkedDamageSeverity {
  if (severity === 'Critical') return 'Severe'
  if (severity === 'Low') return 'Minor'
  return 'Moderate'
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
  /** Context bij registratie vanuit een rit (bv. /incidents/new?tripId=). */
  tripId?: string | null
  customerMessage?: string | null
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
