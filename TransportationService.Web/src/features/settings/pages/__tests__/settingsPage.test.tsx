import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { ApiError } from '../../../../api/apiClient'
import { SettingsPage } from '../SettingsPage'
import type { CompanySettings } from '../../types'

const api = vi.hoisted(() => ({ get: vi.fn(), update: vi.fn() }))

vi.mock('../../api/settingsApi', () => ({
  getCompanySettings: api.get,
  updateCompanySettings: api.update,
}))
vi.mock('../../../reference/api/countriesApi', () => ({
  getCountryOptions: () => Promise.resolve([]),
}))
vi.mock('../../../auth/authContextValue', () => ({
  useAuth: () => ({ hasPermission: (code: string) => code === 'company_settings.manage' }),
}))
vi.mock('../../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }),
}))

function settings(overrides: Partial<CompanySettings> = {}): CompanySettings {
  return {
    companyLegalName: 'Acme BV', tradingName: null, companyNumber: null, vatNumber: null,
    street: null, houseNumber: null, postalCode: null, city: null, countryCode: 'BE',
    operationalStreet: null, operationalHouseNumber: null, operationalPostalCode: null,
    operationalCity: null, operationalCountryCode: null,
    email: null, phoneNumber: null, website: null,
    defaultLanguage: 'nl', timezone: 'Europe/Brussels', defaultCurrency: 'EUR', dateFormat: 'dd/MM/yyyy',
    decimalSeparator: ',', defaultWeightUnit: 'kg', defaultDistanceUnit: 'km',
    iban: 'BE68539007547034', invoiceEmail: null, paymentTermDays: 30, defaultVatRatePercent: 21,
    defaultLoadingMinutes: 30, defaultUnloadingMinutes: 30,
    trainingConflictSeverity: 'Warning', capacityConflictSeverity: 'Warning', shiftOverlapConflictSeverity: 'Warning',
    redeliveryMode: 'Manual', etaShiftNotifyMinutes: null, qualificationExpiryWarningDays: 30,
    employeeNumberPrefix: 'EMP-', employeeNumberNextValue: 1,
    customerNumberPrefix: 'KL-', customerNumberNextValue: 1,
    driverNumberPrefix: 'DRV-', driverNumberNextValue: 1,
    orderNumberPrefix: 'ORD-', orderNumberNextValue: 105,
    tripNumberPrefix: 'RIT-', tripNumberNextValue: 1,
    invoiceNumberPrefix: 'F-', invoiceNumberNextValue: 42,
    vehicleNumberPrefix: 'VH-', vehicleNumberNextValue: 1,
    trailerNumberPrefix: 'TR-', trailerNumberNextValue: 1,
    defaultPageSize: 25, logoReference: 'oud/logo.png',
    ...overrides,
  }
}

function renderPage() {
  return render(
    <MemoryRouter>
      <SettingsPage />
    </MemoryRouter>,
  )
}

beforeEach(() => {
  vi.clearAllMocks()
  api.get.mockResolvedValue(settings())
  api.update.mockImplementation((input: CompanySettings) => Promise.resolve(input))
})

describe('SettingsPage — MD-M15 cleanup', () => {
  it('no longer offers the legacy invoice counter, company IBAN or logo reference, and still sends their values', async () => {
    const user = userEvent.setup()
    renderPage()

    expect(await screen.findByText('Factuurnummering stel je in per facturerende entiteit (Eigen bedrijven).')).toBeInTheDocument()
    expect(screen.getByText('Het IBAN op facturen komt van de facturerende entiteit.')).toBeInTheDocument()
    expect(screen.queryByLabelText('Factuur volgnr')).not.toBeInTheDocument()
    expect(screen.queryByLabelText('Factuur prefix')).not.toBeInTheDocument()
    expect(screen.queryByLabelText('IBAN')).not.toBeInTheDocument()
    expect(screen.queryByLabelText('Logo-referentie')).not.toBeInTheDocument()
    expect(screen.getByLabelText('Standaard betaaltermijn (dagen)')).toHaveValue(30)
    expect(screen.getByText('Alleen gebruikt als klant en facturerende entiteit er geen hebben.')).toBeInTheDocument()

    await user.clear(screen.getByLabelText('Standaard betaaltermijn (dagen)'))
    await user.type(screen.getByLabelText('Standaard betaaltermijn (dagen)'), '45')
    await user.click(screen.getAllByRole('button', { name: 'Opslaan' })[0])

    await waitFor(() => expect(api.update).toHaveBeenCalled())
    expect(api.update.mock.calls[0][0]).toMatchObject({
      paymentTermDays: 45,
      // Hidden fields travel unchanged so nothing is cleared.
      invoiceNumberPrefix: 'F-',
      invoiceNumberNextValue: 42,
      iban: 'BE68539007547034',
      logoReference: 'oud/logo.png',
    })
  })
})

describe('SettingsPage — numbering counters (MD-H5)', () => {
  it('shows the server refusal next to the counter and keeps what the user typed', async () => {
    const message = 'Het volgende opdrachtnummer kan niet lager: dat nummer of een volgend nummer is al in gebruik (hoogste in gebruik: ORD-0104).'
    api.update.mockRejectedValue(
      new ApiError(message, 400, { code: 'common.validation_failed', errors: { OrderNumberNextValue: [message] } }),
    )
    const user = userEvent.setup()
    renderPage()

    const counter = await screen.findByLabelText('Order volgnr')
    await user.clear(counter)
    await user.type(counter, '50')
    await user.click(screen.getAllByRole('button', { name: 'Opslaan' })[0])

    const errors = await screen.findAllByText(message)
    // Next to the input (FormField error) — the action bar may repeat the server message.
    expect(errors.some((node) => node.classList.contains('ui-form-field-error'))).toBe(true)
    expect(screen.getByLabelText('Order volgnr')).toHaveValue(50)

    // Editing the counter answers its error.
    await user.type(screen.getByLabelText('Order volgnr'), '0')
    expect(screen.queryAllByText(message).some((node) => node.classList.contains('ui-form-field-error'))).toBe(false)
  })
})
