import { describe, expect, it, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { ApiError } from '../../../../api/apiClient'
import { AccountingSettingsPage } from '../AccountingSettingsPage'
import type { LedgerAccount, SalesCategory } from '../../api/accountingApi'

const auth = vi.hoisted(() => ({ permissions: new Set<string>(['accounting.manage']) }))

vi.mock('../../../auth/authContextValue', () => ({
  useAuth: () => ({ hasPermission: (code: string) => auth.permissions.has(code) }),
}))
vi.mock('../../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }),
}))

const state = vi.hoisted(() => ({
  accounts: [] as LedgerAccount[],
  categories: [] as SalesCategory[],
  updateCategory: vi.fn(),
  setLedger: vi.fn(),
}))

vi.mock('../../api/accountingApi', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../api/accountingApi')>()),
  listLedgerAccounts: () => Promise.resolve(state.accounts),
  listSalesCategories: () => Promise.resolve(state.categories),
  updateSalesCategory: state.updateCategory,
  setSalesCategoryLedgerAccount: state.setLedger,
}))

function conflict() {
  return new ApiError('Deze gegevens zijn intussen gewijzigd door een andere gebruiker.', 409, {
    code: 'common.concurrency_conflict',
    message: 'Deze gegevens zijn intussen gewijzigd door een andere gebruiker.',
  })
}

function renderPage() {
  return render(
    <MemoryRouter>
      <AccountingSettingsPage />
    </MemoryRouter>,
  )
}

function account(overrides: Partial<LedgerAccount> = {}): LedgerAccount {
  return {
    id: 'acc-1', accountNumber: '700000', name: 'Transportopbrengsten',
    externalCode: null, description: null, isActive: true, ...overrides,
  }
}

function category(overrides: Partial<SalesCategory> = {}): SalesCategory {
  return {
    id: 'cat-1', code: 'TRANSPORT', name: 'Transport', systemRole: 'Transport',
    ledgerAccountId: null, ledgerAccountNumber: null, ledgerAccountName: null,
    isActive: true, sortOrder: 0, ...overrides,
  }
}

describe('AccountingSettingsPage', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    auth.permissions = new Set(['accounting.manage'])
    state.accounts = [account()]
    state.categories = [category(), category({ id: 'cat-2', code: 'DIESEL', name: 'Diesel', systemRole: 'Diesel' })]
    state.updateCategory.mockResolvedValue(category())
    state.setLedger.mockImplementation((id: string, ledgerAccountId: string | null) =>
      Promise.resolve({
        ...state.categories.find((c) => c.id === id)!,
        ledgerAccountId, ledgerAccountNumber: '700000', ledgerAccountName: 'Transportopbrengsten',
      }),
    )
  })

  it('warns for unmapped categories and assigns an account through the dedicated ledger endpoint (MD-H1)', async () => {
    const user = userEvent.setup()
    state.categories = [category(), category({ id: 'cat-2', code: 'DIESEL', name: 'Diesel', systemRole: 'Diesel', version: 7 })]
    renderPage()

    // Both categories are unmapped → one clear warning naming them.
    const warning = await screen.findByRole('alert')
    expect(warning.textContent).toContain("Geen grootboekrekening ingesteld voor 'Transport', 'Diesel'")

    await user.selectOptions(screen.getByLabelText('Grootboekrekening voor Diesel'), 'acc-1')
    await waitFor(() => expect(state.setLedger).toHaveBeenCalledWith('cat-2', 'acc-1', 7))
    // Never the full PUT: that one used to wipe descriptions, VAT overrides and entity mappings.
    expect(state.updateCategory).not.toHaveBeenCalled()
    // The returned DTO is applied to the row.
    await waitFor(() => expect(screen.getByLabelText('Grootboekrekening voor Diesel')).toHaveValue('acc-1'))
  })

  it('the full edit sends the version, keeps fields it does not edit and leaves untouched entity mappings alone', async () => {
    const user = userEvent.setup()
    state.categories = [
      category({
        version: 3, notes: 'Interne notitie', defaultUnitPrice: 12.5, defaultPricingBasis: 'PerTrip',
        ledgerMappings: [{ legalEntityId: 'le-1', ledgerAccountId: 'acc-1', costCentre: null }],
      }),
    ]
    renderPage()

    await user.click((await screen.findAllByRole('button', { name: 'Bewerken' }))[0])
    await user.type(screen.getByLabelText('Kostenplaats'), 'KP1')
    await user.click(screen.getByRole('button', { name: 'Opslaan' }))

    await waitFor(() => expect(state.updateCategory).toHaveBeenCalled())
    const [id, input] = state.updateCategory.mock.calls[0]
    expect(id).toBe('cat-1')
    expect(input).toMatchObject({
      costCentre: 'KP1',
      expectedVersion: 3,
      notes: 'Interne notitie',
      defaultUnitPrice: 12.5,
      defaultPricingBasis: 'PerTrip',
      // null = "unchanged" on the server: the dialog did not touch the mappings.
      ledgerMappings: null,
    })
  })

  it('on a 409 shows the conflict notice and reloads the record into the dialog (MD-M2)', async () => {
    const user = userEvent.setup()
    state.categories = [category({ version: 1, costCentre: null })]
    state.updateCategory.mockRejectedValueOnce(conflict())
    renderPage()

    await user.click((await screen.findAllByRole('button', { name: 'Bewerken' }))[0])
    await user.type(screen.getByLabelText('Kostenplaats'), 'KP1')
    await user.click(screen.getByRole('button', { name: 'Opslaan' }))

    expect(await screen.findByText('Deze gegevens zijn intussen gewijzigd door een andere gebruiker.')).toBeInTheDocument()

    // Someone else set a cost centre in the meantime: "Opnieuw laden" shows their version.
    state.categories = [category({ version: 2, costCentre: 'KP-ANDER' })]
    await user.click(screen.getByRole('button', { name: 'Opnieuw laden' }))

    await waitFor(() => expect(screen.getByLabelText('Kostenplaats')).toHaveValue('KP-ANDER'))
    expect(screen.queryByText('Deze gegevens zijn intussen gewijzigd door een andere gebruiker.')).not.toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Opslaan' }))
    await waitFor(() => expect(state.updateCategory).toHaveBeenLastCalledWith('cat-1', expect.objectContaining({ expectedVersion: 2 })))
  })

  it('asks before discarding a changed category draft on Annuleren and Escape', async () => {
    const user = userEvent.setup()
    renderPage()

    // An untouched dialog closes straight away.
    await user.click((await screen.findAllByRole('button', { name: 'Bewerken' }))[0])
    await user.click(screen.getByRole('button', { name: 'Annuleren' }))
    expect(screen.queryByText('Verkoopcategorie bewerken — Transport')).not.toBeInTheDocument()

    await user.click(screen.getAllByRole('button', { name: 'Bewerken' })[0])
    await user.type(screen.getByLabelText('Kostenplaats'), 'KP1')
    await user.keyboard('{Escape}')
    expect(await screen.findByText('Wijzigingen verwerpen?')).toBeInTheDocument()

    // "Blijven bewerken" keeps the draft.
    await user.click(screen.getByRole('button', { name: 'Blijven bewerken' }))
    expect(screen.getByLabelText('Kostenplaats')).toHaveValue('KP1')

    await user.click(screen.getByRole('button', { name: 'Annuleren' }))
    await user.click(await screen.findByRole('button', { name: 'Sluiten zonder opslaan' }))
    expect(screen.queryByText('Verkoopcategorie bewerken — Transport')).not.toBeInTheDocument()
    expect(state.updateCategory).not.toHaveBeenCalled()
  })

  it('shows mapped categories with a badge and read-only text without accounting.manage', async () => {
    auth.permissions = new Set(['accounting.view'])
    state.categories = [
      category({ ledgerAccountId: 'acc-1', ledgerAccountNumber: '700000', ledgerAccountName: 'Transportopbrengsten' }),
    ]
    render(
      <MemoryRouter>
        <AccountingSettingsPage />
      </MemoryRouter>,
    )

    expect(await screen.findByText('Gekoppeld')).toBeInTheDocument()
    expect(screen.getByText('700000 — Transportopbrengsten')).toBeInTheDocument()
    expect(screen.queryByLabelText('Grootboekrekening voor Transport')).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: '+ Grootboekrekening' })).not.toBeInTheDocument()
  })

  it('lets the operator set the fiscal classification, diesel-base participation and translations of a sales category', async () => {
    const user = userEvent.setup()
    state.categories = [
      category({
        ledgerAccountId: 'acc-1', ledgerAccountNumber: '700000', ledgerAccountName: 'Transportopbrengsten',
        invoiceDescriptionNl: 'Transport', invoiceDescriptionFr: null, includeInDieselBase: false, vatTreatmentOverride: null,
      }),
    ]
    render(
      <MemoryRouter>
        <AccountingSettingsPage />
      </MemoryRouter>,
    )

    await user.click((await screen.findAllByRole('button', { name: 'Bewerken' }))[0])
    expect(await screen.findByText('Verkoopcategorie bewerken — Transport')).toBeInTheDocument()

    await user.type(screen.getByLabelText('Factuuromschrijving (FR)'), 'Transport routier')
    await user.selectOptions(screen.getByLabelText('Afwijkende btw-behandeling'), 'ReverseCharge')
    await user.click(screen.getByLabelText('Meetellen in basis dieseltoeslag'))
    await user.click(screen.getByRole('button', { name: 'Opslaan' }))

    await waitFor(() =>
      expect(state.updateCategory).toHaveBeenCalledWith(
        'cat-1',
        expect.objectContaining({
          invoiceDescriptionFr: 'Transport routier',
          vatTreatmentOverride: 'ReverseCharge',
          includeInDieselBase: true,
        }),
      ),
    )
  })
})
