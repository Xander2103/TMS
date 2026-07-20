import { beforeAll, describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { ValidationSummary } from '../ValidationSummary'

beforeAll(() => {
  // jsdom does not implement scrollIntoView.
  Element.prototype.scrollIntoView = vi.fn()
})

describe('ValidationSummary', () => {
  it('renders nothing without errors', () => {
    const { container } = render(<ValidationSummary message={null} fieldErrors={{}} />)
    expect(container).toBeEmptyDOMElement()
  })

  it('shows the form-wide message as an alert', () => {
    render(<ValidationSummary message="Er ging iets mis met de invoer." />)
    const alert = screen.getByRole('alert')
    expect(alert).toHaveTextContent('Opslaan is niet gelukt')
    expect(alert).toHaveTextContent('Er ging iets mis met de invoer.')
  })

  it('lists field errors with labels when provided', () => {
    render(
      <ValidationSummary
        message="Controleer de invoer."
        fieldErrors={{ vatNumber: ['Ongeldig controlegetal.'], iban: ['Ongeldig formaat.'] }}
        fieldLabels={{ vatNumber: 'BTW-nummer' }}
      />,
    )
    expect(screen.getByText('BTW-nummer:')).toBeInTheDocument()
    expect(screen.getByText(/Ongeldig controlegetal\./)).toBeInTheDocument()
    // Unlabelled field shows only its message — no technical path leaks to the user.
    expect(screen.getByText('Ongeldig formaat.')).toBeInTheDocument()
    expect(screen.queryByText(/iban/i)).not.toBeInTheDocument()
  })

  it('does not repeat the message when it equals the single field error', () => {
    render(<ValidationSummary message="Ongeldig BTW-nummer." fieldErrors={{ vatNumber: ['Ongeldig BTW-nummer.'] }} />)
    expect(screen.getAllByText('Ongeldig BTW-nummer.')).toHaveLength(1)
  })
})
