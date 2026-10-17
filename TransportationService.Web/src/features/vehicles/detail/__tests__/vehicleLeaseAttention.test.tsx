import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { buildVehicleAttention, type VehicleCompliance } from '../vehicleCompliance'
import type { VehicleDetail } from '../../types'
import type { LeasingContract } from '../../../fleet-compliance/leasingApi'
import { FleetDashboardPage } from '../../../fleet-dashboard/pages/FleetDashboardPage'

// FL-M9: a lease that ends within 30 days (or has ended) needs attention — on the vehicle and the dashboard.

const vehicle = { adrSuitable: false, hasCrane: false, hasRefrigeration: false } as VehicleDetail
const labels = { document: () => '', documentType: (t: string) => t, inspection: () => '', maintenance: () => '' }
const none: VehicleCompliance = { documents: null, inspections: null, maintenance: null, damage: null, tachograph: null }
const lease = (over: Partial<LeasingContract>): LeasingContract => ({ id: 'l1', leasingCompany: 'TEST Lease NV', isActive: true, endDate: null, ...over }) as LeasingContract
const today = new Date(2026, 9, 17)

describe('lease attention on the vehicle', () => {
  it('flags a lease ending within 30 days and one that has ended; ignores inactive or far-away ones', () => {
    const leasing = [
      lease({ id: 'soon', endDate: '2026-11-15' }),
      lease({ id: 'gone', endDate: '2026-10-01' }),
      lease({ id: 'far', endDate: '2027-06-01' }),
      lease({ id: 'off', endDate: '2026-10-20', isActive: false }),
    ]
    const facts = buildVehicleAttention(vehicle, { ...none, leasing }, labels, today)
    expect(facts.map((f) => [f.kind, f.key, f.severity, f.focus])).toEqual([
      ['leaseEnded', 'lease-gone', 'warning', 'leasing'],
      ['leaseEnding', 'lease-soon', 'info', 'leasing'],
    ])
  })
})

vi.mock('../../../fleet-dashboard/api/fleetDashboardApi', () => ({
  getFleetDashboard: () =>
    Promise.resolve({
      vehicles: { total: 1, available: 1, inUse: 0, inMaintenance: 0, outOfService: 0, inactive: 0 },
      trailers: { total: 0, available: 0, inUse: 0, inMaintenance: 0, outOfService: 0, inactive: 0 },
      maintenanceDueCount: 0, maintenanceDue: [], inspectionsDueCount: 0, inspectionsDue: [], documentsExpiringCount: 0, documentsExpiring: [],
      openDamageCount: 0, recentDamage: [], fuelWarnings: [],
      leasingEndingCount: 1,
      leasingEnding: [{ id: 'l1', vehicleId: 'v10', trailerId: null, ownerNumber: 'VRT-0010', ownerLicensePlate: '1-TFV-001', leasingCompany: 'TEST FLEET Lease NV', endDate: '2099-11-15' }],
    }),
}))
vi.mock('../../../auth/authContextValue', () => ({ useAuth: () => ({ hasPermission: () => true, hasAnyPermission: () => true }) }))

describe('fleet dashboard lease list', () => {
  it('lists the leases that end soon', async () => {
    render(<MemoryRouter><FleetDashboardPage /></MemoryRouter>)
    const panel = await screen.findByTestId('fd-leasing-ending')
    expect(panel).toHaveTextContent('Leasing eindigt')
    expect(panel).toHaveTextContent('VRT-0010')
    expect(panel).toHaveTextContent('TEST FLEET Lease NV')
  })
})
