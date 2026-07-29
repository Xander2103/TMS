import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { SectionedForm, type SectionDef } from '../SectionedForm'
import { FormSection } from '../FormSection'

function buildSections(): SectionDef[] {
  return [
    { id: 'algemeen', label: 'Algemeen', render: () => <input aria-label="naam" /> },
    { id: 'hr', label: 'HR', optional: true, hasError: true, render: () => <input aria-label="iban" /> },
    { id: 'docs', label: 'Documenten', panel: true, render: () => <div>panel body</div> },
  ]
}

function renderForm(active = 'algemeen', onActiveChange = vi.fn()) {
  return render(
    <SectionedForm
      sections={buildSections()}
      activeId={active}
      onActiveChange={onActiveChange}
      actions={<button type="submit">Opslaan</button>}
    />,
  )
}

describe('SectionedForm', () => {
  it('renders only the active section body', () => {
    renderForm('algemeen')
    expect(screen.getByLabelText('naam')).toBeInTheDocument()
    expect(screen.queryByLabelText('iban')).not.toBeInTheDocument()
  })

  it('exposes tabs with roving selection and switches on click', async () => {
    const onActiveChange = vi.fn()
    renderForm('algemeen', onActiveChange)
    const tab = screen.getByRole('tab', { name: /HR/ })
    expect(tab).toHaveAttribute('aria-selected', 'false')
    await userEvent.click(tab)
    expect(onActiveChange).toHaveBeenCalledWith('hr')
  })

  it('marks a tab with a validation error', () => {
    renderForm('algemeen')
    expect(screen.getByRole('tab', { name: /HR/ })).toHaveAttribute('data-has-error', 'true')
  })

  it('does not add the required marker to optional sections', () => {
    renderForm('algemeen')
    expect(screen.getByRole('tab', { name: /HR/ })).not.toHaveAttribute('data-required')
  })

  it('hides the shared actions on a panel section', () => {
    renderForm('docs')
    expect(screen.queryByRole('button', { name: 'Opslaan' })).not.toBeInTheDocument()
  })

  it('shows the shared actions on a normal section', () => {
    renderForm('algemeen')
    expect(screen.getByRole('button', { name: 'Opslaan' })).toBeInTheDocument()
  })

  it('provides a mobile select mirroring the tabs', async () => {
    const onActiveChange = vi.fn()
    renderForm('algemeen', onActiveChange)
    await userEvent.selectOptions(screen.getByLabelText('Sectie'), 'hr')
    expect(onActiveChange).toHaveBeenCalledWith('hr')
  })

  it('moves selection with arrow keys', async () => {
    const onActiveChange = vi.fn()
    renderForm('algemeen', onActiveChange)
    screen.getByRole('tab', { name: /Algemeen/ }).focus()
    await userEvent.keyboard('{ArrowRight}')
    expect(onActiveChange).toHaveBeenCalledWith('hr')
  })

  describe('orientation', () => {
    it('defaults to a horizontal tablist (no aria-orientation)', () => {
      renderForm('algemeen')
      expect(screen.getByRole('tablist', { name: 'Formuliersecties' })).not.toHaveAttribute('aria-orientation')
    })

    it('orientation="left" renders a vertical rail (aria-orientation) navigable with ArrowUp/ArrowDown', async () => {
      const onActiveChange = vi.fn()
      render(
        <SectionedForm
          sections={buildSections()}
          activeId="algemeen"
          onActiveChange={onActiveChange}
          orientation="left"
          actions={<button type="submit">Opslaan</button>}
        />,
      )
      const tablist = screen.getByRole('tablist', { name: 'Formuliersecties' })
      expect(tablist).toHaveAttribute('aria-orientation', 'vertical')

      screen.getByRole('tab', { name: /Algemeen/ }).focus()
      await userEvent.keyboard('{ArrowDown}')
      expect(onActiveChange).toHaveBeenCalledWith('hr')
    })

    it('orientation="left" still renders exactly one active section body', () => {
      render(
        <SectionedForm
          sections={buildSections()}
          activeId="algemeen"
          onActiveChange={vi.fn()}
          orientation="left"
        />,
      )
      expect(screen.getByLabelText('naam')).toBeInTheDocument()
      expect(screen.queryByLabelText('iban')).not.toBeInTheDocument()
    })
  })

  describe('active section is always expanded', () => {
    function Harness() {
      const [active, setActive] = useState('algemeen')
      const sections: SectionDef[] = [
        { id: 'algemeen', label: 'Algemeen', render: () => <input aria-label="naam" /> },
        {
          id: 'bank',
          label: 'Bank',
          render: () => (
            <FormSection title="Bank" collapsible defaultOpen={false}>
              <input aria-label="iban" />
            </FormSection>
          ),
        },
      ]
      return <SectionedForm sections={sections} activeId={active} onActiveChange={setActive} />
    }

    it('shows collapsible FormSection content immediately when its section becomes active', async () => {
      render(<Harness />)
      expect(screen.queryByLabelText('iban')).not.toBeInTheDocument()
      // One click on the tab — the content must be visible without a second (accordion) click.
      await userEvent.click(screen.getByRole('tab', { name: /Bank/ }))
      expect(screen.getByLabelText('iban')).toBeInTheDocument()
      // No collapse toggle is offered inside a sectioned form.
      expect(screen.queryByRole('button', { name: /Bank/ })).not.toBeInTheDocument()
    })

    it('keeps the newly active section expanded when switching back and forth', async () => {
      render(<Harness />)
      await userEvent.click(screen.getByRole('tab', { name: /Bank/ }))
      await userEvent.click(screen.getByRole('tab', { name: /Algemeen/ }))
      await userEvent.click(screen.getByRole('tab', { name: /Bank/ }))
      expect(screen.getByLabelText('iban')).toBeInTheDocument()
    })
  })
})
