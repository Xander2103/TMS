import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { EmployeeTankCardsSection } from '../EmployeeTankCardsSection'
import type { TankCard } from '../../../tank-cards/types'

vi.mock('../../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }),
}))

// Mutable permission set so each test controls what useAuth() reports.
const auth = vi.hoisted(() => ({ permissions: ['tank_cards.view', 'tank_cards.edit', 'tank_cards.create'] }))

vi.mock('../../../auth/authContextValue', () => ({
  useAuth: () => ({
    status: 'authenticated' as const,
    user: null,
    login: vi.fn(),
    logout: vi.fn(),
    hasPermission: (code: string) => auth.permissions.includes(code),
    hasAnyPermission: (codes: string[]) => codes.some((code) => auth.permissions.includes(code)),
  }),
}))

const api = vi.hoisted(() => ({
  listEmployeeTankCards: vi.fn(),
  searchTankCards: vi.fn(),
  updateTankCard: vi.fn(),
  createTankCard: vi.fn(),
}))

vi.mock('../../../tank-cards/api/tankCardsApi', () => ({
  listEmployeeTankCards: (...args: unknown[]) => api.listEmployeeTankCards(...args),
  searchTankCards: (...args: unknown[]) => api.searchTankCards(...args),
  updateTankCard: (...args: unknown[]) => api.updateTankCard(...args),
  createTankCard: (...args: unknown[]) => api.createTankCard(...args),
}))

function makeCard(overrides: Partial<TankCard>): TankCard {
  return {
    id: 'card-1',
    cardNumber: '1234567812345678',
    provider: 'DKV',
    vehicleId: null,
    vehicleInternalNumber: null,
    vehicleLicensePlate: null,
    driverId: null,
    driverName: null,
    employeeId: null,
    employeeName: null,
    validFrom: null,
    validUntil: '2027-01-01',
    status: 'Active',
    isBlocked: false,
    blockedReason: null,
    internalName: null,
    fuelType: null,
    dailyLimit: null,
    weeklyLimit: null,
    monthlyLimit: null,
    costCenter: null,
    notes: null,
    ...overrides,
  }
}

describe('EmployeeTankCardsSection', () => {
  afterEach(cleanup)

  beforeEach(() => {
    vi.clearAllMocks()
    auth.permissions = ['tank_cards.view', 'tank_cards.edit', 'tank_cards.create']
  })

  it('renders linked cards with a masked number and status badge', async () => {
    api.listEmployeeTankCards.mockResolvedValue([
      makeCard({ id: 'card-1', cardNumber: '1234567812345678', status: 'Active' }),
    ])

    render(<EmployeeTankCardsSection employeeId="emp-1" />)

    expect(await screen.findByText('•••• 5678')).toBeInTheDocument()
    expect(screen.getByText('Actief')).toBeInTheDocument()
    expect(api.listEmployeeTankCards).toHaveBeenCalledWith('emp-1')
  })

  it('shows the empty state when no cards are linked', async () => {
    api.listEmployeeTankCards.mockResolvedValue([])

    render(<EmployeeTankCardsSection employeeId="emp-1" />)

    expect(await screen.findByText('Geen tankkaarten gekoppeld.')).toBeInTheDocument()
  })

  it('links an existing available card, preserving its other fields in the update payload', async () => {
    api.listEmployeeTankCards.mockResolvedValue([])
    const availableCard = makeCard({
      id: 'card-9',
      cardNumber: '9999888877776666',
      provider: 'Shell',
      vehicleId: 'veh-1',
      internalName: 'Bus 3 kaart',
      fuelType: 'Diesel',
      dailyLimit: 50,
      weeklyLimit: 200,
      monthlyLimit: 800,
      costCenter: 'CC-42',
      validFrom: '2026-01-01',
      validUntil: '2027-01-01',
      notes: 'Let op verbruik',
    })
    api.searchTankCards.mockResolvedValue({ items: [availableCard], totalCount: 1, page: 1, pageSize: 200 })
    api.updateTankCard.mockResolvedValue({})

    render(<EmployeeTankCardsSection employeeId="emp-1" />)
    await screen.findByText('Geen tankkaarten gekoppeld.')

    await userEvent.click(screen.getByRole('button', { name: 'Bestaande kaart koppelen' }))
    expect(api.searchTankCards).toHaveBeenCalledWith({ available: true, page: 1, pageSize: 200 })

    const select = await screen.findByLabelText(/^Tankkaart/)
    await userEvent.selectOptions(select, 'card-9')
    await userEvent.click(screen.getByRole('button', { name: 'Koppelen' }))

    await waitFor(() => expect(api.updateTankCard).toHaveBeenCalledTimes(1))
    const [cardId, payload] = api.updateTankCard.mock.calls[0] as [string, Record<string, unknown>]
    expect(cardId).toBe('card-9')
    expect(payload.employeeId).toBe('emp-1')
    // Every other field must be carried over unchanged so linking never wipes existing data.
    expect(payload.cardNumber).toBe('9999888877776666')
    expect(payload.provider).toBe('Shell')
    expect(payload.vehicleId).toBe('veh-1')
    expect(payload.internalName).toBe('Bus 3 kaart')
    expect(payload.fuelType).toBe('Diesel')
    expect(payload.dailyLimit).toBe(50)
    expect(payload.weeklyLimit).toBe(200)
    expect(payload.monthlyLimit).toBe(800)
    expect(payload.costCenter).toBe('CC-42')
    expect(payload.validFrom).toBe('2026-01-01')
    expect(payload.validUntil).toBe('2027-01-01')
    expect(payload.notes).toBe('Let op verbruik')
  })

  it('unlinks a card by sending employeeId null while preserving other fields', async () => {
    const linkedCard = makeCard({
      id: 'card-1',
      cardNumber: '1234567812345678',
      provider: 'DKV',
      employeeId: 'emp-1',
      employeeName: 'Jan Janssen',
      internalName: 'Vaste kaart Jan',
      fuelType: 'Diesel',
      costCenter: 'CC-1',
    })
    api.listEmployeeTankCards.mockResolvedValue([linkedCard])
    api.updateTankCard.mockResolvedValue({})

    render(<EmployeeTankCardsSection employeeId="emp-1" />)
    await screen.findByText('•••• 5678')

    await userEvent.click(screen.getByRole('button', { name: 'Ontkoppelen' }))
    const dialog = await screen.findByRole('dialog', { name: 'Tankkaart ontkoppelen' })
    await userEvent.click(within(dialog).getByRole('button', { name: 'Ontkoppelen' }))

    await waitFor(() => expect(api.updateTankCard).toHaveBeenCalledTimes(1))
    const [cardId, payload] = api.updateTankCard.mock.calls[0] as [string, Record<string, unknown>]
    expect(cardId).toBe('card-1')
    expect(payload.employeeId).toBeNull()
    expect(payload.cardNumber).toBe('1234567812345678')
    expect(payload.provider).toBe('DKV')
    expect(payload.internalName).toBe('Vaste kaart Jan')
    expect(payload.fuelType).toBe('Diesel')
    expect(payload.costCenter).toBe('CC-1')
  })

  it('renders nothing without tank_cards.view', () => {
    auth.permissions = []
    const { container } = render(<EmployeeTankCardsSection employeeId="emp-1" />)
    expect(container).toBeEmptyDOMElement()
    expect(api.listEmployeeTankCards).not.toHaveBeenCalled()
  })
})
