import { describe, expect, it, vi } from 'vitest'
import { render as rtlRender, screen, within } from '@testing-library/react'
import type { ReactElement } from 'react'
import { MemoryRouter } from 'react-router-dom'
import { IncidentChargePanel } from '../IncidentChargePanel'
import type { IncidentDetail } from '../../types'

// null = every permission (the default); a set = exactly those codes.
let permissions: Set<string> | null = null
vi.mock('../../../auth/authContextValue', () => ({
  useAuth: () => ({ hasPermission: (code: string) => permissions === null || permissions.has(code) }),
}))
vi.mock('../../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }),
}))
vi.mock('../../api/incidentsApi', () => ({
  createIncidentRedelivery: vi.fn(),
  decideIncidentCharge: vi.fn(),
  proposeIncidentCharge: vi.fn(),
}))

const render = (ui: ReactElement) => rtlRender(<MemoryRouter>{ui}</MemoryRouter>)

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

  it('asks for confirmation before creating a redelivery (M13)', async () => {
    const { default: userEvent } = await import('@testing-library/user-event')
    const api = await import('../../api/incidentsApi')
    const create = vi.spyOn(api, 'createIncidentRedelivery').mockResolvedValue(incident({ linkedRedeliveryOrderId: 'o-2', linkedRedeliveryOrderNumber: 'ORD-0002' }))
    const user = userEvent.setup()
    render(<IncidentChargePanel incident={incident({ transportOrderNumber: 'ORD-0049', dossierNumber: 'DOS-0048' })} onUpdated={vi.fn()} />)
    await user.click(screen.getByRole('button', { name: 'Herlevering aanmaken' }))
    expect(create).not.toHaveBeenCalled()
    const dialog = screen.getByRole('dialog')
    expect(dialog).toHaveTextContent('ORD-0049')
    expect(dialog).toHaveTextContent('DOS-0048')
    // Unpriced by default — billing is never duplicated silently.
    expect(dialog).toHaveTextContent('start zonder prijs')
    await user.click(within(dialog).getByRole('button', { name: 'Herlevering aanmaken' }))
    expect(create).toHaveBeenCalledWith('inc-1', { quantity: null, free: false, freeReason: null })
  })

  it('a free redelivery needs a reason and uses the confirmed-free mechanism', async () => {
    const { default: userEvent } = await import('@testing-library/user-event')
    const api = await import('../../api/incidentsApi')
    const create = vi.spyOn(api, 'createIncidentRedelivery').mockResolvedValue(incident({ linkedRedeliveryOrderId: 'o-2', linkedRedeliveryOrderNumber: 'ORD-0002' }))
    const user = userEvent.setup()
    render(<IncidentChargePanel incident={incident()} onUpdated={vi.fn()} />)
    await user.click(screen.getByRole('button', { name: 'Herlevering aanmaken' }))
    const dialog = screen.getByRole('dialog')
    await user.type(within(dialog).getByLabelText(/Te herleveren hoeveelheid/), '2')
    await user.click(within(dialog).getByLabelText(/Gratis herlevering/))
    expect(within(dialog).getByRole('button', { name: 'Herlevering aanmaken' })).toBeDisabled()
    await user.type(within(dialog).getByLabelText(/Reden gratis/), 'Eigen fout')
    await user.click(within(dialog).getByRole('button', { name: 'Herlevering aanmaken' }))
    expect(create).toHaveBeenCalledWith('inc-1', { quantity: 2, free: true, freeReason: 'Eigen fout' })
  })

  it('the dispatcher may redeliver through problems.create_redelivery', () => {
    permissions = new Set(['incidents.view', 'problems.create_redelivery'])
    try {
      render(<IncidentChargePanel incident={incident()} onUpdated={vi.fn()} />)
      expect(screen.getByRole('button', { name: 'Herlevering aanmaken' })).toBeInTheDocument()
    } finally {
      permissions = null
    }
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
