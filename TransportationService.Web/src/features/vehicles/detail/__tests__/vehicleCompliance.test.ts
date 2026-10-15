import { describe, expect, it } from 'vitest'
import { buildUpcoming, buildVehicleAttention, type VehicleCompliance } from '../vehicleCompliance'
import type { VehicleDetail } from '../../types'
import type { FleetDocument } from '../../../fleet-documents/types'
import type { Inspection } from '../../../inspections/types'
import type { MaintenanceRecord } from '../../../maintenance/types'
import type { DamageReport } from '../../../damage/types'

const vehicle = { adrSuitable: false, hasCrane: false, hasRefrigeration: false } as VehicleDetail
const labels = {
  document: (d: { documentType: string; customTypeName: string | null }) => d.customTypeName ?? d.documentType,
  documentType: (type: string) => type,
  inspection: (i: { inspectionType: string }) => i.inspectionType,
  maintenance: (m: { maintenanceType: string }) => m.maintenanceType,
}
const none: VehicleCompliance = { documents: null, inspections: null, maintenance: null, damage: null, tachograph: null }
const doc = (over: Partial<FleetDocument>): FleetDocument =>
  ({ id: 'd', documentType: 'Insurance', customTypeName: null, expiryDate: null, issueDate: null, status: 'Valid', ...over }) as FleetDocument
const base = [doc({ id: 'r', documentType: 'Registration', status: 'NoExpiry' }), doc({ id: 'i', documentType: 'Insurance' }), doc({ id: 't', documentType: 'TechnicalInspection' })]
const today = new Date(2026, 9, 15)

describe('buildVehicleAttention', () => {
  it('claims nothing about lists it could not load', () => {
    expect(buildVehicleAttention({ ...vehicle, adrSuitable: true }, none, labels, today)).toEqual([])
  })

  it('only flags the newest document of a type (a renewed policy hides the expired one)', () => {
    const documents = [
      ...base.filter((d) => d.documentType !== 'Insurance'),
      doc({ id: 'old', expiryDate: '2025-10-01', status: 'Expired' }),
      doc({ id: 'new', expiryDate: '2026-10-28', status: 'ExpiringSoon' }),
    ]
    const facts = buildVehicleAttention(vehicle, { ...none, documents }, labels, today)
    expect(facts.map((f) => [f.kind, f.key])).toEqual([['documentExpiring', 'doc-new']])
  })

  it('names equipment without its certificate and missing base documents, quietly', () => {
    const facts = buildVehicleAttention({ ...vehicle, adrSuitable: true, hasCrane: true }, { ...none, documents: [doc({ id: 'r', documentType: 'Registration' })], inspections: [] }, labels, today)
    expect(facts.map((f) => [f.kind, f.severity, f.label ?? f.labels])).toEqual([
      ['equipmentDocumentMissing', 'warning', 'AdrCertificate'],
      ['equipmentDocumentMissing', 'warning', 'CraneInspection'],
      ['baseDocumentsMissing', 'info', ['Insurance', 'TechnicalInspection']],
    ])
  })

  it('treats a tracked vehicle inspection and crane inspection as covering their documents', () => {
    const inspections = [
      { id: 'a', inspectionType: 'VehicleInspection', completedDate: '2026-01-01', urgency: 'Completed', dueDate: '2026-01-01' },
      { id: 'b', inspectionType: 'CraneInspection', completedDate: '2026-01-01', urgency: 'Completed', dueDate: '2026-01-01' },
    ] as Inspection[]
    const facts = buildVehicleAttention({ ...vehicle, hasCrane: true }, { ...none, documents: base.slice(0, 2), inspections }, labels, today)
    expect(facts).toEqual([])
  })

  it('orders critical before warning before info and escalates severe damage', () => {
    const maintenance = [
      { id: 'm1', maintenanceType: 'Repair', status: 'Planned', isOverdue: true, scheduledDate: '2026-10-01' },
      { id: 'm2', maintenanceType: 'PeriodicService', status: 'Planned', isOverdue: false, scheduledDate: '2026-10-22' },
      { id: 'm3', maintenanceType: 'TireService', status: 'Planned', isOverdue: false, scheduledDate: '2026-12-22' },
    ] as MaintenanceRecord[]
    const damage = [
      { id: 'x', status: 'Reported', severity: 'Minor' },
      { id: 'y', status: 'Closed', severity: 'Severe' },
    ] as DamageReport[]
    const facts = buildVehicleAttention(vehicle, { ...none, maintenance, damage }, labels, today)
    expect(facts.map((f) => [f.kind, f.severity])).toEqual([
      ['maintenanceOverdue', 'critical'],
      ['damageOpen', 'warning'],
      ['maintenancePlanned', 'info'],
    ])
    expect(facts.find((f) => f.kind === 'damageOpen')?.count).toBe(1)
    const severe = buildVehicleAttention(vehicle, { ...none, damage: [{ id: 'z', status: 'InRepair', severity: 'Severe' } as DamageReport] }, labels, today)
    expect(severe[0].severity).toBe('critical')
  })
})

describe('buildUpcoming', () => {
  it('lists the next dated item per kind, soonest first', () => {
    const items = buildUpcoming(
      {
        ...none,
        maintenance: [{ id: 'm', maintenanceType: 'Repair', status: 'Planned', scheduledDate: '2026-11-03' }] as MaintenanceRecord[],
        inspections: [{ id: 'i', inspectionType: 'VehicleInspection', completedDate: null, dueDate: '2026-10-25' }] as Inspection[],
        documents: [doc({ id: 'p', expiryDate: '2027-01-09', status: 'Valid' })],
      },
      labels,
    )
    expect(items.map((i) => [i.key, i.date])).toEqual([
      ['inspection', '2026-10-25'],
      ['maintenance', '2026-11-03'],
      ['document', '2027-01-09'],
    ])
  })
})
