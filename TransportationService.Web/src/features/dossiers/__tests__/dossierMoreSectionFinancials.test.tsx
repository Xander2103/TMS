import { describe, expect, it, vi } from 'vitest'
import { render, screen, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { DossierMoreSection } from '../components/DossierMoreSection'
import type { DossierDetail } from '../types'

// A missing price is not € 0,00: the financial block in "Meer" follows the same provenance rule
// (isDossierPriced) as the price tab and the overview.

function dossier(financials: Partial<DossierDetail['financials']>): DossierDetail {
  return {
    orders: [],
    relations: [],
    incidents: [],
    financials: { agreedOrderTotal: 0, invoicedTotal: 0, estimatedIncidentCost: 0, actualIncidentCost: 0, ...financials },
  } as unknown as DossierDetail
}

function renderSection(value: DossierDetail) {
  render(
    <MemoryRouter>
      <DossierMoreSection dossier={value} canManage={false} busy={false} onRemoveRelation={vi.fn()} defaultOpen />
    </MemoryRouter>,
  )
  const label = screen.getByText('Afgesproken omzet')
  return within(label.closest('.db-kpi') as HTMLElement)
}

describe('DossierMoreSection — agreed revenue', () => {
  it('shows "Nog geen prijs" instead of € 0,00 while nothing is priced', () => {
    const kpi = renderSection(dossier({ pricedActivityCount: 0, billableActivityCount: 2 }))
    expect(kpi.getByText('Nog geen prijs')).toBeInTheDocument()
    expect(kpi.queryByText(/€\s?0,00/)).not.toBeInTheDocument()
  })

  it('shows the server total once something is priced, also a deliberate € 0', () => {
    const kpi = renderSection(dossier({ pricedActivityCount: 1, billableActivityCount: 2, agreedOrderTotal: 125 }))
    expect(kpi.getByText(/125,00/)).toBeInTheDocument()
  })
})
