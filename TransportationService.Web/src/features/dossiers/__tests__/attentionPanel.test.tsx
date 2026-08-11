import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { AttentionPanel } from '../components/AttentionPanel'
import type { ReadinessIssue } from '../types'

const issues: ReadinessIssue[] = [
  { code: 'route.unloading_missing', severity: 'Warning', message: 'Loslocatie is nog onbekend', section: 'route', field: null, stage: 'Planning' },
  { code: 'pricing.incomplete', severity: 'Warning', message: 'Wachttijd 1,5 u heeft geen prijs', section: 'prijs', field: null, stage: 'Commercial' },
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
    expect(onNavigate).toHaveBeenCalledWith('route')
    await user.click(screen.getByRole('button', { name: 'Ga naar prijs' }))
    expect(onNavigate).toHaveBeenCalledWith('prijs')
  })
})
