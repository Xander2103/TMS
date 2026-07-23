import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { PeppolFieldGroup } from '../PeppolFieldGroup'
import type { PeppolScheme } from '../../types'

const schemes: PeppolScheme[] = [
  { code: '0208', label: 'Belgisch ondernemingsnummer', countryCode: 'BE' },
  { code: '9925', label: 'Belgisch BTW-nummer', countryCode: 'BE' },
]

function setup(overrides: Partial<Parameters<typeof PeppolFieldGroup>[0]> = {}) {
  const onChange = vi.fn()
  render(
    <PeppolFieldGroup
      scheme="0208"
      participantId="0123456789"
      status="manual"
      schemes={schemes}
      onChange={onChange}
      {...overrides}
    />,
  )
  return { onChange }
}

describe('PeppolFieldGroup', () => {
  it('renders scheme and participant id as one grouped control', () => {
    setup()
    expect(screen.getByRole('group', { name: /Peppol/i })).toBeInTheDocument()
    expect(screen.getByLabelText(/Schema/i)).toHaveValue('0208')
    expect(screen.getByLabelText(/Participant-ID/i)).toHaveValue('0123456789')
  })

  it('emits both values when the scheme changes', async () => {
    const { onChange } = setup()
    await userEvent.selectOptions(screen.getByLabelText(/Schema/i), '9925')
    expect(onChange).toHaveBeenCalledWith({ scheme: '9925', participantId: '0123456789' })
  })

  it('emits both values when the id changes', async () => {
    const { onChange } = setup({ participantId: '' })
    await userEvent.type(screen.getByLabelText(/Participant-ID/i), '9')
    expect(onChange).toHaveBeenCalledWith({ scheme: '0208', participantId: '9' })
  })

  it('shows the auto-retrieved status', () => {
    setup({ status: 'auto' })
    expect(screen.getByText(/automatisch opgehaald/i)).toBeInTheDocument()
  })

  it('shows the not-found status', () => {
    setup({ status: 'not-found' })
    expect(screen.getByText(/niet gevonden/i)).toBeInTheDocument()
  })

  it('disables inputs when disabled', () => {
    setup({ disabled: true })
    expect(screen.getByLabelText(/Schema/i)).toBeDisabled()
    expect(screen.getByLabelText(/Participant-ID/i)).toBeDisabled()
  })
})
