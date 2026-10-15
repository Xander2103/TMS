import type { FleetDocument, FleetDocumentType } from '../../fleet-documents/types'
import type { Inspection } from '../../inspections/types'
import type { MaintenanceRecord } from '../../maintenance/types'
import type { DamageReport } from '../../damage/types'
import type { TachographCalibration } from '../../fleet-compliance/tachographApi'
import type { VehicleDetail } from '../types'

/**
 * Everything the vehicle overview needs to answer "is anything expiring, due, damaged or
 * missing?" — the vehicle's own lists, loaded in parallel. `null` = not loaded (no permission
 * or the request failed): a missing list never produces a claim in either direction.
 */
export interface VehicleCompliance {
  documents: FleetDocument[] | null
  inspections: Inspection[] | null
  maintenance: MaintenanceRecord[] | null
  damage: DamageReport[] | null
  tachograph: TachographCalibration[] | null
}

/** The sub-block of a grouped section an attention item or overview link jumps to (`?focus=`). */
export type VehicleFocus =
  | 'documenten'
  | 'keuringen'
  | 'tachograaf'
  | 'onderhoud'
  | 'schade'
  | 'toewijzing'
  | 'leasing'
  | 'brandstof'
  | 'kpi'

export type VehicleTab = 'overzicht' | 'techniek' | 'documenten' | 'onderhoud' | 'kosten' | 'historiek'

/** Which grouped section hosts each sub-block. */
export const FOCUS_TAB: Record<VehicleFocus, VehicleTab> = {
  documenten: 'documenten',
  keuringen: 'documenten',
  tachograaf: 'documenten',
  onderhoud: 'onderhoud',
  schade: 'onderhoud',
  toewijzing: 'techniek',
  leasing: 'kosten',
  brandstof: 'kosten',
  kpi: 'kosten',
}

export type VehicleAttentionKind =
  | 'documentExpired'
  | 'documentExpiring'
  | 'inspectionOverdue'
  | 'inspectionDue'
  | 'tachographOverdue'
  | 'tachographDue'
  | 'maintenanceOverdue'
  | 'maintenanceInProgress'
  | 'maintenancePlanned'
  | 'damageOpen'
  | 'equipmentDocumentMissing'
  | 'baseDocumentsMissing'

export interface VehicleAttentionFact {
  key: string
  kind: VehicleAttentionKind
  severity: 'critical' | 'warning' | 'info'
  focus: VehicleFocus
  /** Translation key (fleet doc type, inspection type, maintenance type) or custom name. */
  label?: string
  /** Several labels (missing base documents). */
  labels?: string[]
  date?: string | null
  count?: number
}

const OPEN_DAMAGE = new Set(['Reported', 'UnderAssessment', 'InRepair'])
const SEVERE_DAMAGE = new Set(['Severe', 'TotalLoss'])
/** Maintenance planned this close by is worth a quiet line on the overview. */
const PLANNED_WINDOW_DAYS = 14

function daysUntil(date: string, today: Date): number {
  const target = new Date(`${date.slice(0, 10)}T00:00:00`)
  const start = new Date(today.getFullYear(), today.getMonth(), today.getDate())
  return Math.round((target.getTime() - start.getTime()) / 86_400_000)
}

/** Only the newest document of each type counts: an expired policy replaced by a new one is history. */
function latestPerType(documents: FleetDocument[]): FleetDocument[] {
  const byType = new Map<string, FleetDocument>()
  for (const doc of documents) {
    const key = doc.documentType === 'Other' ? `Other:${doc.customTypeName ?? doc.id}` : doc.documentType
    const current = byType.get(key)
    const docTime = doc.expiryDate ?? doc.issueDate ?? ''
    const currentTime = current ? (current.expiryDate ?? current.issueDate ?? '') : ''
    if (!current || docTime > currentTime) byType.set(key, doc)
  }
  return [...byType.values()]
}

interface LabelResolvers {
  document: (doc: FleetDocument) => string
  documentType: (type: FleetDocumentType) => string
  inspection: (inspection: Inspection) => string
  maintenance: (record: MaintenanceRecord) => string
}

/**
 * Builds the overview's attention facts from the backend's own classifications (document
 * status, inspection urgency, maintenance isOverdue, tachograph status) — this function never
 * re-decides whether something is expired; it only selects what needs a person's action. The
 * one derived check is "equipment without its certificate": ADR / crane / refrigeration flags
 * on the vehicle with no such document (or crane inspection) registered at all. It states that
 * nothing is registered — never that the vehicle is unsafe.
 */
export function buildVehicleAttention(
  vehicle: VehicleDetail,
  data: VehicleCompliance,
  labels: LabelResolvers,
  today: Date = new Date(),
): VehicleAttentionFact[] {
  const facts: VehicleAttentionFact[] = []

  if (data.documents) {
    for (const doc of latestPerType(data.documents)) {
      if (doc.status === 'Expired')
        facts.push({ key: `doc-${doc.id}`, kind: 'documentExpired', severity: 'critical', focus: 'documenten', label: labels.document(doc), date: doc.expiryDate })
      else if (doc.status === 'ExpiringSoon')
        facts.push({ key: `doc-${doc.id}`, kind: 'documentExpiring', severity: 'warning', focus: 'documenten', label: labels.document(doc), date: doc.expiryDate })
    }
  }

  if (data.inspections) {
    for (const inspection of data.inspections) {
      if (inspection.completedDate) continue
      if (inspection.urgency === 'Overdue')
        facts.push({ key: `insp-${inspection.id}`, kind: 'inspectionOverdue', severity: 'critical', focus: 'keuringen', label: labels.inspection(inspection), date: inspection.dueDate })
      else if (inspection.urgency === 'DueSoon')
        facts.push({ key: `insp-${inspection.id}`, kind: 'inspectionDue', severity: 'warning', focus: 'keuringen', label: labels.inspection(inspection), date: inspection.dueDate })
    }
  }

  if (data.tachograph && data.tachograph.length > 0) {
    const latest = [...data.tachograph].sort((a, b) => b.nextCalibrationDue.localeCompare(a.nextCalibrationDue))[0]
    if (latest.status === 'Overdue')
      facts.push({ key: `tacho-${latest.id}`, kind: 'tachographOverdue', severity: 'critical', focus: 'tachograaf', date: latest.nextCalibrationDue })
    else if (latest.status === 'ExpiringSoon')
      facts.push({ key: `tacho-${latest.id}`, kind: 'tachographDue', severity: 'warning', focus: 'tachograaf', date: latest.nextCalibrationDue })
  }

  if (data.maintenance) {
    for (const record of data.maintenance) {
      if (record.status !== 'Planned' && record.status !== 'InProgress') continue
      if (record.isOverdue)
        facts.push({ key: `mnt-${record.id}`, kind: 'maintenanceOverdue', severity: 'critical', focus: 'onderhoud', label: labels.maintenance(record), date: record.scheduledDate })
      else if (record.status === 'InProgress')
        facts.push({ key: `mnt-${record.id}`, kind: 'maintenanceInProgress', severity: 'info', focus: 'onderhoud', label: labels.maintenance(record) })
      else if (record.scheduledDate && daysUntil(record.scheduledDate, today) <= PLANNED_WINDOW_DAYS)
        facts.push({ key: `mnt-${record.id}`, kind: 'maintenancePlanned', severity: 'info', focus: 'onderhoud', label: labels.maintenance(record), date: record.scheduledDate })
    }
  }

  if (data.damage) {
    const open = data.damage.filter((report) => OPEN_DAMAGE.has(report.status))
    if (open.length > 0)
      facts.push({
        key: 'damage-open',
        kind: 'damageOpen',
        severity: open.some((report) => SEVERE_DAMAGE.has(report.severity)) ? 'critical' : 'warning',
        focus: 'schade',
        count: open.length,
      })
  }

  if (data.documents) {
    const types = new Set(data.documents.map((doc) => doc.documentType))
    const craneInspected = (data.inspections ?? []).some((i) => i.inspectionType === 'CraneInspection')
    const equipment: [boolean, FleetDocumentType, boolean][] = [
      [vehicle.adrSuitable, 'AdrCertificate', false],
      [vehicle.hasCrane, 'CraneInspection', craneInspected],
      [vehicle.hasRefrigeration, 'RefrigerationCertificate', false],
    ]
    for (const [hasEquipment, type, coveredElsewhere] of equipment) {
      if (hasEquipment && !types.has(type) && !coveredElsewhere)
        facts.push({ key: `missing-${type}`, kind: 'equipmentDocumentMissing', severity: 'warning', focus: 'documenten', label: labels.documentType(type) })
    }
    // A periodic technical inspection tracked under Keuringen covers the "Technische keuring" document.
    const vehicleInspected = (data.inspections ?? []).some((i) => i.inspectionType === 'VehicleInspection')
    const base = (['Registration', 'Insurance', 'TechnicalInspection'] as FleetDocumentType[]).filter(
      (type) => !types.has(type) && !(type === 'TechnicalInspection' && vehicleInspected),
    )
    if (base.length > 0)
      facts.push({ key: 'missing-base', kind: 'baseDocumentsMissing', severity: 'info', focus: 'documenten', labels: base.map(labels.documentType) })
  }

  const order = { critical: 0, warning: 1, info: 2 }
  return facts.sort((a, b) => order[a.severity] - order[b.severity])
}

export interface UpcomingItem {
  key: string
  focus: VehicleFocus
  label: string
  date: string
}

/** The next dated thing per kind — what the fleet manager plans around. */
export function buildUpcoming(data: VehicleCompliance, labels: LabelResolvers): UpcomingItem[] {
  const items: UpcomingItem[] = []
  const nextMaintenance = (data.maintenance ?? [])
    .filter((r) => r.status === 'Planned' && r.scheduledDate)
    .sort((a, b) => a.scheduledDate!.localeCompare(b.scheduledDate!))[0]
  if (nextMaintenance) items.push({ key: 'maintenance', focus: 'onderhoud', label: labels.maintenance(nextMaintenance), date: nextMaintenance.scheduledDate! })
  const nextInspection = (data.inspections ?? []).filter((i) => !i.completedDate).sort((a, b) => a.dueDate.localeCompare(b.dueDate))[0]
  if (nextInspection) items.push({ key: 'inspection', focus: 'keuringen', label: labels.inspection(nextInspection), date: nextInspection.dueDate })
  const latestTacho = [...(data.tachograph ?? [])].sort((a, b) => b.nextCalibrationDue.localeCompare(a.nextCalibrationDue))[0]
  if (latestTacho) items.push({ key: 'tachograph', focus: 'tachograaf', label: 'tachograph', date: latestTacho.nextCalibrationDue })
  const nextExpiry = latestPerType(data.documents ?? [])
    .filter((d) => d.expiryDate && d.status !== 'Expired')
    .sort((a, b) => a.expiryDate!.localeCompare(b.expiryDate!))[0]
  if (nextExpiry) items.push({ key: 'document', focus: 'documenten', label: labels.document(nextExpiry), date: nextExpiry.expiryDate! })
  return items.sort((a, b) => a.date.localeCompare(b.date))
}
