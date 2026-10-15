import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { CompletenessCard } from '../CompletenessCard'
import type { EmployeeCompleteness } from '../../types/employee'

const INCOMPLETE: EmployeeCompleteness = {
  percentage: 62,
  isComplete: false,
  missingItems: [
    { code: 'national_register_number', label: 'Rijksregisternummer', section: 'hr' },
    { code: 'emergency_contact', label: 'Noodcontact', section: 'noodcontacten' },
    { code: 'contract_document', label: 'Contractdocument', section: 'documenten' },
  ],
}

const COMPLETE: EmployeeCompleteness = { percentage: 100, isComplete: true, missingItems: [] }

describe('CompletenessCard (B15)', () => {
  it('counts the gaps and offers the action that fills each one', () => {
    render(<CompletenessCard completeness={INCOMPLETE} onNavigate={vi.fn()} />)
    expect(screen.getByRole('heading', { name: '3 gegevens ontbreken' })).toBeInTheDocument()
    expect(screen.getByRole('progressbar')).toHaveAttribute('aria-valuenow', '62')
    expect(screen.getByRole('button', { name: 'Rijksregisternummer: Aanvullen' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Contractdocument: Bekijken' })).toBeInTheDocument()
  })

  it('is neutral by default and only emphasises gaps that block deployment', () => {
    const { container, rerender } = render(<CompletenessCard completeness={INCOMPLETE} onNavigate={vi.fn()} />)
    expect(container.querySelector('.completeness-card')).not.toHaveClass('is-attention')
    rerender(
      <CompletenessCard
        completeness={{ ...INCOMPLETE, missingItems: [...INCOMPLETE.missingItems, { code: 'driving_licence_document', label: 'Rijbewijsdocument', section: 'documenten' }] }}
        onNavigate={vi.fn()}
      />,
    )
    expect(container.querySelector('.completeness-card')).toHaveClass('is-attention')
    expect(screen.getByRole('img', { name: 'Waarschuwing' })).toBeInTheDocument()
  })

  it('shows nothing once the dossier is complete', () => {
    const { container } = render(<CompletenessCard completeness={COMPLETE} onNavigate={vi.fn()} />)
    expect(container).toBeEmptyDOMElement()
  })

  it('passes the section and the item to onNavigate', async () => {
    const onNavigate = vi.fn()
    render(<CompletenessCard completeness={INCOMPLETE} onNavigate={onNavigate} />)
    await userEvent.click(screen.getByRole('button', { name: 'Noodcontact: Aanvullen' }))
    expect(onNavigate).toHaveBeenCalledWith('noodcontacten', INCOMPLETE.missingItems[1])
  })

  it('renders plain text without actions for read-only viewers', () => {
    render(<CompletenessCard completeness={INCOMPLETE} />)
    expect(screen.queryByRole('button')).not.toBeInTheDocument()
    expect(screen.getByText('Rijksregisternummer')).toBeInTheDocument()
  })

  it('leaves a section gated by canNavigate without an action', () => {
    render(<CompletenessCard completeness={INCOMPLETE} onNavigate={vi.fn()} canNavigate={(section) => section !== 'documenten'} />)
    expect(screen.queryByRole('button', { name: /Contractdocument/ })).not.toBeInTheDocument()
    expect(screen.getByText('Contractdocument')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Rijksregisternummer: Aanvullen' })).toBeInTheDocument()
  })
})
