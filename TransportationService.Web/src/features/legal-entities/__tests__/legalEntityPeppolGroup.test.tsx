import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { LegalEntitiesPage } from '../pages/LegalEntitiesPage'
import * as legalEntitiesApi from '../api/legalEntitiesApi'
import * as customersApi from '../../customers/api/customersApi'
import type { LegalEntity } from '../types'

const auth = vi.hoisted(() => ({ permissions: ['legal_entities.manage'] }))

vi.mock('../../auth/authContextValue', () => ({
  useAuth: () => ({
    status: 'authenticated' as const,
    user: null,
    login: vi.fn(),
    logout: vi.fn(),
    hasPermission: (code: string) => auth.permissions.includes(code),
    hasAnyPermission: (codes: string[]) => codes.some((code) => auth.permissions.includes(code)),
  }),
}))
vi.mock('../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }),
}))

function entity(): LegalEntity {
  return {
    id: 'le-1',
    legalName: 'Acme BV',
    tradingName: null,
    companyNumber: null,
    vatNumber: 'BE0123456749',
    peppolId: '0123456789',
    peppolScheme: '0208',
    street: null,
    houseNumber: null,
    postalCode: null,
    city: null,
    countryCode: 'BE',
    email: null,
    phoneNumber: null,
    website: null,
    iban: null,
    bic: null,
    bankName: null,
    defaultCurrency: 'EUR',
    paymentTermDays: 30,
    invoiceNumberFormat: '{YYYY}{MM}{SEQ}',
    invoiceSequencePadding: 4,
    invoicePrefix: null,
    creditNotePrefix: null,
    invoiceFooter: null,
    hasLogo: false,
    logoFileName: null,
    isActive: true,
    isDefault: true,
  }
}

beforeEach(() => {
  auth.permissions = ['legal_entities.manage']
  vi.spyOn(legalEntitiesApi, 'listLegalEntities').mockResolvedValue([entity()])
  vi.spyOn(customersApi, 'getPeppolSchemes').mockResolvedValue([
    { code: '0208', label: 'Belgisch ondernemingsnummer', countryCode: 'BE' },
  ])
})

describe('LegalEntitiesPage — grouped Peppol control', () => {
  it('renders the shared PeppolFieldGroup with the combined Peppol-ID in the editor', async () => {
    render(
      <MemoryRouter>
        <LegalEntitiesPage />
      </MemoryRouter>,
    )

    await userEvent.click(await screen.findByRole('button', { name: 'Bewerken' }))

    expect(screen.getByRole('group', { name: 'Peppol' })).toBeInTheDocument()
    expect(screen.getByLabelText(/Peppol-ID/i)).toHaveValue('0208:0123456789')
    // The two raw inputs are replaced: the raw schema field only exists behind "Geavanceerd".
    expect(screen.queryByLabelText('Peppol-schema')).not.toBeInTheDocument()
  })
})
