import { describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { FormSection } from '../FormSection'
import { SectionedFormBodyContext } from '../sectionedFormContext'

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

describe('FormSection variants', () => {
  it('is framed (fieldset + legend) outside a SectionedForm', () => {
    const { container } = render(
      <FormSection title="Openingstijden">
        <span>Inhoud</span>
      </FormSection>,
    )
    expect(container.querySelector('fieldset.ui-form-section-framed legend')).toHaveTextContent('Openingstijden')
  })

  it('is flat (section + heading, no fieldset) inside a SectionedForm body', () => {
    const { container } = render(
      <SectionedFormBodyContext.Provider value={true}>
        <FormSection title="Contactpersonen" description="Beheer contactpersonen.">
          <span>Inhoud</span>
        </FormSection>
      </SectionedFormBodyContext.Provider>,
    )
    expect(container.querySelector('fieldset')).toBeNull()
    const section = container.querySelector('section.ui-form-section-flat')!
    expect(section).not.toBeNull()
    expect(screen.getByRole('heading', { level: 3, name: 'Contactpersonen' })).toBeInTheDocument()
    expect(section.getAttribute('aria-labelledby')).toBe(screen.getByRole('heading', { level: 3 }).id)
    expect(screen.getByText('Beheer contactpersonen.')).toBeInTheDocument()
  })

  it('honours an explicit variant', () => {
    const { container } = render(
      <FormSection title="Los" variant="flat">
        <span>Inhoud</span>
      </FormSection>,
    )
    expect(container.querySelector('section.ui-form-section-flat')).not.toBeNull()
  })
})
