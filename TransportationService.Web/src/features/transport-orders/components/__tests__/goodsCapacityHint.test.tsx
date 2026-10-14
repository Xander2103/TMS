import { describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import { CargoCoverageBadge } from '../CargoCoverageBadge'
import { GoodsCapacityHint } from '../GoodsCapacityHint'
import { isOnSiteLiftingOrder } from '../onSiteLifting'
import type { CargoCommercialCoverage } from '../../types'

/**
 * Master sprint 2026-09-21, D4 / B55 — the capacity hint is honest and quiet: without a vehicle it
 * only says the check becomes available after assignment (no warning tone); with a vehicle it
 * flags a missing payload, warns when a KNOWN capacity is exceeded, and never calls a load safe.
 */

const FORBIDDEN = /veilig|\bok\b|in orde/i

describe('GoodsCapacityHint', () => {
  it('B55: without a vehicle it stays quiet — a muted neutral line, no warning or attention', () => {
    const { container } = render(<GoodsCapacityHint totalWeightKg={6000} heaviestUnitKg={2000} />)
    expect(screen.getByText('6.000 kg')).toBeInTheDocument()
    expect(screen.getByText('2.000 kg')).toBeInTheDocument()
    const line = screen.getByText('Capaciteitscontrole beschikbaar na voertuigtoewijzing.')
    expect(line).toHaveClass('goods-capacity-note')
    expect(container.textContent).not.toMatch(/nog te controleren|Aandacht|transpallet|laadklep/i)
    expect(container.querySelector('.goods-capacity-attention, .goods-capacity-warning')).toBeNull()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    expect(container.textContent).not.toMatch(FORBIDDEN)
  })

  it('B55: without a vehicle it does not compare against capacities it was handed', () => {
    render(<GoodsCapacityHint totalWeightKg={13000} heaviestUnitKg={2000} payloadKg={12000} tailLiftCapacityKg={1500} />)
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    expect(screen.getByText('Capaciteitscontrole beschikbaar na voertuigtoewijzing.')).toBeInTheDocument()
  })

  it('B55: says nothing about the check while the assigned vehicle is still loading', () => {
    const { container } = render(<GoodsCapacityHint totalWeightKg={6000} heaviestUnitKg={2000} vehicle="loading" />)
    expect(container.textContent).not.toMatch(/Aandacht|voertuigtoewijzing/)
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('B55: a vehicle without a payload gets an attention line, not a warning', () => {
    const { container } = render(
      <GoodsCapacityHint totalWeightKg={6000} heaviestUnitKg={2000} vehicle="assigned" tailLiftCapacityKg={2500} />,
    )
    const attention = screen.getByText('Aandacht: Laadvermogen voertuig ontbreekt.')
    expect(attention).toHaveClass('goods-capacity-attention')
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    expect(container.textContent).not.toContain('voertuigtoewijzing')
    expect(container.textContent).not.toMatch(FORBIDDEN)
  })

  it('reports an unknown heaviest unit instead of deriving one from the total', () => {
    const { container } = render(
      <GoodsCapacityHint totalWeightKg={6000} heaviestUnitKg={null} linesWithoutUnitWeight={1} tailLiftCapacityKg={1500} vehicle="assigned" />,
    )
    expect(screen.getByText('onbekend')).toBeInTheDocument()
    expect(screen.getByText(/1 goederenlijn\(en\) zonder gewicht per eenheid/)).toBeInTheDocument()
    // Nothing to compare against → no warning, and certainly no "fits".
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    expect(container.textContent).not.toContain('2.000')
    expect(container.textContent).not.toMatch(FORBIDDEN)
  })

  it('warns when the heaviest unit exceeds a known tail lift capacity', () => {
    render(<GoodsCapacityHint totalWeightKg={6000} heaviestUnitKg={2000} payloadKg={12000} tailLiftCapacityKg={1500} vehicle="assigned" />)
    expect(screen.getByRole('alert')).toHaveTextContent(
      'Zwaarste eenheid (2.000 kg) overschrijdt de capaciteit van de laadklep (1.500 kg).',
    )
  })

  it('warns when the total weight exceeds a known payload', () => {
    render(<GoodsCapacityHint totalWeightKg={13000} heaviestUnitKg={1000} payloadKg={12000} tailLiftCapacityKg={1500} vehicle="assigned" />)
    expect(screen.getByRole('alert')).toHaveTextContent(
      'Totaal gewicht (13.000 kg) overschrijdt het laadvermogen van het voertuig (12.000 kg).',
    )
  })

  it('never says a load is safe — even when every known capacity is respected', () => {
    const { container } = render(
      <GoodsCapacityHint totalWeightKg={6000} heaviestUnitKg={1000} payloadKg={12000} tailLiftCapacityKg={1500} vehicle="assigned" />,
    )
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    expect(container.textContent).not.toContain('Aandacht')
    // Pallet truck / forklift capacities are not modelled: said plainly, in a muted note.
    expect(screen.getByText(/transpallet\/heftruck wordt niet geregistreerd/)).toHaveClass('goods-capacity-note')
    expect(container.textContent).not.toMatch(FORBIDDEN)
  })
})

describe('CargoCoverageBadge', () => {
  it.each<[CargoCommercialCoverage, string]>([
    ['SeparatelyPriced', 'Afzonderlijk geprijsd'],
    ['Included', 'Inbegrepen in rit- of activiteitprijs'],
    ['ToReview', 'Nog te controleren'],
  ])('renders %s as "%s"', (coverage, label) => {
    render(<CargoCoverageBadge coverage={coverage} />)
    expect(screen.getByText(label)).toBeInTheDocument()
  })

  it('renders nothing when the backend sends no (or an unknown) coverage', () => {
    const { container, rerender } = render(<CargoCoverageBadge coverage={undefined} />)
    expect(container).toBeEmptyDOMElement()
    rerender(<CargoCoverageBadge coverage={null} />)
    expect(container).toBeEmptyDOMElement()
    rerender(<CargoCoverageBadge coverage={'Future' as CargoCommercialCoverage} />)
    expect(container).toBeEmptyDOMElement()
  })
})

describe('isOnSiteLiftingOrder', () => {
  it('is true only for craneJobKind OnSiteLifting; an absent field means normal goods behaviour', () => {
    expect(isOnSiteLiftingOrder({ craneJobKind: 'OnSiteLifting' })).toBe(true)
    expect(isOnSiteLiftingOrder({ craneJobKind: 'TransportWithCrane' })).toBe(false)
    expect(isOnSiteLiftingOrder({})).toBe(false)
    expect(isOnSiteLiftingOrder(null)).toBe(false)
  })
})
