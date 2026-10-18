import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { ApiError } from '../../../api/apiClient'
import { LegalEntitiesPage } from '../pages/LegalEntitiesPage'
import * as legalEntitiesApi from '../api/legalEntitiesApi'
import * as customersApi from '../../customers/api/customersApi'
import type { LegalEntity } from '../types'

vi.mock('../../auth/authContextValue', () => ({
  useAuth: () => ({
    status: 'authenticated' as const,
    user: null,
    login: vi.fn(),
    logout: vi.fn(),
    hasPermission: (code: string) => code === 'legal_entities.manage',
    hasAnyPermission: (codes: string[]) => codes.includes('legal_entities.manage'),
  }),
}))
vi.mock('../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }),
}))

function entity(overrides: Partial<LegalEntity> = {}): LegalEntity {
  return {
    id: 'le-1',
    legalName: 'Acme BV',
    tradingName: null,
    companyNumber: null,
    vatNumber: 'BE0123456749',
    peppolId: null,
    peppolScheme: null,
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
    version: 4,
    ...overrides,
  }
}

const CONFLICT = 'Deze gegevens zijn intussen gewijzigd door een andere gebruiker.'

function renderPage() {
  return render(
    <MemoryRouter>
      <LegalEntitiesPage />
    </MemoryRouter>,
  )
}

async function openEditor(user: ReturnType<typeof userEvent.setup>) {
  await user.click(await screen.findByRole('button', { name: 'Bewerken' }))
}

let calls: string[] = []

beforeEach(() => {
  vi.restoreAllMocks()
  calls = []
  vi.spyOn(legalEntitiesApi, 'listLegalEntities').mockResolvedValue([entity()])
  vi.spyOn(customersApi, 'getPeppolSchemes').mockResolvedValue([])
  vi.spyOn(legalEntitiesApi, 'updateLegalEntity').mockImplementation(async () => {
    calls.push('update')
    return entity({ version: 5 })
  })
  vi.spyOn(legalEntitiesApi, 'uploadLegalEntityLogo').mockImplementation(async () => {
    calls.push('upload')
    return entity({ version: 5, hasLogo: true, logoFileName: 'logo.png' })
  })
  vi.spyOn(legalEntitiesApi, 'deleteLegalEntityLogo').mockImplementation(async () => {
    calls.push('delete')
    return entity({ version: 5, hasLogo: false })
  })
})

describe('LegalEntitiesPage — edit dialog (MD-M2 / MD-M10)', () => {
  it('stages a chosen logo and uploads it only after Opslaan stored the entity', async () => {
    const user = userEvent.setup()
    renderPage()
    await openEditor(user)

    const file = new File(['png'], 'nieuw-logo.png', { type: 'image/png' })
    await user.upload(screen.getByLabelText('Logo uploaden'), file)

    expect(legalEntitiesApi.uploadLegalEntityLogo).not.toHaveBeenCalled()
    expect(screen.getByText('nieuw-logo.png')).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Opslaan' }))

    await waitFor(() => expect(calls).toEqual(['update', 'upload']))
    expect(legalEntitiesApi.updateLegalEntity).toHaveBeenCalledWith('le-1', expect.objectContaining({ expectedVersion: 4 }))
    expect(legalEntitiesApi.uploadLegalEntityLogo).toHaveBeenCalledWith('le-1', file)
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  })

  it('applies "Logo verwijderen" on save, and Annuleren on a staged change asks first', async () => {
    vi.mocked(legalEntitiesApi.listLegalEntities).mockResolvedValue([entity({ hasLogo: true, logoFileName: 'oud.png' })])
    vi.mocked(legalEntitiesApi.updateLegalEntity).mockImplementation(async () => {
      calls.push('update')
      return entity({ version: 5, hasLogo: true, logoFileName: 'oud.png' })
    })
    const user = userEvent.setup()
    renderPage()
    await openEditor(user)

    await user.click(screen.getByRole('button', { name: 'Logo verwijderen' }))
    expect(legalEntitiesApi.deleteLegalEntityLogo).not.toHaveBeenCalled()
    expect(screen.getByText('Het logo wordt verwijderd wanneer je op Opslaan klikt.')).toBeInTheDocument()

    // A staged logo removal is an unsaved change.
    await user.click(screen.getByRole('button', { name: 'Annuleren' }))
    expect(await screen.findByText('Wijzigingen verwerpen?')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Blijven bewerken' }))

    await user.click(screen.getByRole('button', { name: 'Opslaan' }))
    await waitFor(() => expect(calls).toEqual(['update', 'delete']))
  })

  it('shows server field errors (IBAN) next to the field and keeps the dialog open', async () => {
    vi.mocked(legalEntitiesApi.updateLegalEntity).mockRejectedValue(
      new ApiError('Het IBAN is ongeldig.', 400, {
        code: 'common.validation_failed',
        errors: { Iban: ['Het IBAN is ongeldig (controlegetal klopt niet).'] },
      }),
    )
    const user = userEvent.setup()
    renderPage()
    await openEditor(user)

    await user.type(screen.getByLabelText('IBAN'), 'BE00 0000 0000 0000')
    await user.click(screen.getByRole('button', { name: 'Opslaan' }))

    expect(await screen.findByText('Het IBAN is ongeldig (controlegetal klopt niet).')).toBeInTheDocument()
    expect(screen.getByLabelText('IBAN')).toHaveValue('BE00 0000 0000 0000')
  })

  it('on a 409 offers to reload the record and then saves against the new version', async () => {
    vi.mocked(legalEntitiesApi.updateLegalEntity).mockRejectedValueOnce(
      new ApiError(CONFLICT, 409, { code: 'common.concurrency_conflict', message: CONFLICT }),
    )
    vi.spyOn(legalEntitiesApi, 'getLegalEntity').mockResolvedValue(entity({ version: 9, iban: 'BE68539007547034' }))
    const user = userEvent.setup()
    renderPage()
    await openEditor(user)

    await user.type(screen.getByLabelText('BIC'), 'GEBABEBB')
    await user.click(screen.getByRole('button', { name: 'Opslaan' }))
    expect(await screen.findByText(CONFLICT)).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Opnieuw laden' }))
    await waitFor(() => expect(screen.getByLabelText('IBAN')).toHaveValue('BE68539007547034'))
    expect(screen.queryByText(CONFLICT)).not.toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Opslaan' }))
    await waitFor(() =>
      expect(legalEntitiesApi.updateLegalEntity).toHaveBeenLastCalledWith('le-1', expect.objectContaining({ expectedVersion: 9 })),
    )
  })

  it('Escape on a changed draft asks before discarding; an untouched draft closes at once', async () => {
    const user = userEvent.setup()
    renderPage()
    await openEditor(user)
    await user.keyboard('{Escape}')
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()

    await openEditor(user)
    await user.type(screen.getByLabelText('Handelsnaam'), 'Acme')
    await user.keyboard('{Escape}')
    expect(await screen.findByText('Wijzigingen verwerpen?')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Sluiten zonder opslaan' }))
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(legalEntitiesApi.updateLegalEntity).not.toHaveBeenCalled()
  })
})
