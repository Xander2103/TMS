import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { SectionedForm, type SectionDef } from '../SectionedForm'

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
})
