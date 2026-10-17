import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MyActivitiesSection } from '../MyActivitiesSection'
import type { MyActivity } from '../../types'

const toast = vi.hoisted(() => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }))
vi.mock('../../../../components/ui/toastContext', () => ({ useToast: () => toast }))
const api = vi.hoisted(() => ({ listMyActivities: vi.fn(), startMyActivity: vi.fn(), completeMyActivity: vi.fn() }))
vi.mock('../../api/myTripsApi', () => api)

function activity(overrides: Partial<MyActivity> = {}): MyActivity {
  return {
    id: 'a1', dossierNumber: 'DOS-0099', customerName: 'TEST Klant', typeName: 'Kraanwerk ter plaatse', label: 'Kraan werf C',
    plannedDate: '2026-10-17', startTime: '08:00:00', durationHours: 3, plannedEnd: '2026-10-17T11:00:00',
    siteAddress: 'Werfstraat 1, Mechelen', workDescription: 'Dakkoepel plaatsen, 2,4 t', vehicleNumber: 'VRT-0003',
    vehiclePlate: '1-KRN-201', executionStatus: 'Planned', startedAt: null, completedAt: null, ...overrides,
  }
}

// T-H7: the operator sees the job (site, work description, time, vehicle) and starts / completes it.
describe('MyActivitiesSection', () => {
  beforeEach(() => vi.clearAllMocks())

  it('shows the job on site with its work description and no price', async () => {
    api.listMyActivities.mockResolvedValue([activity()])
    render(<MyActivitiesSection />)
    const card = await screen.findByRole('article', { name: 'Kraan werf C' })
    expect(within(card).getByText('Dakkoepel plaatsen, 2,4 t')).toBeInTheDocument()
    expect(within(card).getByText('Werfstraat 1, Mechelen')).toBeInTheDocument()
    expect(within(card).getByText(/08:00–11:00/)).toBeInTheDocument()
    expect(within(card).getByText('VRT-0003 (1-KRN-201)')).toBeInTheDocument()
    expect(card.textContent).not.toMatch(/€/)
  })

  it('starts, then completes only after confirmation', async () => {
    const user = userEvent.setup()
    api.listMyActivities.mockResolvedValue([activity()])
    api.startMyActivity.mockResolvedValue(activity({ executionStatus: 'InProgress' }))
    api.completeMyActivity.mockResolvedValue(activity({ executionStatus: 'Completed' }))
    render(<MyActivitiesSection />)

    await user.click(await screen.findByRole('button', { name: 'Starten' }))
    expect(api.startMyActivity).toHaveBeenCalledWith('a1')
    expect(await screen.findByText('Bezig')).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Afronden' }))
    expect(api.completeMyActivity).not.toHaveBeenCalled()
    await user.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Afronden' }))
    expect(api.completeMyActivity).toHaveBeenCalledWith('a1')
    expect(await screen.findByText('Afgerond')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Afronden' })).not.toBeInTheDocument()
  })

  it('renders nothing when the driver has no standalone work', async () => {
    api.listMyActivities.mockResolvedValue([])
    const { container } = render(<MyActivitiesSection />)
    await vi.waitFor(() => expect(api.listMyActivities).toHaveBeenCalled())
    expect(container).toBeEmptyDOMElement()
  })
})
