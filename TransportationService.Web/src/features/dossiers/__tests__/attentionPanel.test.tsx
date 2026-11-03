import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
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

  it('open incidents appear as linked attention items, also without readiness issues (M12)', () => {
    render(
      <MemoryRouter>
        <AttentionPanel issues={[]} onNavigate={vi.fn()} openIncidents={[{ id: 'inc-9', title: 'Verkeerde lossplaats' }]} />
      </MemoryRouter>,
    )
    expect(screen.getByText('1 open incident')).toBeInTheDocument()
    expect(screen.getByText('1 open incident').closest('li')?.title).toMatch(/afsluiten kan pas na afhandeling/)
    expect(screen.getByRole('link', { name: 'Verkeerde lossplaats' })).toHaveAttribute('href', '/incidents/inc-9')
  })

  it('UX-010: an open afwijking is named, serious ones as blocking, and links only for who may open it', () => {
    const exceptions = [
      { id: 'e-1', typeLabel: 'Vertraging', severityLabel: 'hoog', holds: true, tripNumber: 'RIT-0240' },
      { id: 'e-2', typeLabel: 'Beschadigd collo', severityLabel: 'laag', holds: false, tripNumber: null },
    ]
    const { unmount } = render(
      <MemoryRouter>
        <AttentionPanel issues={[]} onNavigate={vi.fn()} openExceptions={exceptions} canOpenExceptions />
      </MemoryRouter>,
    )
    expect(screen.getByRole('link', { name: 'Open afwijking: Vertraging, ernst hoog' })).toHaveAttribute('href', '/exceptions/e-1')
    expect(screen.getByText(/op RIT-0240/)).toBeInTheDocument()
    expect(screen.getByRole('img', { name: 'Blokkerend' })).toBeInTheDocument()
    expect(screen.getByRole('img', { name: 'Waarschuwing' })).toBeInTheDocument()
    unmount()

    render(
      <MemoryRouter>
        <AttentionPanel issues={[]} onNavigate={vi.fn()} openExceptions={exceptions} />
      </MemoryRouter>,
    )
    expect(screen.queryByRole('link')).not.toBeInTheDocument()
    expect(screen.getByText(/Open afwijking: Vertraging, ernst hoog/)).toBeInTheDocument()
  })

  it('shows icon + message per issue and navigates to the named section', async () => {
    const user = userEvent.setup()
    const onNavigate = vi.fn()
    render(<AttentionPanel issues={issues} onNavigate={onNavigate} />)

    expect(screen.getByText('Loslocatie is nog onbekend')).toBeInTheDocument()
    // Never colour-only: each severity carries a labeled icon.
    expect(screen.getAllByRole('img', { name: 'Waarschuwing' })).toHaveLength(2)
    expect(screen.getByRole('img', { name: 'Info' })).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Naar route' }))
    expect(onNavigate).toHaveBeenCalledWith('route', null, issues[0])
    await user.click(screen.getByRole('button', { name: 'Naar prijs' }))
    expect(onNavigate).toHaveBeenCalledWith('prijs', 'price', issues[1])
  })

  it('groups issues of one kind: one item, one action, landing on the first activity of the group', async () => {
    const user = userEvent.setup()
    const onNavigate = vi.fn()
    const activities = [
      dossierActivity({ id: 'a-6', linkedTransportOrderId: 'o-6', linkedOrderNumber: '0006', linkedOrderStatus: 'Confirmed' }),
      dossierActivity({ id: 'a-7', sequence: 2, hasStops: false, activityTypeName: 'Opslag' }),
    ]
    const base = { severity: 'Warning' as const, stage: 'Commercial', section: 'prijs' as const, field: 'price' }
    const first: ReadinessIssue = { ...base, code: 'pricing.missing', message: '0006: verkoopprijs ontbreekt.', transportOrderId: 'o-6' }
    render(
      <AttentionPanel
        activities={activities}
        onNavigate={onNavigate}
        issues={[
          first,
          { ...base, code: 'pricing.missing', message: 'Opslag: verkoopprijs ontbreekt.', activityId: 'a-7' },
          { ...base, code: 'route.date_missing', section: 'route', field: 'stops.plannedFrom', message: '0006: planningsdatum ontbreekt.', transportOrderId: 'o-6' },
          { ...base, code: 'order.confirm.stops', severity: 'Blocking', section: 'route', field: 'stops.unloading', message: '0006: loslocatie is nog onbekend (nodig om te bevestigen).', transportOrderId: 'o-6' },
        ]}
      />,
    )

    expect(screen.getByText('Verkoopprijs ontbreekt')).toBeInTheDocument()
    expect(screen.getByText('2 activiteiten · 0006, Opslag')).toBeInTheDocument()
    expect(screen.getByText('Planningsdatum ontbreekt')).toBeInTheDocument()
    expect(screen.getByText('Loslocatie onbekend')).toBeInTheDocument()
    expect(screen.getAllByRole('button', { name: 'Naar prijs' })).toHaveLength(1)
    expect(screen.getAllByRole('button')).toHaveLength(3)
    // The worst severity first, and the bar takes its tone.
    expect(screen.getAllByRole('listitem')[0]).toHaveTextContent('Loslocatie onbekend')
    expect(screen.getByRole('region', { name: 'Aandacht' })).toHaveClass('is-blocking')
    // The original messages stay readable.
    expect(screen.getByText('Verkoopprijs ontbreekt').closest('li')).toHaveAttribute('title', '0006: verkoopprijs ontbreekt.\nOpslag: verkoopprijs ontbreekt.')

    await user.click(screen.getByRole('button', { name: 'Naar prijs' }))
    expect(onNavigate).toHaveBeenCalledWith('prijs', 'price', first)
    await user.click(screen.getByRole('button', { name: 'Naar planning' }))
    expect(onNavigate).toHaveBeenCalledWith('route', 'stops.plannedFrom', expect.objectContaining({ code: 'route.date_missing' }))
  })

  it('names both stops when the backend says both are unknown, and keeps an unknown code on its own message', () => {
    const base = { severity: 'Warning' as const, stage: 'Planning', section: 'route' as const, field: 'stops.loading' }
    render(
      <AttentionPanel
        onNavigate={vi.fn()}
        issues={[
          { ...base, code: 'order.confirm.stops', message: '0006: laad- en loslocatie zijn nog onbekend (nodig om te bevestigen).' },
          { ...base, code: 'activity.none', section: 'activiteiten', field: null, message: 'Nog geen activiteit toegevoegd.' },
        ]}
      />,
    )
    expect(screen.getByText('Laad- en loslocatie onbekend')).toBeInTheDocument()
    expect(screen.getByText('Nog geen activiteit toegevoegd.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Naar activiteiten' })).toBeInTheDocument()
  })
})
