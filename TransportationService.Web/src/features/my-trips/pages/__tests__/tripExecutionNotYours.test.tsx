import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { TripExecutionPage } from '../TripExecutionPage'
import { ApiError } from '../../../../api/apiClient'

vi.mock('../../../auth/authContextValue', () => ({
  useAuth: () => ({ hasPermission: () => false }),
}))
vi.mock('../../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }),
}))
vi.mock('../../../scanning/components/ScanPanel', () => ({ ScanPanel: () => null }))
vi.mock('../../../exceptions/components/ReportExceptionDialog', () => ({ ReportExceptionDialog: () => null }))
vi.mock('../../../pod/components/PodDialog', () => ({ PodDialog: () => null }))

const api = vi.hoisted(() => ({
  getTripExecution: vi.fn(),
  getStopHistory: vi.fn(),
  transitionStop: vi.fn(),
  completeStop: vi.fn(),
}))
vi.mock('../../api/myTripsApi', () => api)

describe('TripExecutionPage — another driver\'s trip (SEC-H1/M5)', () => {
  it('reads "Niet gevonden" with a way back, never a retry, when the API answers 404', async () => {
    api.getTripExecution.mockRejectedValue(new ApiError('Not Found', 404))
    render(
      <MemoryRouter initialEntries={['/my-trips/trip-of-someone-else']}>
        <Routes>
          <Route path="/my-trips/:id" element={<TripExecutionPage />} />
        </Routes>
      </MemoryRouter>,
    )

    expect(await screen.findByText('Niet gevonden')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: /terug/i })).toHaveAttribute('href', '/my-trips')
    expect(screen.queryByRole('button', { name: /opnieuw proberen/i })).not.toBeInTheDocument()
  })
})
