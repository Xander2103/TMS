import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { AgreementValidationPanel } from '../AgreementValidationPanel'
import type { PricingConfigCheck } from '../../api/pricingApi'

const state = vi.hoisted(() => ({
  checks: [] as PricingConfigCheck[],
  validate: vi.fn(),
}))

vi.mock('../../api/pricingApi', async (importOriginal) => {
  const original = await importOriginal<typeof import('../../api/pricingApi')>()
  return {
    ...original,
    validateAgreementConfiguration: state.validate,
  }
})

beforeEach(() => {
  vi.clearAllMocks()
  state.checks = []
  state.validate.mockImplementation(() => Promise.resolve(state.checks))
})

describe('AgreementValidationPanel', () => {
  it('shows the clean state when no problems are found', async () => {
    render(<AgreementValidationPanel agreementId="agr-1" />)

    expect(await screen.findByText('Geen problemen gevonden.')).toBeInTheDocument()
    expect(state.validate).toHaveBeenCalledWith('agr-1')
  })

  it('lists error and warning findings with distinct styling', async () => {
    state.checks = [
      { severity: 'error', message: "Regels 'A' en 'B' overlappen in geldigheid met gelijke specificiteit — dit blokkeert prijsberekening in die periode." },
      { severity: 'warning', message: "Staffel 'X' heeft een gat tussen 2 en 4." },
    ]
    render(<AgreementValidationPanel agreementId="agr-1" />)

    const errorItem = await screen.findByText(/overlappen in geldigheid/)
    const warningItem = await screen.findByText(/heeft een gat tussen/)
    expect(errorItem).toHaveClass('pricing-table-validation-error')
    expect(warningItem).toHaveClass('pricing-table-validation-warning')
    expect(screen.queryByText('Geen problemen gevonden.')).not.toBeInTheDocument()
  })

  it('re-runs the check when "Controleer configuratie" is clicked', async () => {
    const user = userEvent.setup()
    render(<AgreementValidationPanel agreementId="agr-1" />)
    await screen.findByText('Geen problemen gevonden.')

    state.checks = [{ severity: 'warning', message: 'Deze gedeelde tabel is aan geen enkele klant gekoppeld.' }]
    await user.click(screen.getByRole('button', { name: 'Controleer configuratie' }))

    await waitFor(() => expect(state.validate).toHaveBeenCalledTimes(2))
    expect(await screen.findByText('Deze gedeelde tabel is aan geen enkele klant gekoppeld.')).toBeInTheDocument()
  })

  it('shows an inline error when the check fails to load', async () => {
    // An Error without its own message falls back to the panel's default text.
    state.validate.mockRejectedValue(new Error())
    render(<AgreementValidationPanel agreementId="agr-1" />)

    expect(await screen.findByText('De configuratie kon niet worden gecontroleerd.')).toBeInTheDocument()
  })
})
