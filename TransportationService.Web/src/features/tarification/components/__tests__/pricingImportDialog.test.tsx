import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { PricingImportDialog } from '../PricingImportDialog'
import type { PricingImportCommitResult, PricingImportPreview } from '../../api/pricingImportApi'

const state = vi.hoisted(() => ({
  preview: vi.fn(),
  commit: vi.fn(),
  download: vi.fn(),
  profiles: [] as unknown[],
  history: [] as unknown[],
}))

vi.mock('../../api/pricingImportApi', () => ({
  previewPricingImport: (...args: unknown[]) => state.preview(...args),
  commitPricingImport: (...args: unknown[]) => state.commit(...args),
  downloadAgreementExport: (...args: unknown[]) => state.download(...args),
  // Sprint 4: the dialog also loads mapping profiles and this table's import history.
  listPricingImportProfiles: () => Promise.resolve(state.profiles),
  listPricingImportHistory: () => Promise.resolve(state.history),
}))

function makePreview(overrides: Partial<PricingImportPreview> = {}): PricingImportPreview {
  return {
    rowsFound: 4,
    rowsValid: 3,
    warnings: [],
    errors: [],
    alreadyImported: false,
    previousImportAt: null,
    previousImportFileName: null,
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
    state.profiles = []
    state.history = []
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

  it('reads the file through a saved mapping profile when one is chosen', async () => {
    state.profiles = [
      { id: 'prof-1', name: 'Atlas Copco 2026', notes: null, headerRow: 2, sheetName: 'Tarieven', mapping: {}, isActive: true },
    ]
    state.preview.mockResolvedValue(makePreview())

    render(<PricingImportDialog agreementId="agr-1" agreementName="Distributie België" onClose={vi.fn()} onImported={vi.fn()} />)
    const user = userEvent.setup()

    const profileSelect = await screen.findByLabelText('Mappingprofiel')
    await user.selectOptions(profileSelect, 'prof-1')
    await selectFile(screen.getByLabelText('Bestand (.xlsx)'))
    await user.click(screen.getByRole('button', { name: 'Voorbeeld' }))

    await waitFor(() => expect(state.preview).toHaveBeenCalled())
    // The mapping decides how the file is read, so it must reach the server.
    expect(state.preview).toHaveBeenCalledWith('agr-1', expect.anything(), 'prof-1')
  })

  it('warns when the exact same file was already imported into this table', async () => {
    state.preview.mockResolvedValue(
      makePreview({
        alreadyImported: true,
        previousImportAt: '2026-08-01T10:00:00Z',
        previousImportFileName: 'atlas-2026.xlsx',
      }),
    )

    render(<PricingImportDialog agreementId="agr-1" agreementName="Distributie België" onClose={vi.fn()} onImported={vi.fn()} />)
    const user = userEvent.setup()
    await selectFile(screen.getByLabelText('Bestand (.xlsx)'))
    await user.click(screen.getByRole('button', { name: 'Voorbeeld' }))

    expect(await screen.findByText(/al geïmporteerd/)).toHaveTextContent('atlas-2026.xlsx')
  })

  it('lists the import history for this table before a file is previewed', async () => {
    state.history = [
      {
        id: 'run-1', agreementId: 'agr-1', targetAgreementId: 'agr-1', fileName: 'atlas-2026.xlsx',
        checksum: 'abc', profileName: 'Atlas Copco 2026', mode: 'UpdateAgreement',
        rowsRead: 12, rowsValid: 12, created: 10, updated: 2, removed: 0, failed: 0,
        importedAt: '2026-08-01T10:00:00Z', importedByUserId: null,
      },
    ]

    render(<PricingImportDialog agreementId="agr-1" agreementName="Distributie België" onClose={vi.fn()} onImported={vi.fn()} />)

    const row = (await screen.findByText('atlas-2026.xlsx')).closest('tr')!
    expect(within(row).getByText('Atlas Copco 2026')).toBeInTheDocument()
    expect(within(row).getByText(/12 gelezen/)).toBeInTheDocument()
  })

  it('downloads the current table via the export link', async () => {
    state.download.mockResolvedValue(undefined)
    render(<PricingImportDialog agreementId="agr-1" agreementName="Distributie België" onClose={vi.fn()} onImported={vi.fn()} />)

    const user = userEvent.setup()
    await user.click(screen.getByRole('button', { name: 'Huidige tabel downloaden' }))

    await waitFor(() => expect(state.download).toHaveBeenCalledWith('agr-1', 'Distributie België'))
  })
})
