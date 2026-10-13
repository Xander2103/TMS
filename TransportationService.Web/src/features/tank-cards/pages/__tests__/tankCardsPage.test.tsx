import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { TankCardsPage } from '../TankCardsPage'

vi.mock('../../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }),
}))

vi.mock('../../../auth/authContextValue', () => ({
  useAuth: () => ({
    status: 'authenticated' as const,
    user: null,
    login: vi.fn(),
    logout: vi.fn(),
    hasPermission: () => true,
    hasAnyPermission: () => true,
  }),
}))

const api = vi.hoisted(() => ({
  searchTankCards: vi.fn(),
  createTankCard: vi.fn(),
  updateTankCard: vi.fn(),
  deleteTankCard: vi.fn(),
  setTankCardBlocked: vi.fn(),
  listEmployeeTankCards: vi.fn(),
}))

vi.mock('../../api/tankCardsApi', () => ({
  searchTankCards: (...args: unknown[]) => api.searchTankCards(...args),
  createTankCard: (...args: unknown[]) => api.createTankCard(...args),
  updateTankCard: (...args: unknown[]) => api.updateTankCard(...args),
  deleteTankCard: (...args: unknown[]) => api.deleteTankCard(...args),
  setTankCardBlocked: (...args: unknown[]) => api.setTankCardBlocked(...args),
  listEmployeeTankCards: (...args: unknown[]) => api.listEmployeeTankCards(...args),
}))

vi.mock('../../../vehicles/api/vehiclesApi', () => ({
  getVehicleOptions: () => Promise.resolve([]),
}))

// The employee combobox loads its own options; keep it empty and network-free for this page test.
vi.mock('../../../employees/api/employeesApi', () => ({
  searchEmployees: () => Promise.resolve({ items: [], totalCount: 0, page: 1, pageSize: 500 }),
}))

describe('TankCardsPage', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    api.searchTankCards.mockResolvedValue({ items: [], totalCount: 0, page: 1, pageSize: 25 })
    api.createTankCard.mockResolvedValue({})
  })

  afterEach(cleanup)

  it('submits the new tank-card fields (fuel type, limits, cost centre, internal name) on create', async () => {
    render(<TankCardsPage />)

    await userEvent.click(await screen.findByRole('button', { name: 'Nieuwe tankkaart' }))

    await userEvent.type(screen.getByLabelText(/^Kaartnummer/), '4444333322221111')
    await userEvent.type(screen.getByLabelText(/^Leverancier/), 'Total')
    await userEvent.type(screen.getByLabelText('Interne naam'), 'Bestelwagen 7')
    await userEvent.type(screen.getByLabelText('Brandstoftype'), 'Diesel')
    await userEvent.type(screen.getByLabelText('Limiet per dag (€)'), '75')
    await userEvent.type(screen.getByLabelText('Limiet per week (€)'), '300')
    await userEvent.type(screen.getByLabelText('Limiet per maand (€)'), '1200')
    await userEvent.type(screen.getByLabelText('Kostenplaats'), 'CC-7')

    await userEvent.click(screen.getByRole('button', { name: 'Opslaan' }))

    await waitFor(() => expect(api.createTankCard).toHaveBeenCalledTimes(1))
    const payload = api.createTankCard.mock.calls[0][0] as Record<string, unknown>
    expect(payload.cardNumber).toBe('4444333322221111')
    expect(payload.provider).toBe('Total')
    expect(payload.internalName).toBe('Bestelwagen 7')
    expect(payload.fuelType).toBe('Diesel')
    expect(payload.dailyLimit).toBe(75)
    expect(payload.weeklyLimit).toBe(300)
    expect(payload.monthlyLimit).toBe(1200)
    expect(payload.costCenter).toBe('CC-7')
    expect(payload.employeeId).toBeNull()
    expect('driverId' in payload).toBe(false)
  })

  it('omits unset optional limits as null instead of 0 or empty string', async () => {
    render(<TankCardsPage />)

    await userEvent.click(await screen.findByRole('button', { name: 'Nieuwe tankkaart' }))
    await userEvent.type(screen.getByLabelText(/^Kaartnummer/), '1111222233334444')
    await userEvent.type(screen.getByLabelText(/^Leverancier/), 'Shell')

    await userEvent.click(screen.getByRole('button', { name: 'Opslaan' }))

    await waitFor(() => expect(api.createTankCard).toHaveBeenCalledTimes(1))
    const payload = api.createTankCard.mock.calls[0][0] as Record<string, unknown>
    expect(payload.dailyLimit).toBeNull()
    expect(payload.weeklyLimit).toBeNull()
    expect(payload.monthlyLimit).toBeNull()
    expect(payload.internalName).toBeNull()
    expect(payload.fuelType).toBeNull()
    expect(payload.costCenter).toBeNull()
  })
})
