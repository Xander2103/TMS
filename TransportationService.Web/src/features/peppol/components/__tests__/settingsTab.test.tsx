import { describe, expect, it, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { SettingsTab } from '../SettingsTab'
import type { PeppolSettings } from '../../api/peppolApi'
import type { LegalEntity } from '../../../legal-entities/types'

vi.mock('../../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }),
}))

const api = vi.hoisted(() => ({
  getPeppolSettings: vi.fn(),
  savePeppolSettings: vi.fn(),
  testPeppolConnection: vi.fn(),
  listLegalEntities: vi.fn(),
}))
vi.mock('../../api/peppolApi', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../api/peppolApi')>()),
  getPeppolSettings: api.getPeppolSettings,
  savePeppolSettings: api.savePeppolSettings,
  testPeppolConnection: api.testPeppolConnection,
}))
vi.mock('../../../legal-entities/api/legalEntitiesApi', () => ({
  listLegalEntities: api.listLegalEntities,
}))

function entity(overrides: Partial<LegalEntity> = {}): LegalEntity {
  return {
    id: 'le-1',
    legalName: 'Transport BV',
    tradingName: null,
    companyNumber: null,
    vatNumber: 'BE0123456789',
    peppolId: '0208:0123456789',
    peppolScheme: '0208',
    street: null,
    houseNumber: null,
    postalCode: null,
    city: null,
    countryCode: 'BE',
    email: null,
    phoneNumber: null,
    website: null,
    iban: 'BE68539007547034',
    bic: null,
    bankName: null,
    defaultCurrency: 'EUR',
    paymentTermDays: 30,
    invoiceNumberFormat: '{YYYY}-{SEQ}',
    invoiceSequencePadding: 4,
    invoicePrefix: null,
    creditNotePrefix: null,
    invoiceFooter: null,
    hasLogo: false,
    logoFileName: null,
    isActive: true,
    isDefault: true,
    ...overrides,
  }
}

function settings(overrides: Partial<PeppolSettings> = {}): PeppolSettings {
  return {
    legalEntityId: 'le-1',
    legalEntityName: 'Transport BV',
    enabled: false,
    environment: 'Sandbox',
    providerKey: 'sandbox',
    invoiceEmailFallback: null,
    defaultInvoiceNote: null,
    ...overrides,
  }
}

function renderTab(props: Partial<Parameters<typeof SettingsTab>[0]> = {}) {
  return render(
    <MemoryRouter>
      <SettingsTab canConfigure canValidate {...props} />
    </MemoryRouter>,
  )
}

describe('SettingsTab', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    api.listLegalEntities.mockResolvedValue([entity()])
    api.getPeppolSettings.mockResolvedValue(settings())
    api.savePeppolSettings.mockResolvedValue(settings({ enabled: true }))
  })

  it('saves the settings with the edited values in the PUT payload', async () => {
    const user = userEvent.setup()
    renderTab()

    await user.click(await screen.findByLabelText('Actief'))
    await user.selectOptions(screen.getByLabelText('Omgeving'), 'Live')
    await user.type(screen.getByLabelText('E-mailterugval'), 'facturen@transport.be')
    await user.type(screen.getByLabelText('Standaardnotitie'), 'Bedankt voor uw vertrouwen')
    await user.click(screen.getByRole('button', { name: 'Opslaan' }))

    await waitFor(() =>
      expect(api.savePeppolSettings).toHaveBeenCalledWith('le-1', {
        enabled: true,
        environment: 'Live',
        providerKey: 'sandbox',
        invoiceEmailFallback: 'facturen@transport.be',
        defaultInvoiceNote: 'Bedankt voor uw vertrouwen',
      }),
    )
  })

  it('disables the inputs without peppol.configure', async () => {
    renderTab({ canConfigure: false })

    expect(await screen.findByLabelText('Actief')).toBeDisabled()
    expect(screen.getByLabelText('Omgeving')).toBeDisabled()
    expect(screen.getByLabelText('Provider')).toBeDisabled()
    expect(screen.getByLabelText('E-mailterugval')).toBeDisabled()
    expect(screen.getByLabelText('Standaardnotitie')).toBeDisabled()
    expect(screen.queryByRole('button', { name: 'Opslaan' })).not.toBeInTheDocument()
  })

  it('shows the Gevonden state after a successful connection test', async () => {
    const user = userEvent.setup()
    api.testPeppolConnection.mockResolvedValue({
      found: true,
      supportedDocumentTypes: ['Invoice', 'CreditNote'],
      providerReference: 'ref-123',
      error: null,
    })
    renderTab()

    await user.click(await screen.findByRole('button', { name: 'Verbinding testen' }))

    expect(await screen.findByText('Gevonden')).toBeInTheDocument()
    expect(screen.getByText('ref-123')).toBeInTheDocument()
    expect(screen.getByText(/Invoice, CreditNote/)).toBeInTheDocument()
    expect(api.testPeppolConnection).toHaveBeenCalledWith('le-1')
  })

  it('shows the Niet gevonden state when the participant is unknown', async () => {
    const user = userEvent.setup()
    api.testPeppolConnection.mockResolvedValue({
      found: false,
      supportedDocumentTypes: [],
      providerReference: null,
      error: 'Deelnemer niet geregistreerd.',
    })
    renderTab()

    await user.click(await screen.findByRole('button', { name: 'Verbinding testen' }))

    expect(await screen.findByText('Niet gevonden')).toBeInTheDocument()
    expect(screen.getByText(/Deelnemer niet geregistreerd\./)).toBeInTheDocument()
  })
})
