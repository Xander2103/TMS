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
}

export interface DossierInput {
  title: string
  description: string | null
  customerId: string | null
  responsibleUserId: string | null
  notes: string | null
}

export const DOSSIER_STATUS_LABELS: Record<DossierStatus, string> = {
  Open: 'Open',
  Closed: 'Gesloten',
}

export const DOSSIER_STATUS_TONE: Record<DossierStatus, BadgeTone> = {
  Open: 'success',
  Closed: 'neutral',
}

export const DOSSIER_RELATION_LABELS: Record<DossierRelationType, string> = {
  FollowUp: 'Vervolgdossier',
  Return: 'Retour',
  Claim: 'Claim/schade',
  Replacement: 'Vervanging',
  Duplicate: 'Duplicaat',
  Other: 'Overig',
}
