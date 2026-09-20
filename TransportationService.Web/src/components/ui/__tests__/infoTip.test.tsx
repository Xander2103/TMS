import { describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { InfoTip } from '../InfoTip'

describe('InfoTip', () => {
  it('shows the tooltip on hover and hides it again on mouse leave', async () => {
    const user = userEvent.setup()
    render(<InfoTip text="Uitleg bij dit veld." />)

    const trigger = screen.getByRole('button', { name: 'Meer informatie' })
    expect(screen.queryByRole('tooltip')).not.toBeInTheDocument()

    await user.hover(trigger)
    expect(screen.getByRole('tooltip')).toHaveTextContent('Uitleg bij dit veld.')

    await user.unhover(trigger)
    expect(screen.queryByRole('tooltip')).not.toBeInTheDocument()
  })

  it('shows the tooltip on keyboard focus and closes it with Escape', async () => {
    const user = userEvent.setup()
    render(<InfoTip text="Toetsenborduitleg." />)

    await user.tab()
    expect(screen.getByRole('button', { name: 'Meer informatie' })).toHaveFocus()
    expect(screen.getByRole('tooltip')).toHaveTextContent('Toetsenborduitleg.')

    await user.keyboard('{Escape}')
    expect(screen.queryByRole('tooltip')).not.toBeInTheDocument()

    // Blur (focus leaving the widget) also closes it.
    await user.hover(screen.getByRole('button', { name: 'Meer informatie' }))
    expect(screen.getByRole('tooltip')).toBeInTheDocument()
    await user.tab()
    expect(screen.queryByRole('tooltip')).not.toBeInTheDocument()
  })

  it('wires aria-describedby from the trigger to the tooltip element', async () => {
    const user = userEvent.setup()
    render(<InfoTip text="Beschrijving." ariaLabel="Hulp bij dimona" />)

    const trigger = screen.getByRole('button', { name: 'Hulp bij dimona' })
    await user.hover(trigger)
    const tooltip = screen.getByRole('tooltip')
    expect(tooltip.id).toBeTruthy()
    expect(trigger).toHaveAttribute('aria-describedby', tooltip.id)
    expect(trigger).toHaveAccessibleDescription('Beschrijving.')
  })

  it('renders an external link that stays reachable with the keyboard', async () => {
    const user = userEvent.setup()
    render(
      <>
        <InfoTip text="Met link." link={{ href: 'https://example.org/info', label: 'Meer info' }} />
        <button type="button">Volgende</button>
      </>,
    )

    await user.tab()
    const link = screen.getByRole('link', { name: 'Meer info' })
    expect(link).toHaveAttribute('href', 'https://example.org/info')
    expect(link).toHaveAttribute('target', '_blank')
    expect(link).toHaveAttribute('rel', 'noopener noreferrer')

    // Tab moves focus from the trigger to the link: the tooltip must stay open.
    await user.tab()
    expect(link).toHaveFocus()
    expect(screen.getByRole('tooltip')).toBeInTheDocument()

    // Leaving the widget closes it.
    await user.tab()
    expect(screen.getByRole('button', { name: 'Volgende' })).toHaveFocus()
    expect(screen.queryByRole('tooltip')).not.toBeInTheDocument()
  })
})
