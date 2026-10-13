import { describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { FleetKpiPanel } from '../FleetKpiPanel'
import type { FleetKpi } from '../fleetKpiApi'

const kpi = vi.hoisted(() => ({
  value: {
    assetId: 'v-1',
    from: '2026-01-01',
    to: '2026-07-21',
    values: [
      { key: 'trips', label: 'Ritten', value: 42, unit: 'ritten', quality: 'Actual', detail: null },
      { key: 'km', label: 'Kilometers', value: null, unit: 'km', quality: 'Unavailable', detail: 'Onvoldoende odometergegevens' },
    ],
  } as FleetKpi,
}))

vi.mock('../fleetKpiApi', async () => {
  const actual = await vi.importActual<typeof import('../fleetKpiApi')>('../fleetKpiApi')
  return { ...actual, getFleetKpi: () => Promise.resolve(kpi.value) }
})

describe('FleetKpiPanel', () => {
  it('formats actual metrics and renders unavailable ones as an em dash with explanation', async () => {
    render(
      <MemoryRouter>
        <FleetKpiPanel ownerType="vehicle" ownerId="v-1" />
      </MemoryRouter>,
    )

    await waitFor(() => expect(screen.getByText('Ritten')).toBeInTheDocument())
    expect(screen.getByText('42 ritten')).toBeInTheDocument()
    expect(screen.getByText('—')).toBeInTheDocument()
    expect(screen.getByText('Onvoldoende odometergegevens')).toBeInTheDocument()
  })
})
