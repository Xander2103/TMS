import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ApiError } from '../../../api/apiClient'
import { ReplaceVehicleDialog } from '../components/ReplaceVehicleDialog'

// FL-M13: replacing the vehicle of a planned trip — reason required, full conflict check, override
// only for holders of the override right.

const auth = vi.hoisted(() => ({ granted: new Set<string>() }))
vi.mock('../../auth/authContextValue', () => ({ useAuth: () => ({ hasPermission: (code: string) => auth.granted.has(code) }) }))
const api = vi.hoisted(() => ({ replaceTripVehicle: vi.fn() }))
vi.mock('../api/planningApi', () => api)

const trip = { id: 'trip-1', tripNumber: 'RIT-0043', vehicleId: 'v-golden', trailerId: null }
const vehicleOptions = [
  { value: 'v-golden', label: 'VRT-0012 (1-TFG-001)', subtitle: '⛔ Onderhoud gepland' },
  { value: 'v-4', label: 'VRT-0004 (1-ABC-103)', subtitle: 'Volvo FH' },
]
const conflict = {
  code: 'VehicleNotOperational', blocking: true, description: 'Voertuig VRT-0004 is niet inzetbaar.', severity: 'Blocking',
  category: 'Resource', overrideAllowed: true, requiredPermission: 'planning.override_restriction', suggestedAction: null,
}

async function pickAndSubmit(reason = 'Golden in onderhoud') {
  await userEvent.click(screen.getByRole('combobox', { name: /Nieuw voertuig/ }))
  await userEvent.click(await screen.findByRole('option', { name: /VRT-0004/ }))
  if (reason) await userEvent.type(screen.getByLabelText(/Reden/), reason)
  await userEvent.click(screen.getByRole('button', { name: 'Vervangen' }))
}

beforeEach(() => {
  vi.clearAllMocks()
  auth.granted = new Set()
})

describe('ReplaceVehicleDialog', () => {
  it('does not offer the current vehicle and requires a reason', async () => {
    render(<ReplaceVehicleDialog trip={trip} vehicleOptions={vehicleOptions} trailerOptions={[]} onReplaced={vi.fn()} onClose={vi.fn()} />)
    await userEvent.click(screen.getByRole('combobox', { name: /Nieuw voertuig/ }))
    expect(screen.queryByRole('option', { name: /VRT-0012/ })).not.toBeInTheDocument()
    await userEvent.click(await screen.findByRole('option', { name: /VRT-0004/ }))
    await userEvent.click(screen.getByRole('button', { name: 'Vervangen' }))
    expect(screen.getByRole('alert')).toHaveTextContent('Geef een reden op voor de vervanging.')
    expect(api.replaceTripVehicle).not.toHaveBeenCalled()
  })

  it('replaces with the reason and reports the updated trip', async () => {
    const updated = { ...trip, vehicleId: 'v-4' }
    api.replaceTripVehicle.mockResolvedValue(updated)
    const onReplaced = vi.fn()
    render(<ReplaceVehicleDialog trip={trip} vehicleOptions={vehicleOptions} trailerOptions={[]} onReplaced={onReplaced} onClose={vi.fn()} />)
    await pickAndSubmit()
    await waitFor(() => expect(api.replaceTripVehicle).toHaveBeenCalledWith('trip-1', { vehicleId: 'v-4', trailerId: null, reason: 'Golden in onderhoud' }))
    expect(onReplaced).toHaveBeenCalledWith(updated)
  })

  it('a blocked replacement shows the conflicts; a planner without the right cannot override', async () => {
    api.replaceTripVehicle.mockRejectedValue(new ApiError('Het voertuig kan niet worden vervangen door conflicten.', 409, { message: 'Het voertuig kan niet worden vervangen door conflicten.', conflicts: [conflict] }))
    render(<ReplaceVehicleDialog trip={trip} vehicleOptions={vehicleOptions} trailerOptions={[]} onReplaced={vi.fn()} onClose={vi.fn()} />)
    await pickAndSubmit()
    expect(await screen.findByText('Voertuig VRT-0004 is niet inzetbaar.')).toBeInTheDocument()
    expect(screen.queryByLabelText(/reden/i, { selector: '#cd-override-reason' })).not.toBeInTheDocument()
  })

  it('a holder of the override right retries with override and both reasons', async () => {
    auth.granted = new Set(['planning.override_restriction'])
    api.replaceTripVehicle
      .mockRejectedValueOnce(new ApiError('Geblokkeerd', 409, { message: 'Geblokkeerd', conflicts: [conflict] }))
      .mockResolvedValueOnce({ ...trip, vehicleId: 'v-4' })
    const onReplaced = vi.fn()
    render(<ReplaceVehicleDialog trip={trip} vehicleOptions={vehicleOptions} trailerOptions={[]} onReplaced={onReplaced} onClose={vi.fn()} />)
    await pickAndSubmit()
    const reasonBox = await screen.findByRole('textbox')
    await userEvent.type(reasonBox, 'Klant gaat akkoord')
    const overrideButton = screen.getAllByRole('button').find((b) => /overschrijv|toch/i.test(b.textContent ?? ''))!
    await userEvent.click(overrideButton)
    await waitFor(() =>
      expect(api.replaceTripVehicle).toHaveBeenLastCalledWith('trip-1', expect.objectContaining({ override: true, reason: 'Golden in onderhoud — Klant gaat akkoord' })),
    )
    expect(onReplaced).toHaveBeenCalled()
  })
})
