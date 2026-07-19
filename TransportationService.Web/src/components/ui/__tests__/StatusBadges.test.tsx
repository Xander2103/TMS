import { describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import { StatusBadges } from '../StatusBadges'

describe('StatusBadges', () => {
  it('renders a single administrative badge', () => {
    render(<StatusBadges active />)
    expect(screen.getAllByText('Actief')).toHaveLength(1)
  })

  it('never duplicates the active label when an operational status is shown', () => {
    render(
      <StatusBadges
        active
        operational={{ label: 'Beschikbaar', tone: 'success' }}
      />,
    )
    expect(screen.getAllByText('Actief')).toHaveLength(1)
    expect(screen.getByText('Beschikbaar')).toBeInTheDocument()
  })

  it('shows the blocked badge with its reason', () => {
    render(
      <StatusBadges
        active={false}
        blocked={{ isBlocked: true, reason: 'Rijverbod' }}
      />,
    )
    expect(screen.getByText('Inactief')).toBeInTheDocument()
    expect(screen.getByText('Geblokkeerd')).toBeInTheDocument()
    expect(screen.getByText(/Rijverbod/)).toBeInTheDocument()
  })

  it('shows the operational reason when not blocked', () => {
    render(
      <StatusBadges
        active
        operational={{ label: 'Buiten dienst', tone: 'danger', reason: 'Motorschade' }}
      />,
    )
    expect(screen.getByText(/Motorschade/)).toBeInTheDocument()
  })
})
