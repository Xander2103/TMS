import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { IncidentChargePanel } from '../IncidentChargePanel'
import type { IncidentDetail } from '../../types'

vi.mock('../../../auth/authContextValue', () => ({
  useAuth: () => ({ hasPermission: () => true }),
}))
vi.mock('../../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }),
}))
vi.mock('../../api/incidentsApi', () => ({
  createIncidentRedelivery: vi.fn(),
  decideIncidentCharge: vi.fn(),
  proposeIncidentCharge: vi.fn(),
}))

function incident(overrides: Partial<IncidentDetail> = {}): IncidentDetail {
  return {
    id: 'inc-1',
    title: 'Mislukte levering',
    description: 'Klant afwezig',
    incidentType: 'WrongDelivery',
    customTypeName: null,
    status: 'New',
    severity: 'Medium',
    cause: null,
    responsibleUserId: null,
    responsibleName: null,
    customerImpact: null,
    operationalImpact: null,
    financialImpact: null,
    estimatedCost: null,
    actualCost: null,
    customerId: null,
    customerName: null,
    driverId: null,
    driverName: null,
    vehicleId: null,
    vehicleLabel: null,
    trailerId: null,
    trailerLabel: null,
    transportOrderId: 'order-1',
    transportOrderNumber: 'ORD-0001',
    tripId: null,
    tripNumber: null,
    dossierId: null,
    dossierNumber: null,
    dueDate: null,
    resolution: null,
    resolvedAt: null,
    createdAt: '2026-08-12T08:00:00Z',
    allowedStatusChanges: [],
    responsibleParty: 'Unknown',
    responsibilityNotes: null,
    chargeDecision: 'None',
    chargeAmount: null,
    chargeDescription: null,
    linkedRedeliveryOrderId: null,
    linkedRedeliveryOrderNumber: null,
    redeliverySuggested: false,
    ...overrides,
  }
}

describe('IncidentChargePanel', () => {
  it('shows the redelivery recommendation when suggested and no redelivery order exists yet', () => {
    render(<IncidentChargePanel incident={incident({ redeliverySuggested: true })} onUpdated={vi.fn()} />)

    expect(screen.getByRole('status')).toHaveTextContent(
      'Herlevering aanbevolen na mislukte levering — controleer en maak aan.',
    )
    expect(screen.getByRole('button', { name: 'Herlevering aanmaken' })).toBeInTheDocument()
  })

  it('hides the recommendation once a redelivery order is linked', () => {
    render(
      <IncidentChargePanel
        incident={incident({
          redeliverySuggested: true,
          linkedRedeliveryOrderId: 'order-2',
          linkedRedeliveryOrderNumber: 'ORD-0002',
        })}
        onUpdated={vi.fn()}
      />,
    )

    expect(screen.queryByRole('status')).not.toBeInTheDocument()
    expect(screen.getByText('ORD-0002')).toBeInTheDocument()
  })
})
