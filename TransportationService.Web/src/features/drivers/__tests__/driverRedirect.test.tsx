import { describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { DriverDetailPage } from '../pages/DriverDetailPage'
import type { DriverDetail } from '../types'

const driver = vi.hoisted(() => ({
  value: {
    id: 'drv-1',
    employeeId: 'emp-42',
    driverNumber: 'CH-0001',
  } as Partial<DriverDetail>,
}))

vi.mock('../api/driversApi', () => ({
  getDriver: () => Promise.resolve(driver.value),
}))

/** The legacy /drivers/:id route resolves the driver and redirects to its personnel tab. */
describe('DriverDetailPage redirect resolver', () => {
  it('redirects /drivers/:id to the employee chauffeursprofiel tab', async () => {
    render(
      <MemoryRouter initialEntries={['/drivers/drv-1']}>
        <Routes>
          <Route path="/drivers/:id" element={<DriverDetailPage />} />
          <Route
            path="/employees/:id"
            element={<div>Personeelsdossier {new URLSearchParams(window.location.search).get('tab')}</div>}
          />
        </Routes>
      </MemoryRouter>,
    )

    await waitFor(() => expect(screen.getByText(/Personeelsdossier/)).toBeInTheDocument())
  })
})
