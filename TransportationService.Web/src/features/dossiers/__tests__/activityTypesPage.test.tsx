import { describe, expect, it, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { ActivityTypesPage } from '../pages/ActivityTypesPage'
import type { ActivityType } from '../api/activityTypesApi'

const auth = vi.hoisted(() => ({ permissions: new Set<string>(['activity_types.view', 'activity_types.manage']) }))

vi.mock('../../auth/authContextValue', () => ({
  useAuth: () => ({ hasPermission: (code: string) => auth.permissions.has(code) }),
}))
vi.mock('../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }),
}))

const state = vi.hoisted(() => ({
  types: [] as ActivityType[],
  create: vi.fn(),
  update: vi.fn(),
  remove: vi.fn(),
}))

vi.mock('../api/activityTypesApi', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../api/activityTypesApi')>()),
  listActivityTypes: () => Promise.resolve(state.types),
  createActivityType: state.create,
  updateActivityType: state.update,
  removeActivityType: state.remove,
}))

function activityType(overrides: Partial<ActivityType> = {}): ActivityType {
  return {
    id: 'at-1',
    code: 'DIRECT_TRANSPORT',
    name: 'Direct transport',
    isActive: true,
    sortOrder: 1,
    icon: 'truck',
    kpiCategory: 'Transport',
    hasStops: true,
    supportsGoods: true,
    planningRelevant: true,
    warehouseRelevant: false,
    allowsDuration: false,
    isQuickStart: true,
    quickStartOrder: 2,
    isSystemDefaultTransport: true,
    ...overrides,
  }
}

describe('ActivityTypesPage', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    auth.permissions = new Set(['activity_types.view', 'activity_types.manage'])
    state.types = [
      activityType(),
      activityType({
        id: 'at-2', code: 'KRAANWERK', name: 'Kraanwerk ter plaatse', icon: 'crane',
        hasStops: false, supportsGoods: false, allowsDuration: true,
        isQuickStart: false, quickStartOrder: 0, isSystemDefaultTransport: false,
      }),
    ]
    state.create.mockResolvedValue(activityType({ id: 'at-3', code: 'OPSLAG', name: 'Opslag' }))
  })

  it('lists the types with code, capability badges and status', async () => {
    render(
      <MemoryRouter>
        <ActivityTypesPage />
      </MemoryRouter>,
    )

    expect(await screen.findByText('Direct transport')).toBeInTheDocument()
    expect(screen.getByText('KRAANWERK')).toBeInTheDocument()
    expect(screen.getByText('Standaard transport')).toBeInTheDocument()
    // Capability badges follow the flags: one transport-executed type, one with duration.
    expect(screen.getAllByText('Transportopdracht')).toHaveLength(1)
    expect(screen.getAllByText('Duur')).toHaveLength(1)
    expect(screen.getAllByText('Actief').length).toBeGreaterThanOrEqual(2)
  })

  it('creates a type through the dialog', async () => {
    const user = userEvent.setup()
    render(
      <MemoryRouter>
        <ActivityTypesPage />
      </MemoryRouter>,
    )

    await user.click(await screen.findByRole('button', { name: 'Nieuw activiteitstype' }))
    await user.type(screen.getByLabelText(/^Code/), 'OPSLAG')
    await user.type(screen.getByLabelText(/^Naam/), 'Opslag')
    await user.click(screen.getByLabelText(/Magazijnrelevant/))
    await user.click(screen.getByRole('button', { name: 'Opslaan' }))

    await waitFor(() =>
      expect(state.create).toHaveBeenCalledWith(
        expect.objectContaining({
          code: 'OPSLAG',
          name: 'Opslag',
          warehouseRelevant: true,
          hasStops: false,
          isActive: true,
        }),
      ),
    )
  })

  it('hides every manage action without activity_types.manage', async () => {
    auth.permissions = new Set(['activity_types.view'])
    render(
      <MemoryRouter>
        <ActivityTypesPage />
      </MemoryRouter>,
    )

    expect(await screen.findByText('Direct transport')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Nieuw activiteitstype' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Bewerken' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Verwijderen' })).not.toBeInTheDocument()
  })
})
