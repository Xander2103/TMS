import { describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { KpiCard } from '../components/KpiCard'
import { pct, presetRange } from '../types'

describe('kpi formatting', () => {
  it('formats percentages zero-safely', () => {
    expect(pct(14.8)).toBe('14,8%')
    expect(pct(0)).toBe('0,0%')
    expect(pct(null)).toBe('—')
    expect(pct(undefined)).toBe('—')
  })

  it('builds sane period presets', () => {
    const today = presetRange('today')
    expect(today.from).toBe(today.to)
    const month = presetRange('month')
    expect(month.from <= month.to).toBe(true)
    expect(month.from.endsWith('-01')).toBe(true)
  })
})

describe('KpiCard', () => {
  it('renders a clickable tile with label, value and hint when a link is given', () => {
    render(
      <MemoryRouter>
        <KpiCard label="Omzet periode" value="€ 1.250,00" hint="14 ritten" to="/kpi/trips" />
      </MemoryRouter>,
    )

    const tile = screen.getByRole('button', { name: /Omzet periode/ })
    expect(tile).toHaveTextContent('€ 1.250,00')
    expect(tile).toHaveTextContent('14 ritten')
  })

  it('renders a plain tile without a link (no fake affordance)', () => {
    render(
      <MemoryRouter>
        <KpiCard label="CO₂-raming" value="214 kg" />
      </MemoryRouter>,
    )

    expect(screen.queryByRole('button')).toBeNull()
    expect(screen.getByText('CO₂-raming')).toBeInTheDocument()
  })
})
