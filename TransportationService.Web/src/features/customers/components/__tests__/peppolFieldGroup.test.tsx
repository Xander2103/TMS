import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { PeppolFieldGroup } from '../PeppolFieldGroup'
import { combinePeppolValue, parsePeppolValue, peppolFormatError } from '../../utils/peppolValue'
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

describe('parsePeppolValue / combinePeppolValue', () => {
  it('maps a combined value onto scheme + participant id', () => {
    expect(parsePeppolValue('0208:0123456789')).toEqual({ scheme: '0208', participantId: '0123456789' })
  })

  it('maps a bare value onto the participant id without inventing a scheme', () => {
    expect(parsePeppolValue('0123456789')).toEqual({ scheme: '', participantId: '0123456789' })
  })

  it('passes an invalid combined value through verbatim so validation can flag it', () => {
    expect(parsePeppolValue('abcd:123')).toEqual({ scheme: '', participantId: 'abcd:123' })
    expect(parsePeppolValue('0208:12:34')).toEqual({ scheme: '', participantId: '0208:12:34' })
  })

  it('round-trips combine → parse', () => {
    expect(parsePeppolValue(combinePeppolValue('9925', 'BE0123456749'))).toEqual({
      scheme: '9925',
      participantId: 'BE0123456749',
    })
  })

  it('flags invalid combined formats (as a translation key) and accepts valid ones', () => {
    expect(peppolFormatError('abcd:123')).toBe('customers.vatValidation.peppolFormat')
    expect(peppolFormatError('0208:12:34')).toBe('customers.vatValidation.peppolFormat')
    expect(peppolFormatError('0208:0123456789')).toBeNull()
    expect(peppolFormatError('0123456789')).toBeNull()
    expect(peppolFormatError('')).toBeNull()
  })
})

describe('PeppolFieldGroup', () => {
  it('renders one combined Peppol-ID field', () => {
    setup()
    expect(screen.getByRole('group', { name: /Peppol/i })).toBeInTheDocument()
    expect(screen.getByLabelText(/Peppol-ID/i)).toHaveValue('0208:0123456789')
    // Scheme + participant are internal storage details, hidden by default.
    expect(screen.queryByLabelText(/Schema/i)).not.toBeInTheDocument()
    expect(screen.queryByLabelText(/Participant-ID/i)).not.toBeInTheDocument()
  })

  it('emits parsed scheme + participant when a combined value is typed', async () => {
    const { onChange } = setup({ scheme: '', participantId: '0208:012345678' })
    await userEvent.type(screen.getByLabelText(/Peppol-ID/i), '9')
    expect(onChange).toHaveBeenLastCalledWith({ scheme: '0208', participantId: '0123456789' })
  })

  it('emits an empty scheme when a bare number is typed', async () => {
    const { onChange } = setup({ scheme: '', participantId: '' })
    await userEvent.type(screen.getByLabelText(/Peppol-ID/i), '9')
    expect(onChange).toHaveBeenCalledWith({ scheme: '', participantId: '9' })
  })

  it('shows a format error for an invalid combined value', () => {
    setup({ scheme: '', participantId: 'abcd:123' })
    expect(screen.getByRole('alert')).toHaveTextContent(/ongeldig peppol-id/i)
  })

  it('shows the validated status', () => {
    setup({ status: 'auto' })
    expect(screen.getByText('Gevalideerd')).toBeInTheDocument()
  })

  it('shows the not-found status', () => {
    setup({ status: 'not-found' })
    expect(screen.getByText('Niet gevonden')).toBeInTheDocument()
  })

  it('shows the manual status', () => {
    setup({ status: 'manual' })
    expect(screen.getByText('Manueel ingevoerd')).toBeInTheDocument()
  })

  it('reveals separate scheme + participant fields behind Geavanceerd', async () => {
    const { onChange } = setup()
    await userEvent.click(screen.getByRole('button', { name: /Geavanceerd/i }))
    expect(screen.getByLabelText(/Schema/i)).toHaveValue('0208')
    expect(screen.getByLabelText(/Participant-ID/i)).toHaveValue('0123456789')
    await userEvent.selectOptions(screen.getByLabelText(/Schema/i), '9925')
    expect(onChange).toHaveBeenCalledWith({ scheme: '9925', participantId: '0123456789' })
  })

  it('disables the input and hides Geavanceerd when disabled', () => {
    setup({ disabled: true })
    expect(screen.getByLabelText(/Peppol-ID/i)).toBeDisabled()
    expect(screen.queryByRole('button', { name: /Geavanceerd/i })).not.toBeInTheDocument()
  })

  it('warns when the scheme is not in the catalog', () => {
    setup({ scheme: '0000', participantId: '123' })
    expect(screen.getByRole('status')).toHaveTextContent(/staat niet in de schemalijst/i)
  })
})
