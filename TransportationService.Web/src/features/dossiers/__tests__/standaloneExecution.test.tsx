import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ApiError } from '../../../api/apiClient'
import { ActivityCard } from '../components/ActivityCard'
import { StandaloneExecutionBlock } from '../components/StandaloneExecutionBlock'
import type { DossierActivity } from '../types'
import { dossierActivity, dossierDetail } from './fixtures'

/**
 * T-H7 (2026-10-16) — a standalone activity (crane work on site, plateau) is executable: status
 * badge, Start / Afronden / Annuleren (reason) / Terugzetten, and driver + vehicle through the
 * planning conflict flow. The card shows status, driver, plate and the time window.
 */

const auth = vi.hoisted(() => ({ permissions: new Set<string>() }))
vi.mock('../../auth/authContextValue', () => ({
  useAuth: () => ({
    hasPermission: (code: string) => auth.permissions.has(code),
    hasAnyPermission: (codes: string[]) => codes.some((code) => auth.permissions.has(code)),
  }),
}))
const toast = vi.hoisted(() => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }))
vi.mock('../../../components/ui/toastContext', () => ({ useToast: () => toast }))

const api = vi.hoisted(() => ({
  assignStandaloneActivity: vi.fn(),
  cancelStandaloneActivity: vi.fn(),
  transitionStandaloneActivity: vi.fn(),
}))
vi.mock('../api/dossiersApi', () => api)

vi.mock('../../planning/usePlanningResources', () => ({
  usePlanningResources: () => ({
    vehicles: [{ id: 'v-kr', internalNumber: 'KR-1', licensePlate: '1-KRA-001' }],
    trailers: [],
    status: { vehicles: 'ready', trailers: 'ready' },
  }),
}))
// PERF-H3: the driver picker searches the server per keystroke instead of a preloaded list.
vi.mock('../../drivers/api/driversApi', () => ({
  searchDrivers: () =>
    Promise.resolve({
      items: [{ id: 'd-jan', driverNumber: 'CH-001', fullName: 'Jan Peeters', employeeNumber: 'P-1', categoryName: null, availabilityStatus: 'Available', isActive: true, isBlocked: false }],
      totalCount: 1,
      page: 1,
      pageSize: 25,
    }),
  getDriver: () => Promise.resolve({ id: 'd-jan', fixedVehicle: null }),
}))

function crane(overrides: Partial<DossierActivity> = {}): DossierActivity {
  return dossierActivity({
    id: 'a-crane',
    activityTypeCode: 'KRAANWERK',
    activityTypeName: 'Kraanwerk ter plaatse',
    hasStops: false,
    supportsGoods: false,
    allowsDuration: true,
    linkedTransportOrderId: null,
    linkedOrderNumber: null,
    linkedOrderStatus: null,
    label: 'Kraan werf B',
    plannedDate: '2026-09-22',
    durationHours: 2.5,
    planningRelevant: true,
    isExecutable: true,
    requiresCrane: true,
    executionStatus: 'Planned',
    ...overrides,
  })
}

function renderBlock(activity: DossierActivity, { drawerDirty = false, canManage = true } = {}) {
  const onApply = vi.fn()
  const onConflict = vi.fn(() => false)
  const dossier = dossierDetail({ activities: [activity] })
  render(
    <StandaloneExecutionBlock
      dossier={dossier}
      activity={activity}
      canManage={canManage}
      drawerDirty={drawerDirty}
      onApply={onApply}
      onConflict={onConflict}
    />,
  )
  return { onApply, dossier, block: within(screen.getByRole('region', { name: 'Uitvoering' })) }
}

describe('StandaloneExecutionBlock', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    auth.permissions = new Set(['dossiers.manage', 'planning.edit'])
  })

  it('shows the status and completes the activity through the complete endpoint', async () => {
    const user = userEvent.setup()
    const updated = dossierDetail({ activities: [crane({ executionStatus: 'Completed' })] })
    api.transitionStandaloneActivity.mockResolvedValue(updated)
    const { onApply, block, dossier } = renderBlock(crane())

    expect(block.getByText('Gepland')).toBeInTheDocument()
    await user.click(block.getByRole('button', { name: 'Afronden' }))

    await waitFor(() => expect(onApply).toHaveBeenCalledWith(updated))
    expect(api.transitionStandaloneActivity).toHaveBeenCalledWith(dossier.id, 'a-crane', 'complete', dossier.version)
    expect(toast.showSuccess).toHaveBeenCalledWith('Activiteit afgerond.')
  })

  it('starts a planned activity and refuses status changes while the drawer has unsaved edits', async () => {
    const user = userEvent.setup()
    api.transitionStandaloneActivity.mockResolvedValue(dossierDetail())
    const { block } = renderBlock(crane(), { drawerDirty: true })

    await user.click(block.getByRole('button', { name: 'Start' }))

    expect(api.transitionStandaloneActivity).not.toHaveBeenCalled()
    expect(block.getByRole('alert')).toHaveTextContent('Sla eerst je wijzigingen op')
  })

  it('cancels only with a reason', async () => {
    const user = userEvent.setup()
    api.cancelStandaloneActivity.mockResolvedValue(dossierDetail())
    const { block, onApply, dossier } = renderBlock(crane())

    await user.click(block.getByRole('button', { name: 'Annuleren' }))
    const dialog = within(screen.getByRole('dialog'))
    await user.click(dialog.getByRole('button', { name: 'Activiteit annuleren' }))
    expect(api.cancelStandaloneActivity).not.toHaveBeenCalled()

    await user.type(dialog.getByLabelText(/Reden/), 'Klant had geen kraan nodig')
    await user.click(dialog.getByRole('button', { name: 'Activiteit annuleren' }))

    await waitFor(() => expect(onApply).toHaveBeenCalled())
    expect(api.cancelStandaloneActivity).toHaveBeenCalledWith(dossier.id, 'a-crane', 'Klant had geen kraan nodig', dossier.version)
  })

  it('offers "Terugzetten" instead of the actions once the activity is resolved', () => {
    const { block } = renderBlock(crane({ executionStatus: 'Cancelled', cancellationReason: 'Weer' }))

    expect(block.getByText('Geannuleerd')).toBeInTheDocument()
    expect(block.getByText('Reden annulatie: Weer')).toBeInTheDocument()
    expect(block.queryByRole('button', { name: 'Afronden' })).not.toBeInTheDocument()
    expect(block.getByRole('button', { name: 'Terugzetten naar gepland' })).toBeInTheDocument()
  })

  it('opens the conflict dialog when the assignment hits a blocking planning conflict', async () => {
    const user = userEvent.setup()
    api.assignStandaloneActivity.mockRejectedValue(
      new ApiError('Conflict', 409, {
        message: 'De toewijzing botst met blokkerende planningsconflicten.',
        conflicts: [{ code: 'DriverAbsent', blocking: true, severity: 'Blocking', description: 'Chauffeur Jan Peeters is afwezig op 22/09/2026 (Verlof).' }],
      }),
    )
    const { block } = renderBlock(crane())

    await user.click(block.getByLabelText('Chauffeur'))
    await user.click(await screen.findByRole('option', { name: /Jan Peeters/ }))
    await user.click(block.getByRole('button', { name: 'Toewijzing opslaan' }))

    expect(await screen.findByText(/afwezig op 22\/09\/2026/)).toBeInTheDocument()
    expect(api.assignStandaloneActivity).toHaveBeenCalledWith('d-1', 'a-crane', expect.objectContaining({ driverId: 'd-jan', override: false }))
  })

  it('is read-only without dossier or planning rights', () => {
    auth.permissions = new Set()
    const { block } = renderBlock(crane({ driverName: 'Jan Peeters', driverId: 'd-jan' }), { canManage: false })

    expect(block.queryByRole('button')).not.toBeInTheDocument()
    expect(block.getByText('Jan Peeters')).toBeInTheDocument()
  })
})

describe('ActivityCard — standalone execution', () => {
  it('shows the status badge, driver, plate and the time window of a standalone activity', () => {
    const activity = crane({
      executionStatus: 'InProgress',
      startTime: '07:30:00',
      plannedEnd: '2026-09-22T10:00:00',
      driverName: 'Jan Peeters',
      vehiclePlate: '1-KRA-001',
      vehicleNumber: 'KR-1',
    })
    render(
      <ul>
        <ActivityCard activity={activity} activities={[activity]} dossier={dossierDetail({ activities: [activity] })} onOpen={vi.fn()} />
      </ul>,
    )
    const card = within(screen.getByRole('listitem'))

    expect(card.getByText('Bezig')).toBeInTheDocument()
    expect(card.getByText('Jan Peeters')).toBeInTheDocument()
    expect(card.getByText('1-KRA-001')).toBeInTheDocument()
    expect(card.getByText(/07:30–10:00/)).toBeInTheDocument()
  })
})
