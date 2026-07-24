import { describe, expect, it, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { UnitTypeMasterEditor } from '../UnitTypeMasterEditor'
import { suggestUnitCode } from '../../unitCodeSuggestion'
import type { UnitTypeMaster } from '../../api/pricingApi'

const auth = vi.hoisted(() => ({ permissions: new Set<string>() }))

vi.mock('../../../auth/authContextValue', () => ({
  useAuth: () => ({ hasPermission: (code: string) => auth.permissions.has(code) }),
}))
vi.mock('../../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }),
}))

const api = vi.hoisted(() => ({
  units: [] as UnitTypeMaster[],
  create: vi.fn(),
  update: vi.fn(),
}))

vi.mock('../../api/pricingApi', async (importOriginal) => {
  const original = await importOriginal<typeof import('../../api/pricingApi')>()
  return {
    ...original,
    listUnitTypeMaster: () => Promise.resolve(api.units),
    createUnitTypeMaster: api.create,
    updateUnitTypeMaster: api.update,
  }
})

function makeUnit(overrides: Partial<UnitTypeMaster> = {}): UnitTypeMaster {
  return {
    id: 'u-1',
    code: 'EUROPALLET',
    name: 'Europallet',
    description: null,
    isActive: true,
    sortOrder: 0,
    allowForOrderEntry: true,
    allowForPricing: true,
    category: 'Packaging',
    decimals: 0,
    symbol: null,
    dimensionBehavior: 'DefaultButOverridable',
    defaultLengthCm: 120,
    defaultWidthCm: 80,
    defaultHeightCm: null,
    defaultWeightKg: null,
    maxWeightKg: null,
    defaultVolumeM3: null,
    defaultLoadingMeters: null,
    defaultPalletPlaces: null,
    ...overrides,
  }
}

describe('suggestUnitCode', () => {
  it('derives an uppercase alphanumeric code from the name', () => {
    expect(suggestUnitCode('Europallet')).toBe('EUROPALLET')
    expect(suggestUnitCode('Blokpallet A')).toBe('BLOKPALLETA')
    expect(suggestUnitCode('Kubieke méter')).toBe('KUBIEKEMETER')
  })
})

describe('UnitTypeMasterEditor', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    auth.permissions = new Set(['unit_types.view', 'unit_types.manage'])
    api.units = [makeUnit()]
    api.create.mockResolvedValue(makeUnit({ id: 'u-2' }))
  })

  it('lists units with dimensions, behaviour and flags', async () => {
    render(<UnitTypeMasterEditor />)

    expect(await screen.findByText('EUROPALLET')).toBeInTheDocument()
    expect(screen.getByText('120 × 80 cm')).toBeInTheDocument()
    expect(screen.getByText('Standaard, aanpasbaar')).toBeInTheDocument()
    expect(screen.getByText('Verpakking')).toBeInTheDocument()
  })

  it('suggests a code from the name but keeps it fully editable', async () => {
    const user = userEvent.setup()
    render(<UnitTypeMasterEditor />)

    await user.click(await screen.findByRole('button', { name: '+ Eenheid' }))
    await user.type(screen.getByLabelText(/Naam/), 'Blokpallet A')
    // The suggestion follows the name…
    expect(screen.getByLabelText(/Code/)).toHaveValue('BLOKPALLETA')

    // …until the user takes over; then the name stops influencing it.
    await user.clear(screen.getByLabelText(/Code/))
    await user.type(screen.getByLabelText(/Code/), 'BLOK-A')
    await user.type(screen.getByLabelText(/Naam/), ' XL')
    expect(screen.getByLabelText(/Code/)).toHaveValue('BLOK-A')

    await user.type(screen.getByLabelText('Lengte (cm)'), '120')
    await user.type(screen.getByLabelText('Breedte (cm)'), '100')
    await user.click(screen.getByRole('button', { name: 'Opslaan' }))

    await waitFor(() => expect(api.create).toHaveBeenCalled())
    const payload = api.create.mock.calls[0][0]
    expect(payload.code).toBe('BLOK-A')
    expect(payload.name).toBe('Blokpallet A XL')
    expect(payload.defaultLengthCm).toBe(120)
    expect(payload.defaultWidthCm).toBe(100)
  })

  it('is read-only without a manage permission', async () => {
    auth.permissions = new Set(['unit_types.view'])
    render(<UnitTypeMasterEditor />)

    expect(await screen.findByText('EUROPALLET')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: '+ Eenheid' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Bewerken' })).not.toBeInTheDocument()
  })
})
