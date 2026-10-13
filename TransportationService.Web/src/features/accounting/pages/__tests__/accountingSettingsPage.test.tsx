import { describe, expect, it, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
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
}))

vi.mock('../../api/accountingApi', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../api/accountingApi')>()),
  listLedgerAccounts: () => Promise.resolve(state.accounts),
  listSalesCategories: () => Promise.resolve(state.categories),
  updateSalesCategory: state.updateCategory,
}))

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
  })

  it('warns for unmapped categories and assigns an account through the mapping select', async () => {
    const user = userEvent.setup()
    render(
      <MemoryRouter>
        <AccountingSettingsPage />
      </MemoryRouter>,
    )

    // Both categories are unmapped → one clear warning naming them.
    const warning = await screen.findByRole('alert')
    expect(warning.textContent).toContain("Geen grootboekrekening ingesteld voor 'Transport', 'Diesel'")

    await user.selectOptions(screen.getByLabelText('Grootboekrekening voor Diesel'), 'acc-1')
    await waitFor(() =>
      expect(state.updateCategory).toHaveBeenCalledWith('cat-2', expect.objectContaining({ ledgerAccountId: 'acc-1' })),
    )
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
