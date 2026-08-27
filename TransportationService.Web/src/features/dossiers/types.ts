import type { BadgeTone } from '../../components/ui/Badge'

export type DossierStatus = 'Open' | 'Closed'

export type DossierRelationType = 'FollowUp' | 'Return' | 'Claim' | 'Replacement' | 'Duplicate' | 'Other'

export interface DossierListItem {
  id: string
  dossierNumber: string
  title: string
  status: DossierStatus
  customerId: string | null
  customerName: string | null
  responsibleName: string | null
  orderCount: number
  openIncidentCount: number
  createdAt: string
}

export interface DossierOrder {
  linkId: string
  orderId: string
  orderNumber: string
  orderDate: string
  status: string
  goodsDescription: string | null
  agreedPrice: number | null
}

export interface DossierRelation {
  id: string
  relationType: DossierRelationType
  notes: string | null
  isOutgoing: boolean
  otherDossierId: string
  otherDossierNumber: string
  otherDossierTitle: string
}

export interface DossierIncident {
  id: string
  title: string
  incidentType: string
  status: string
  severity: string
  dueDate: string | null
}

export interface DossierFinancialSummary {
  agreedOrderTotal: number
  invoicedTotal: number
  estimatedIncidentCost: number
  actualIncidentCost: number
}

/** One activity card on the dossier: type capabilities + the linked execution record. */
export interface DossierActivity {
  id: string
  activityTypeId: string
  activityTypeCode: string
  activityTypeName: string
  icon: string | null
  hasStops: boolean
  supportsGoods: boolean
  allowsDuration: boolean
  sequence: number
  label: string | null
  linkedTransportOrderId: string | null
  linkedOrderNumber: string | null
  linkedOrderStatus: string | null
  linkedActivityId: string | null
  plannedDate: string | null
  durationHours: number | null
  notes: string | null
}

export type ReadinessSeverity = 'Info' | 'Warning' | 'Blocking'
export type ReadinessSection = 'algemeen' | 'activiteiten' | 'route' | 'goederen' | 'prijs'

/** One actionable attention item (additive readiness projection). */
export interface ReadinessIssue {
  code: string
  severity: ReadinessSeverity
  message: string
  section: ReadinessSection
  field: string | null
  stage: string
}

export interface DossierDetail {
  id: string
  dossierNumber: string
  title: string
  description: string | null
  status: DossierStatus
  customerId: string | null
  customerName: string | null
  responsibleUserId: string | null
  responsibleName: string | null
  closedAt: string | null
  notes: string | null
  createdAt: string
  orders: DossierOrder[]
  relations: DossierRelation[]
  incidents: DossierIncident[]
  financials: DossierFinancialSummary
  // --- Wave 1 dossier foundation ---
  customerReference: string | null
  dossierDate: string | null
  legalEntityId: string | null
  legalEntityName: string | null
  /** Concurrency token (uuid); echoed on every mutation — a mismatch yields 409 with the current state. */
  version: string
  activities: DossierActivity[]
  readiness: ReadinessIssue[]
}

export interface DossierInput {
  title: string
  description: string | null
  customerId: string | null
  responsibleUserId: string | null
  notes: string | null
  customerReference?: string | null
  dossierDate?: string | null
  /** Concurrency token; omitted = check skipped (legacy callers). */
  version?: string
}

/** Fast-create request (§8): customer is the ONLY required field. */
export interface NewDossierInput {
  customerId: string
  dossierDate?: string | null
  customerReference?: string | null
  /** Quick-start template tile; null/omitted = leeg dossier. */
  activityTypeId?: string | null
  title?: string | null
}

/** Add/update payload of one dossier activity (type immutable on update). */
export interface DossierActivityInput {
  activityTypeId: string
  label?: string | null
  plannedDate?: string | null
  durationHours?: number | null
  linkedActivityId?: string | null
  notes?: string | null
  /** HasStops types only: also create a draft order inside this dossier and link it. */
  createLinkedOrder?: boolean
  version?: string
}

/** Vertaalsleutels per status — renderen als t(DOSSIER_STATUS_LABELS[status]). */
export const DOSSIER_STATUS_LABELS: Record<DossierStatus, string> = {
  Open: 'dossiers.status.Open',
  Closed: 'dossiers.status.Closed',
}

export const DOSSIER_STATUS_TONE: Record<DossierStatus, BadgeTone> = {
  Open: 'success',
  Closed: 'neutral',
}

/** Vertaalsleutels per relatietype — renderen als t(DOSSIER_RELATION_LABELS[type]). */
export const DOSSIER_RELATION_LABELS: Record<DossierRelationType, string> = {
  FollowUp: 'dossiers.relationType.FollowUp',
  Return: 'dossiers.relationType.Return',
  Claim: 'dossiers.relationType.Claim',
  Replacement: 'dossiers.relationType.Replacement',
  Duplicate: 'dossiers.relationType.Duplicate',
  Other: 'dossiers.relationType.Other',
}
