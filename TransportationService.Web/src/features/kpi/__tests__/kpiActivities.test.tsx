import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { KpiActivitiesSection } from '../components/KpiActivitiesSection'
import type { ActivityKpiReport } from '../types'

const report: ActivityKpiReport = {
  from: '2026-07-20',
  to: '2026-07-24',
  rows: [
    {
      activityTypeId: 't-kraan',
      code: 'KRAANWERK',
      name: 'Kraanwerk',
      kpiCategory: 'Kraan',
      activityCount: 3,
      linkedOrderCount: 2,
      revenue: 1000,
      redeliveryCount: 1,
    },
    {
      activityTypeId: 't-plateau',
      code: 'PLATEAU',
      name: 'Plateauwerk',
      kpiCategory: null,
      activityCount: 1,
      linkedOrderCount: 0,
      revenue: 0,
      redeliveryCount: 0,
    },
  ],
  totals: { activityCount: 4, linkedOrderCount: 2, revenue: 1000, redeliveryCount: 1 },
  palletDays: 12,
  perCategory: [
    { kpiCategory: 'Kraan', activityCount: 3, linkedOrderCount: 2, revenue: 1000, redeliveryCount: 1 },
    { kpiCategory: null, activityCount: 1, linkedOrderCount: 0, revenue: 0, redeliveryCount: 0 },
  ],
}

vi.mock('../api/kpiApi', () => ({
  getActivityKpis: vi.fn(() => Promise.resolve(report)),
}))

describe('KpiActivitiesSection', () => {
  it('renders one row per activity type, a totals row and the pallet-day stat', async () => {
    render(<KpiActivitiesSection from="2026-07-20" to="2026-07-24" />)

    expect(await screen.findByText('Kraanwerk')).toBeInTheDocument()
    expect(screen.getByText('Plateauwerk')).toBeInTheDocument()
    expect(screen.getByText('Kraan')).toBeInTheDocument()

    const craneRow = screen.getByText('Kraanwerk').closest('tr')!
    expect(craneRow).toHaveTextContent('3')
    expect(craneRow).toHaveTextContent('2')
    expect(craneRow).toHaveTextContent('1.000,00')

    const totalsRow = screen.getByText('Totaal').closest('tr')!
    expect(totalsRow).toHaveTextContent('4')
    expect(totalsRow).toHaveTextContent('1.000,00')

    expect(screen.getByText(/Pallet-dagen \(opslag\)/)).toBeInTheDocument()
    expect(screen.getByText('12')).toBeInTheDocument()
  })
})
