import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { TripInspector } from '../components/TripInspector'
import type { TripDetail } from '../../planning/types'
import type { PlanningResources } from '../types'

// UX-005: assigning without drag-and-drop, from the data the board already loaded.

const trip = (over: Partial<TripDetail> = {}) => ({
  id: 't-1', tripNumber: 'RIT-0238', tripDate: '2026-10-31', status: 'Draft', version: 'v-1',
  driverId: null, driverName: null, vehicleId: null, vehicleNumber: null, trailerId: null, trailerNumber: null,
  orders: [], overrides: [], allowedTransitions: ['Planned'],
  conflicts: [{ code: 'MissingDriver', blocking: true, description: 'Er is nog geen chauffeur toegewezen.', severity: 'Blocking' }],
  ...over,
}) as unknown as TripDetail

const resources = {
  from: '2026-10-31', to: '2026-10-31', truncated: false, limit: 200,
  drivers: [{ id: 'd-1', driverNumber: 'CH-1', name: 'An Peeters', availabilityStatus: 'Available', isActive: true, isBlocked: false, blockReason: null, fixedVehicleId: null, fixedVehicleNumber: null, assignments: [], absences: [], qualificationBlocks: [], qualificationWarnings: [] }],
  vehicles: [
    { id: 'v-1', internalNumber: 'VRT-1', licensePlate: '1-AAA-111', operationalStatus: 'Available', isActive: true, assignments: [], readiness: 'Blocked', readinessSummary: 'Zware schade' },
    { id: 'v-2', internalNumber: 'VRT-2', licensePlate: '1-BBB-222', operationalStatus: 'Available', isActive: true, assignments: [], readiness: 'Deployable', readinessSummary: null },
  ],
  trailers: [],
} as unknown as PlanningResources

function renderInspector(tripDetail: TripDetail, onAssign = vi.fn()) {
  render(
    <MemoryRouter>
      <TripInspector
        trip={tripDetail} isLoading={false} busy={false} resources={resources} onClose={vi.fn()} onPlan={vi.fn()} onRevertToDraft={vi.fn()}
        onRemoveOrder={vi.fn()} onMoveOrder={vi.fn()} onAssignResource={onAssign}
      />
    </MemoryRouter>,
  )
  return onAssign
}

describe('TripInspector pickers', () => {
  it('assigns a driver by keyboard, without drag-and-drop', async () => {
    const user = userEvent.setup()
    const onAssign = renderInspector(trip())
    await user.type(screen.getByLabelText('Chauffeur'), 'An Pe')
    await user.keyboard('{ArrowDown}{Enter}')
    expect(onAssign).toHaveBeenCalledWith(expect.objectContaining({ id: 't-1' }), 'driver', 'd-1', 'An Peeters (CH-1)')
  })

  it('states a blocked vehicle in the option itself, and leaves the decision to the server', async () => {
    const user = userEvent.setup()
    const onAssign = renderInspector(trip())
    await user.click(screen.getByLabelText('Voertuig'))
    expect(await screen.findByText(/Niet inzetbaar · Zware schade/)).toBeInTheDocument()
    await user.click(screen.getByText('VRT-1 · 1-AAA-111'))
    expect(onAssign).toHaveBeenCalledWith(expect.anything(), 'vehicle', 'v-1', 'VRT-1 · 1-AAA-111')
  })

  it('reads a missing resource on a draft as incomplete, not as an alarm', () => {
    renderInspector(trip())
    expect(screen.getByText('Onvolledig')).toBeInTheDocument()
    expect(screen.queryByText('Blokkerend')).not.toBeInTheDocument()
  })

  it('keeps the alarm once the trip runs, and no pickers there', () => {
    renderInspector(trip({ status: 'InProgress' as TripDetail['status'], driverName: 'An Peeters' }))
    expect(screen.getByText('Blokkerend')).toBeInTheDocument()
    expect(screen.queryByLabelText('Chauffeur')).not.toBeInTheDocument()
    expect(screen.getByText('An Peeters')).toBeInTheDocument()
  })
})
