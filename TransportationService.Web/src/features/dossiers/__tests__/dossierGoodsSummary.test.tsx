import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { DossierGoodsSummary } from '../components/DossierGoodsSummary'
import type { CargoItem, TransportOrderDetail } from '../../transport-orders/types'
import { orderDetail } from './fixtures'

/**
 * Master sprint 2026-09-21, D4/D2 — read-only goods summary: coverage badge per line (only when
 * the backend sends one), weights exactly as entered, the honest capacity block, and a fitting
 * empty state for on-site lifting jobs (which lift a load and carry no goods).
 */

const lookup = vi.hoisted(() => ({
  options: [] as { id: string; code: string; name: string }[],
  isLoading: false,
}))
vi.mock('../../master-data/hooks/useLookupOptions', () => ({
  useLookupOptions: () => ({ options: lookup.options, isLoading: lookup.isLoading, error: null, refresh: () => Promise.resolve() }),
}))

function cargo(overrides: Partial<CargoItem>): CargoItem {
  return { ...orderDetail().cargoItems[0], ...overrides }
}

function renderSummary(order: TransportOrderDetail, canEdit = true) {
  return render(<DossierGoodsSummary order={order} loading={false} canEdit={canEdit} onEdit={vi.fn()} />)
}

const lineTexts = (container: HTMLElement) =>
  Array.from(container.querySelectorAll('.dossier-goods-lines li')).map((li) => li.textContent ?? '')

describe('DossierGoodsSummary — unit labels come from the unit catalogue', () => {
  beforeEach(() => {
    lookup.options = [
      { id: 'u-ep', code: 'EP', name: 'Europallet' },
      { id: 'u-colli', code: 'COLLI', name: 'Colli' },
    ]
    lookup.isLoading = false
  })

  it('shows the catalogue name of a managed unit code, never the raw code', () => {
    const { container } = renderSummary(
      orderDetail({
        cargoItems: [
          cargo({ id: 'a', expectedQuantity: 2, quantityUnitCode: 'EP', quantityUnit: null, description: null }),
          cargo({ id: 'b', expectedQuantity: 5, quantityUnitCode: 'COLLI', quantityUnit: null, description: null }),
        ],
      }),
    )
    const [first, second] = lineTexts(container)
    expect(first).toContain('2 × Europallet')
    expect(first).not.toMatch(/\bEP\b/)
    expect(second).toContain('5 × Colli')
    expect(second).not.toMatch(/\bCOLLI\b/)
  })

  it('falls back to the code itself for a code the catalogue does not know', () => {
    const { container } = renderSummary(
      orderDetail({ cargoItems: [cargo({ id: 'a', expectedQuantity: 3, quantityUnitCode: 'XYZ', quantityUnit: null, description: null })] }),
    )
    expect(lineTexts(container)[0]).toContain('3 × XYZ')
    expect(container.textContent).not.toContain('stuks')
  })

  it('shows the legacy free-text unit when the line has no code', () => {
    const { container } = renderSummary(
      orderDetail({ cargoItems: [cargo({ id: 'a', expectedQuantity: 4, quantityUnitCode: null, quantityUnit: 'rollen', description: null })] }),
    )
    expect(lineTexts(container)[0]).toContain('4 × rollen')
  })

  it('shows the code (not the fallback word) while the catalogue is still loading', () => {
    lookup.options = []
    lookup.isLoading = true
    const { container } = renderSummary(
      orderDetail({ cargoItems: [cargo({ id: 'a', expectedQuantity: 2, quantityUnitCode: 'EP', quantityUnit: null, description: null })] }),
    )
    expect(lineTexts(container)[0]).toContain('2 × EP')
    expect(container.textContent).not.toContain('stuks')
  })

  it('keeps the fallback word only when neither a code nor a legacy unit exists', () => {
    const { container } = renderSummary(
      orderDetail({ cargoItems: [cargo({ id: 'a', expectedQuantity: 1, quantityUnitCode: null, quantityUnit: null, description: null })] }),
    )
    expect(lineTexts(container)[0]).toContain('1 × stuks')
  })
})

describe('DossierGoodsSummary', () => {
  it('shows a coverage badge for each of the three backend values', () => {
    renderSummary(
      orderDetail({
        cargoItems: [
          cargo({ id: 'a', commercialCoverage: 'SeparatelyPriced' }),
          cargo({ id: 'b', commercialCoverage: 'Included' }),
          cargo({ id: 'c', commercialCoverage: 'ToReview' }),
        ],
      }),
    )
    expect(screen.getByText('Afzonderlijk geprijsd')).toBeInTheDocument()
    expect(screen.getByText('Inbegrepen in rit- of activiteitprijs')).toBeInTheDocument()
    expect(screen.getByText('Nog te controleren')).toBeInTheDocument()
  })

  it('renders no badge while the backend sends no coverage', () => {
    const { container } = renderSummary(orderDetail({ cargoItems: [cargo({ id: 'a' })] }))
    expect(container.querySelector('.ui-badge')).toBeNull()
  })

  it('shows a total-only line with its total and never a per-unit weight', () => {
    const { container } = renderSummary(
      orderDetail({ cargoItems: [cargo({ id: 'a', expectedQuantity: 3, totalWeightKg: 6000, weightPerUnitKg: null })] }),
    )
    const item = container.querySelector('.dossier-goods-lines li') as HTMLElement
    expect(item.textContent).toContain('totaal 6.000 kg')
    expect(item.textContent).not.toContain('per eenheid')
    expect(container.textContent).not.toContain('2.000')
    expect(screen.getByText('Capaciteit nog te controleren')).toBeInTheDocument()
    expect(container.textContent).not.toMatch(/veilig/i)
  })

  it('shows "Geen goederen — hijswerk op locatie" for an on-site lifting order instead of an empty/error state', () => {
    const lifting = { ...orderDetail({ cargoItems: [], goodsDescription: null }), craneJobKind: 'OnSiteLifting' }
    renderSummary(lifting as TransportOrderDetail)
    expect(screen.getByText('Geen goederen — hijswerk op locatie')).toBeInTheDocument()
    expect(screen.queryByText('Nog geen goederenlijnen.')).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: '+ Goederen' })).not.toBeInTheDocument()
  })

  it('keeps the normal empty state when craneJobKind is absent or another kind', () => {
    renderSummary(orderDetail({ cargoItems: [], goodsDescription: null }))
    expect(screen.getByText('Nog geen goederenlijnen.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: '+ Goederen' })).toBeInTheDocument()
    expect(screen.queryByText('Geen goederen — hijswerk op locatie')).not.toBeInTheDocument()
  })
})
