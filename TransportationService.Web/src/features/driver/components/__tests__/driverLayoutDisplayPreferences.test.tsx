import { afterAll, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { DriverLayout } from '../DriverLayout'
import { resetDisplayPreferences } from '../../../../components/layout/DisplayPreferencesProvider'
import {
  formatDateTime, resetDateFormatPreference, resetTimeZonePreference,
} from '../../../../utils/dates'

/**
 * C-03 / I-1: the driver shell is mounted outside AppLayout and CustomerPortalLayout, so it used
 * to be the one authenticated shell that never fetched the regional preferences — every driver
 * screen silently rendered on the seeded default zone and format. Forcing the process zone to
 * America/New_York and the tenant to Asia/Tokyo makes all three readings distinguishable:
 * browser 02:00, default (Europe/Amsterdam) 08:00, tenant 15:00.
 */
declare const process: { env: Record<string, string | undefined> }

const ORIGINAL_TZ = process.env.TZ

beforeAll(() => {
  process.env.TZ = 'America/New_York'
})

afterAll(() => {
  if (ORIGINAL_TZ === undefined) delete process.env.TZ
  else process.env.TZ = ORIGINAL_TZ
})

const api = vi.hoisted(() => ({ getJson: vi.fn() }))
vi.mock('../../../../api/apiClient', () => ({ apiClient: { getJson: api.getJson } }))

vi.mock('../../../../hooks/useActionQueueSync', () => ({
  useActionQueueSync: () => ({ actions: [], scans: [], unsyncedCount: 0 }),
}))
vi.mock('../../../../hooks/useOnlineStatus', () => ({ useOnlineStatus: () => true }))

function DriverProbe() {
  return <span data-testid="stamp">{formatDateTime('2026-07-15T06:00:00Z')}</span>
}

beforeEach(() => {
  api.getJson.mockReset()
  resetDisplayPreferences()
  resetTimeZonePreference()
  resetDateFormatPreference()
})

describe('DriverLayout regional bootstrap', () => {
  it('loads the tenant preferences and renders driver screens in the tenant zone', async () => {
    api.getJson.mockResolvedValue({
      dateFormat: 'yyyy-MM-dd', decimalSeparator: ',', timezone: 'Asia/Tokyo',
    })

    render(
      <MemoryRouter initialEntries={['/driver']}>
        <Routes>
          <Route element={<DriverLayout />}>
            <Route path="/driver" element={<DriverProbe />} />
          </Route>
        </Routes>
      </MemoryRouter>,
    )

    expect(await screen.findByTestId('stamp')).toHaveTextContent('2026-07-15 15:00')
    expect(api.getJson).toHaveBeenCalledWith('/api/company-settings/display')
  })

  it('still renders the driver screen on the defaults when the fetch fails', async () => {
    api.getJson.mockRejectedValue(new Error('offline'))

    render(
      <MemoryRouter initialEntries={['/driver']}>
        <Routes>
          <Route element={<DriverLayout />}>
            <Route path="/driver" element={<DriverProbe />} />
          </Route>
        </Routes>
      </MemoryRouter>,
    )

    // Offline is the driver app's normal state — the shell must never be held hostage by it.
    expect(await screen.findByTestId('stamp')).toHaveTextContent('15/07/2026 08:00')
  })
})
