import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { PricingImportDialog } from '../PricingImportDialog'
import type { PricingImportCommitResult, PricingImportPreview } from '../../api/pricingImportApi'

const state = vi.hoisted(() => ({
  preview: vi.fn(),
  commit: vi.fn(),
  download: vi.fn(),
}))

vi.mock('../../api/pricingImportApi', () => ({
  previewPricingImport: (...args: unknown[]) => state.preview(...args),
  commitPricingImport: (...args: unknown[]) => state.commit(...args),
  downloadAgreementExport: (...args: unknown[]) => state.download(...args),
}))

function makePreview(overrides: Partial<PricingImportPreview> = {}): PricingImportPreview {
  return {
    rowsFound: 4,
    rowsValid: 3,
    warnings: [],
    errors: [],
    added: [],
    updated: [],
    removed: [],
    ...overrides,
  }
}

function makeCommitResult(overrides: Partial<PricingImportCommitResult> = {}): PricingImportCommitResult {
  return {
    agreementId: 'agr-1',
    agreement: {
      id: 'agr-1',
      customerId: null,
      customerName: null,
      name: 'Distributie België',
      currency: 'EUR',
      effectiveFrom: '2026-01-01',
      effectiveUntil: null,
      isActive: true,
      minimumAmount: null,
      notes: null,
      surcharges: [],
      isShared: false,
      maximumAmount: null,
      customerCount: 0,
      customerNames: null,
      baseAgreementId: null,
      baseAgreementName: null,
      modifiers: [],
      includedLoadingMinutes: null,
      includedUnloadingMinutes: null,
      includedCombinedMinutes: null,
      extraHourlyRate: null,
    },
    added: 1,
    updated: 2,
    removed: 0,
    ...overrides,
  }
}

function selectFile(input: HTMLElement) {
  const file = new File(['dummy'], 'tarieven.xlsx', {
    type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
  })
  return userEvent.setup().upload(input, file)
}

describe('PricingImportDialog', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('renders the counts banner and added/updated/removed badges after preview', async () => {
    state.preview.mockResolvedValue(
      makePreview({
        rowsFound: 143,
        rowsValid: 140,
        warnings: [{ row: 5, message: 'Dubbele rij: dezelfde staffel komt al voor bij deze regel.' }, { row: 9, message: 'Nog een waarschuwing.' }],
        errors: [{ row: 12, message: 'Basis \'Bogus\' is onbekend.' }],
        added: [{ name: 'Statiegeld', summary: 'Basis: Fixed; Eenheidsprijs: 25,00', ruleId: null, fieldChanges: null }],
        updated: [{ name: 'Afstand', summary: null, ruleId: 'rule-1', fieldChanges: ['Eenheidsprijs: 1,25 → 1,40'] }],
        removed: [],
      }),
    )

    render(<PricingImportDialog agreementId="agr-1" agreementName="Distributie België" onClose={vi.fn()} onImported={vi.fn()} />)

    const user = userEvent.setup()
    await selectFile(screen.getByLabelText('Bestand (.xlsx)'))
    await user.click(screen.getByRole('button', { name: 'Voorbeeld' }))

    const summary = await screen.findByText(/rijen gevonden/, { selector: 'p' })
    expect(summary).toHaveTextContent('143 rijen gevonden — 140 geldig, 2 waarschuwingen, 1 fout')

    expect(screen.getByText('Toevoegen: 1')).toBeInTheDocument()
    expect(screen.getByText('Wijzigen: 1')).toBeInTheDocument()
    expect(screen.getByText('Verwijderen: 0')).toBeInTheDocument()

    expect(screen.getByText("Basis 'Bogus' is onbekend.")).toBeInTheDocument()
    expect(screen.getByText('Eenheidsprijs: 1,25 → 1,40')).toBeInTheDocument()
    expect(screen.getByText('Basis: Fixed; Eenheidsprijs: 25,00')).toBeInTheDocument()

    // Errors block the commit button.
    expect(screen.getByRole('button', { name: 'Importeren' })).toBeDisabled()
  })

  it('commits with mode=UpdateAgreement and applyRemovals when checked', async () => {
    state.preview.mockResolvedValue(makePreview({ errors: [] }))
    state.commit.mockResolvedValue(makeCommitResult())
    const onImported = vi.fn()

    render(<PricingImportDialog agreementId="agr-1" agreementName="Distributie België" onClose={vi.fn()} onImported={onImported} />)

    const user = userEvent.setup()
    await selectFile(screen.getByLabelText('Bestand (.xlsx)'))
    await user.click(screen.getByRole('button', { name: 'Voorbeeld' }))
    await screen.findByText(/rijen gevonden/)

    await user.click(screen.getByLabelText(/Verwijderingen toepassen/))
    await user.click(screen.getByRole('button', { name: 'Importeren' }))

    await waitFor(() => expect(state.commit).toHaveBeenCalled())
    const [agreementId, , options] = state.commit.mock.calls[0]
    expect(agreementId).toBe('agr-1')
    expect(options).toEqual(
      expect.objectContaining({ mode: 'UpdateAgreement', applyRemovals: true, newName: null, newEffectiveFrom: null }),
    )
    expect(onImported).toHaveBeenCalledWith(expect.objectContaining({ agreementId: 'agr-1', added: 1, updated: 2, removed: 0 }))
  })

  it('commits with mode=DuplicateAsNewVersion and the new name/effective date', async () => {
    state.preview.mockResolvedValue(makePreview({ errors: [] }))
    state.commit.mockResolvedValue(makeCommitResult({ agreementId: 'agr-2' }))

    render(<PricingImportDialog agreementId="agr-1" agreementName="Distributie België" onClose={vi.fn()} onImported={vi.fn()} />)

    const user = userEvent.setup()
    await selectFile(screen.getByLabelText('Bestand (.xlsx)'))
    await user.click(screen.getByRole('button', { name: 'Voorbeeld' }))
    await screen.findByText(/rijen gevonden/)

    await user.click(screen.getByLabelText('Als nieuwe versie importeren'))
    const nameInput = screen.getByLabelText(/Naam nieuwe versie/)
    await user.clear(nameInput)
    await user.type(nameInput, 'Distributie België 2027')
    const dateInput = screen.getByLabelText(/Ingangsdatum/)
    await user.clear(dateInput)
    await user.type(dateInput, '2027-01-01')

    await user.click(screen.getByRole('button', { name: 'Importeren' }))

    await waitFor(() => expect(state.commit).toHaveBeenCalled())
    const [, , options] = state.commit.mock.calls[0]
    expect(options).toEqual(
      expect.objectContaining({
        mode: 'DuplicateAsNewVersion',
        newName: 'Distributie België 2027',
        newEffectiveFrom: '2027-01-01',
      }),
    )
  })

  it('downloads the current table via the export link', async () => {
    state.download.mockResolvedValue(undefined)
    render(<PricingImportDialog agreementId="agr-1" agreementName="Distributie België" onClose={vi.fn()} onImported={vi.fn()} />)

    const user = userEvent.setup()
    await user.click(screen.getByRole('button', { name: 'Huidige tabel downloaden' }))

    await waitFor(() => expect(state.download).toHaveBeenCalledWith('agr-1', 'Distributie België'))
  })
})
