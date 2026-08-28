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
  listPricingImportFields: () => Promise.resolve([]),
  readPricingImportHeaders: vi.fn(),
  createPricingImportProfile: vi.fn(),
  updatePricingImportProfile: vi.fn(),
  deletePricingImportProfile: vi.fn(),
}))
const auth = vi.hoisted(() => ({ permissions: new Set<string>(['tariffs.import', 'tariffs.manage']) }))
vi.mock('../../../auth/authContextValue', () => ({
  useAuth: () => ({ hasPermission: (code: string) => auth.permissions.has(code) }),
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
    auth.permissions = new Set(['tariffs.import', 'tariffs.manage'])
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
    // Removals are only presented once 'Verwijderingen toepassen' is on.
    expect(screen.queryByText('Verwijderen: 0')).not.toBeInTheDocument()

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
        importedAt: '2026-08-01T10:00:00Z', importedByUserId: null, status: 'Succeeded', error: null,
      },
      {
        id: 'run-2', agreementId: 'agr-1', targetAgreementId: 'agr-1', fileName: 'atlas-kapot.xlsx',
        checksum: 'def', profileName: null, mode: 'UpdateAgreement',
        rowsRead: 3, rowsValid: 2, created: 0, updated: 0, removed: 0, failed: 1,
        importedAt: '2026-08-02T10:00:00Z', importedByUserId: null, status: 'Rejected',
        error: "Import geblokkeerd door fouten: Rij 4: Basis 'Bogus' is onbekend.",
      },
    ]

    render(<PricingImportDialog agreementId="agr-1" agreementName="Distributie België" onClose={vi.fn()} onImported={vi.fn()} />)

    const row = (await screen.findByText('atlas-2026.xlsx')).closest('tr')!
    expect(within(row).getByText('Atlas Copco 2026')).toBeInTheDocument()
    expect(within(row).getByText('Geslaagd')).toBeInTheDocument()
    expect(within(row).getByText(/12 gelezen/)).toBeInTheDocument()

    // A rejected attempt is in the history too — with its status and reason, not fake counts.
    const rejected = screen.getByText('atlas-kapot.xlsx').closest('tr')!
    expect(within(rejected).getByText('Geweigerd')).toBeInTheDocument()
    expect(within(rejected).getByText(/Bogus/)).toBeInTheDocument()
    expect(within(rejected).queryByText(/gelezen/)).not.toBeInTheDocument()
  })

  it('explains name-matched rows and partial columns on the preview', async () => {
    state.preview.mockResolvedValue(
      makePreview({
        errors: [],
        updated: [{ name: 'Rit', summary: null, ruleId: 'rule-1', fieldChanges: ['Eenheidsprijs: 1,25 → 1,40'] }],
        matchedByNameCount: 2,
        presentFields: ['naam', 'basis', 'eenheidsprijs'],
      }),
    )
    render(<PricingImportDialog agreementId="agr-1" agreementName="Distributie België" onClose={vi.fn()} onImported={vi.fn()} />)
    const user = userEvent.setup()
    await selectFile(screen.getByLabelText('Bestand (.xlsx)'))
    await user.click(screen.getByRole('button', { name: 'Voorbeeld' }))

    expect(await screen.findByText(/2 rij\(en\) zonder RegelId/)).toBeInTheDocument()
    expect(screen.getByText(/alleen de aanwezige velden worden bijgewerkt/)).toBeInTheDocument()
  })

  it('only offers profile management with tariffs.manage; import-only users still pick a profile', async () => {
    state.profiles = [{ id: 'p1', name: 'Atlas Copco 2026', notes: null, headerRow: 2, sheetName: null, mapping: {}, isActive: true }]
    auth.permissions = new Set(['tariffs.import'])

    render(<PricingImportDialog agreementId="agr-1" agreementName="Distributie België" onClose={vi.fn()} onImported={vi.fn()} />)

    expect(await screen.findByRole('option', { name: 'Atlas Copco 2026' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Kolomtoewijzing beheren' })).not.toBeInTheDocument()
  })

  it('offers profile management with tariffs.manage', async () => {
    render(<PricingImportDialog agreementId="agr-1" agreementName="Distributie België" onClose={vi.fn()} onImported={vi.fn()} />)

    const user = userEvent.setup()
    await user.click(screen.getByRole('button', { name: 'Kolomtoewijzing beheren' }))
    expect(await screen.findByTestId('pricing-import-profile-panel')).toBeInTheDocument()
  })

  it('downloads the current table via the export link', async () => {
    state.download.mockResolvedValue(undefined)
    render(<PricingImportDialog agreementId="agr-1" agreementName="Distributie België" onClose={vi.fn()} onImported={vi.fn()} />)

    const user = userEvent.setup()
    await user.click(screen.getByRole('button', { name: /Huidige tabel downloaden/ }))

    await waitFor(() => expect(state.download).toHaveBeenCalledWith('agr-1', 'Distributie België'))
  })
})

describe('PricingImportDialog — verwijderingen', () => {
  it('does not present rows missing from the file as removals while "Verwijderingen toepassen" is off', async () => {
    state.preview.mockResolvedValue(
      makePreview({
        rowsFound: 1,
        rowsValid: 1,
        added: [],
        updated: [],
        removed: [{ name: 'Palletstaffel', summary: null, ruleId: 'rule-9', fieldChanges: null }],
      }),
    )
    render(<PricingImportDialog agreementId="agr-1" agreementName="Distributie België" onClose={vi.fn()} onImported={vi.fn()} />)
    const user = userEvent.setup()
    await selectFile(screen.getByLabelText('Bestand (.xlsx)'))
    await user.click(screen.getByRole('button', { name: 'Voorbeeld' }))

    expect(await screen.findByTestId('pricing-import-removals-skipped')).toHaveTextContent(
      "1 regel(s) uit deze tabel staan niet in het bestand. Ze blijven staan omdat 'Verwijderingen toepassen' uit staat.",
    )
    expect(screen.queryByText('Verwijderen: 1')).not.toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'Verwijderen' })).not.toBeInTheDocument()

    await user.click(screen.getByLabelText(/Verwijderingen toepassen/))
    expect(await screen.findByText('Verwijderen: 1')).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Verwijderen' })).toBeInTheDocument()
    expect(screen.getByText('Palletstaffel')).toBeInTheDocument()
    expect(screen.queryByTestId('pricing-import-removals-skipped')).not.toBeInTheDocument()
  })
})
