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

const COMPLETE: EmployeeCompleteness = {
  percentage: 100,
  isComplete: true,
  missingItems: [],
}

describe('CompletenessCard', () => {
  it('shows the percentage and a chip for every missing item', () => {
    render(<CompletenessCard completeness={INCOMPLETE} onNavigate={vi.fn()} />)
    expect(screen.getByText('Dossier 62% compleet')).toBeInTheDocument()
    expect(screen.getByRole('progressbar')).toHaveAttribute('aria-valuenow', '62')
    expect(screen.getByRole('button', { name: 'Rijksregisternummer' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Noodcontact' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Contractdocument' })).toBeInTheDocument()
  })

  it('shows the success state and no missing-item list when complete', () => {
    render(<CompletenessCard completeness={COMPLETE} onNavigate={vi.fn()} />)
    expect(screen.getByText('Dossier compleet ✓')).toBeInTheDocument()
    expect(screen.queryByRole('button')).not.toBeInTheDocument()
  })

  it('calls onNavigate with the missing item\'s section when its chip is clicked', async () => {
    const onNavigate = vi.fn()
    render(<CompletenessCard completeness={INCOMPLETE} onNavigate={onNavigate} />)
    await userEvent.click(screen.getByRole('button', { name: 'Noodcontact' }))
    expect(onNavigate).toHaveBeenCalledWith('noodcontacten')
    await userEvent.click(screen.getByRole('button', { name: 'Contractdocument' }))
    expect(onNavigate).toHaveBeenCalledWith('documenten')
  })

  it('renders missing items as non-interactive chips (no buttons) for read-only viewers', () => {
    render(<CompletenessCard completeness={INCOMPLETE} />)
    expect(screen.queryByRole('button')).not.toBeInTheDocument()
    expect(screen.getByText('Rijksregisternummer')).toBeInTheDocument()
  })
})
