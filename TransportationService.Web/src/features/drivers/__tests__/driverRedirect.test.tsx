import { describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, Route, Routes, useSearchParams } from 'react-router-dom'
import { DriverDetailPage } from '../pages/DriverDetailPage'
import type { DriverDetail } from '../types'

/** Reads the destination route's own search params (MemoryRouter keeps its history separate
 * from `window.location`, so that can't be used to observe the redirect target). */
function DestinationProbe() {
  const [searchParams] = useSearchParams()
  return <div>Personeelsdossier {searchParams.get('section')}</div>
}

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

/** The legacy /drivers/:id route resolves the driver and redirects to its personnel section. */
describe('DriverDetailPage redirect resolver', () => {
  it('redirects /drivers/:id to the employee profile\'s chauffeursgegevens section', async () => {
    render(
      <MemoryRouter initialEntries={['/drivers/drv-1']}>
        <Routes>
          <Route path="/drivers/:id" element={<DriverDetailPage />} />
          <Route path="/employees/:id" element={<DestinationProbe />} />
        </Routes>
      </MemoryRouter>,
    )

    await waitFor(() => expect(screen.getByText('Personeelsdossier chauffeursgegevens')).toBeInTheDocument())
  })
})
