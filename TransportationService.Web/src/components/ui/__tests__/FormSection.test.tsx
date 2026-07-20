import { describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { FormSection } from '../FormSection'

describe('FormSection', () => {
  it('renders children directly when not collapsible', () => {
    render(
      <FormSection title="Algemeen">
        <span>Veldinhoud</span>
      </FormSection>,
    )
    expect(screen.getByText('Veldinhoud')).toBeInTheDocument()
    expect(screen.queryByRole('button')).not.toBeInTheDocument()
  })

  it('starts collapsed and toggles open via the legend button', async () => {
    const user = userEvent.setup()
    render(
      <FormSection title="Kwalificaties (optioneel)" collapsible>
        <span>Kwalificatievelden</span>
      </FormSection>,
    )

    const toggle = screen.getByRole('button', { name: /Kwalificaties/ })
    expect(toggle).toHaveAttribute('aria-expanded', 'false')
    expect(screen.queryByText('Kwalificatievelden')).not.toBeInTheDocument()

    await user.click(toggle)
    expect(toggle).toHaveAttribute('aria-expanded', 'true')
    expect(screen.getByText('Kwalificatievelden')).toBeInTheDocument()

    await user.click(toggle)
    expect(screen.queryByText('Kwalificatievelden')).not.toBeInTheDocument()
  })

  it('honours defaultOpen', () => {
    render(
      <FormSection title="Technische gegevens" collapsible defaultOpen>
        <span>Techniek</span>
      </FormSection>,
    )
    expect(screen.getByText('Techniek')).toBeInTheDocument()
  })
})
