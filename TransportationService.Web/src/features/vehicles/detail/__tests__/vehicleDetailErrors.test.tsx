import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { RouterProvider, createMemoryRouter } from 'react-router-dom'
import { ApiError } from '../../../../api/apiClient'
import { VehicleDetailPage } from '../../pages/VehicleDetailPage'

// FL-M10: an unknown vehicle reads "Voertuig niet gevonden" with a way back — not a retry loop.

vi.mock('../../../auth/authContextValue', () => ({ useAuth: () => ({ hasPermission: () => true }) }))
vi.mock('../../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }),
}))
const getVehicle = vi.hoisted(() => vi.fn())
vi.mock('../../api/vehiclesApi', () => ({
  getVehicle,
  updateVehicle: vi.fn(),
  deleteVehicle: vi.fn(),
  setVehicleActive: vi.fn(),
  setVehicleDriver: vi.fn(),
}))

function renderPage() {
  const router = createMemoryRouter([{ path: '/vehicles/:id', element: <VehicleDetailPage /> }], { initialEntries: ['/vehicles/nope'] })
  render(<RouterProvider router={router} />)
}

describe('VehicleDetailPage load errors', () => {
  it('404 → "Voertuig niet gevonden" with a back link and no retry', async () => {
    getVehicle.mockRejectedValue(new ApiError('Not Found', 404))
    renderPage()
    expect(await screen.findByText('Voertuig niet gevonden.')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Terug naar voertuigen' })).toHaveAttribute('href', '/vehicles')
    expect(screen.queryByRole('button', { name: 'Opnieuw proberen' })).not.toBeInTheDocument()
  })

  it('403 → no access text', async () => {
    getVehicle.mockRejectedValue(new ApiError('Forbidden', 403))
    renderPage()
    expect(await screen.findByText('Geen toegang tot dit voertuig.')).toBeInTheDocument()
  })

  it('other failures keep the retry', async () => {
    getVehicle.mockRejectedValue(new ApiError('boom', 500))
    renderPage()
    expect(await screen.findByText('Voertuig kon niet worden geladen.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Opnieuw proberen' })).toBeInTheDocument()
  })
})
