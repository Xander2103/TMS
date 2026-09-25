import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { AttentionPanel } from '../components/AttentionPanel'
import type { ReadinessIssue } from '../types'
import { dossierActivity } from './fixtures'

const issues: ReadinessIssue[] = [
  { code: 'route.unloading_missing', severity: 'Warning', message: 'Loslocatie is nog onbekend', section: 'route', field: null, stage: 'Planning' },
  { code: 'pricing.incomplete', severity: 'Warning', message: 'Wachttijd 1,5 u heeft geen prijs', section: 'prijs', field: 'price', stage: 'Commercial' },
  { code: 'activity.none', severity: 'Info', message: 'Nog geen activiteit', section: 'activiteiten', field: null, stage: 'Planning' },
]

describe('AttentionPanel', () => {
  it('renders nothing when there are no issues', () => {
    const { container } = render(<AttentionPanel issues={[]} onNavigate={vi.fn()} />)
    expect(container).toBeEmptyDOMElement()
  })

  it('shows icon + message per issue and navigates to the named section', async () => {
    const user = userEvent.setup()
    const onNavigate = vi.fn()
    render(<AttentionPanel issues={issues} onNavigate={onNavigate} />)

    expect(screen.getByText('Loslocatie is nog onbekend')).toBeInTheDocument()
    // Never colour-only: each severity carries a labeled icon.
    expect(screen.getAllByRole('img', { name: 'Waarschuwing' })).toHaveLength(2)
    expect(screen.getByRole('img', { name: 'Info' })).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Ga naar route' }))
    expect(onNavigate).toHaveBeenCalledWith('route', null, issues[0])
    await user.click(screen.getByRole('button', { name: 'Ga naar prijs' }))
    expect(onNavigate).toHaveBeenCalledWith('prijs', 'price', issues[1])
  })

  it('makes the warning itself clickable: same jump, with the issue (activity/order) along', async () => {
    const user = userEvent.setup()
    const onNavigate = vi.fn()
    const priceIssue: ReadinessIssue = {
      code: 'pricing.missing', severity: 'Warning', message: '0006: verkoopprijs ontbreekt.', section: 'prijs', field: 'price', stage: 'Commercial', transportOrderId: 'o-6',
    }
    render(<AttentionPanel issues={[priceIssue]} onNavigate={onNavigate} />)
    await user.click(screen.getByRole('button', { name: '0006: verkoopprijs ontbreekt.' }))
    expect(onNavigate).toHaveBeenCalledWith('prijs', 'price', priceIssue)
  })

  it('names the activity/order of an issue that only carries the id — and never twice', () => {
    const activities = [
      dossierActivity({ id: 'a-6', linkedTransportOrderId: 'o-6', linkedOrderNumber: '0006', linkedOrderStatus: 'Confirmed' }),
      dossierActivity({ id: 'a-7', sequence: 2, hasStops: false, activityTypeName: 'Opslag' }),
      dossierActivity({ id: 'a-8', sequence: 3, hasStops: false, activityTypeName: 'Kraanwerk', label: 'Werf Gent' }),
    ]
    const base = { severity: 'Warning' as const, stage: 'Planning', field: null }
    render(
      <AttentionPanel
        activities={activities}
        onNavigate={vi.fn()}
        issues={[
          { ...base, code: 'route.date_missing', section: 'route', message: 'planningsdatum ontbreekt.', transportOrderId: 'o-6' },
          { ...base, code: 'pricing.missing', section: 'prijs', message: '0006: verkoopprijs ontbreekt.', activityId: 'a-6' },
          { ...base, code: 'pricing.missing', section: 'prijs', message: 'verkoopprijs ontbreekt.', activityId: 'a-7' },
          // The backend names a standalone activity by its type: no second prefix with the label.
          { ...base, code: 'pricing.missing', section: 'prijs', message: 'Kraanwerk: verkoopprijs ontbreekt.', activityId: 'a-8' },
          { ...base, code: 'activity.none', section: 'activiteiten', message: 'Nog geen activiteit toegevoegd.' },
        ]}
      />,
    )
    expect(screen.getByRole('button', { name: '0006: planningsdatum ontbreekt.' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: '0006: verkoopprijs ontbreekt.' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Opslag: verkoopprijs ontbreekt.' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Kraanwerk: verkoopprijs ontbreekt.' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Nog geen activiteit toegevoegd.' })).toBeInTheDocument()
  })
})
