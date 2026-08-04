import { describe, expect, it, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ServiceOptionsEditor } from '../ServiceOptionsEditor'
import type { ServiceOption, UnitTypeSettings } from '../../api/pricingApi'

const auth = vi.hoisted(() => ({ permissions: new Set<string>() }))

vi.mock('../../../auth/authContextValue', () => ({
  useAuth: () => ({ hasPermission: (code: string) => auth.permissions.has(code) }),
}))
vi.mock('../../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }),
}))

const state = vi.hoisted(() => ({
  options: [] as ServiceOption[],
  units: [] as UnitTypeSettings[],
  create: vi.fn(),
  update: vi.fn(),
}))

vi.mock('../../api/pricingApi', async (importOriginal) => {
  const original = await importOriginal<typeof import('../../api/pricingApi')>()
  return {
    ...original,
    listServiceOptions: () => Promise.resolve(state.options),
    listUnitTypeSettings: () => Promise.resolve(state.units),
    createServiceOption: state.create,
    updateServiceOption: state.update,
    deleteServiceOption: vi.fn(),
  }
})

function makeOption(overrides: Partial<ServiceOption> = {}): ServiceOption {
  return {
    id: 'opt-1',
    code: 'PICK',
    name: 'Picking',
    kind: 'PerUnit',
    defaultValue: 1.25,
    isActive: true,
    sortOrder: 0,
    description: null,
    invoiceDescription: null,
    selectableInOrders: true,
    unitTypeId: 'u-colli',
    unitTypeName: 'colli',
    autoApply: true,
    onlyForAdr: false,
    ...overrides,
  }
}

describe('ServiceOptionsEditor — warehouse calculation bases', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    auth.permissions = new Set(['tariffs.view', 'tariffs.manage'])
    state.options = [makeOption()]
    state.units = [
      { id: 'u-colli', code: 'COLLI', name: 'colli', isActive: true, sortOrder: 0, allowForOrderEntry: true, allowForPricing: true },
    ]
    state.create.mockResolvedValue(makeOption())
  })

  it('shows the per-unit value with the unit name and an Automatisch badge for an auto-apply service', async () => {
    render(<ServiceOptionsEditor />)

    expect(await screen.findByText('Picking')).toBeInTheDocument()
    expect(screen.getByText('€ 1.25/colli')).toBeInTheDocument()
    expect(screen.getByText('Automatisch')).toBeInTheDocument()
  })

  it('offers every new calculation basis in the Berekeningswijze select', async () => {
    const user = userEvent.setup()
    render(<ServiceOptionsEditor />)

    await user.click(await screen.findByRole('button', { name: '+ Dienst' }))
    const dialog = await screen.findByRole('dialog')
    const kindSelect = within(dialog).getByLabelText(/Berekeningswijze/)

    for (const label of ['Per eenheid…', 'Per orderlijn', 'Per kg', 'Per m³', 'Per laadmeter', 'Per dag', 'Per pallet/dag']) {
      expect(within(dialog).getByRole('option', { name: label })).toBeInTheDocument()
    }

    // Selecting "Per eenheid" reveals the unit select (required to resolve the quantity).
    await user.selectOptions(kindSelect, 'PerUnit')
    expect(within(dialog).getByLabelText('Eenheid *')).toBeInTheDocument()
  })

  it('saves AutoApply and OnlyForAdr and the chosen unit for a new PerUnit service', async () => {
    const user = userEvent.setup()
    render(<ServiceOptionsEditor />)

    await user.click(await screen.findByRole('button', { name: '+ Dienst' }))
    const dialog = await screen.findByRole('dialog')

    await user.type(within(dialog).getByLabelText('Code *'), 'PALUIT')
    await user.type(within(dialog).getByLabelText('Naam *'), 'PAL UIT')
    await user.selectOptions(within(dialog).getByLabelText(/Berekeningswijze/), 'PerUnit')
    await user.selectOptions(within(dialog).getByLabelText('Eenheid *'), 'u-colli')
    await user.click(within(dialog).getByLabelText(/Automatisch toepassen/))
    await user.click(within(dialog).getByLabelText('Alleen bij ADR'))
    await user.click(within(dialog).getByRole('button', { name: 'Opslaan' }))

    await waitFor(() => expect(state.create).toHaveBeenCalled())
    const payload = state.create.mock.calls[0][0]
    expect(payload).toEqual(expect.objectContaining({
      code: 'PALUIT', kind: 'PerUnit', unitTypeId: 'u-colli', autoApply: true, onlyForAdr: true,
    }))
  })
})

describe('ServiceOptionsEditor — time-based conditions (wave 2026-08-04 §16/§17)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    auth.permissions = new Set(['tariffs.view', 'tariffs.manage'])
    state.options = [makeOption()]
    state.units = []
    state.create.mockResolvedValue(makeOption())
  })

  it('saves a configured "Lossen vóór 10:00" condition with priority', async () => {
    const user = userEvent.setup()
    render(<ServiceOptionsEditor />)

    await user.click(await screen.findByRole('button', { name: '+ Dienst' }))
    const dialog = await screen.findByRole('dialog')

    await user.type(within(dialog).getByLabelText('Code *'), 'VOOR10')
    await user.type(within(dialog).getByLabelText('Naam *'), 'Levering vóór 10u')
    await user.click(within(dialog).getByRole('button', { name: '+ Tijdsvoorwaarde' }))

    await user.selectOptions(within(dialog).getByLabelText('Voorwaarde'), 'StopTimeBefore')
    await user.selectOptions(within(dialog).getByLabelText('Stoptype'), 'Unloading')
    await user.type(within(dialog).getByLabelText('Uur'), '10:00')
    const priorityInput = within(dialog).getByLabelText('Prioriteit')
    await user.clear(priorityInput)
    await user.type(priorityInput, '1')

    await user.click(within(dialog).getByRole('button', { name: 'Opslaan' }))

    await waitFor(() => expect(state.create).toHaveBeenCalled())
    const payload = state.create.mock.calls[0][0]
    expect(payload.timeConditions).toEqual([
      { kind: 'StopTimeBefore', stopScope: 'Unloading', timeOfDay: '10:00', priority: 1, allowStacking: false },
    ])
  })

  it('shows a summary badge for a stored time condition', async () => {
    state.options = [
      makeOption({
        timeConditions: [
          { kind: 'StopTimeBefore', stopScope: 'Unloading', timeOfDay: '10:00:00', priority: 1, allowStacking: false },
        ],
      }),
    ]
    render(<ServiceOptionsEditor />)

    expect(await screen.findByText('Lossen vóór 10:00 (prioriteit 1)')).toBeInTheDocument()
  })
})
