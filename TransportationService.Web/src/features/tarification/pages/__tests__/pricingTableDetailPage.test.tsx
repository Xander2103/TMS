import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { PricingTableDetailPage } from '../PricingTableDetailPage'
import type { PricingAgreement } from '../../api/pricingApi'

const auth = vi.hoisted(() => ({ permissions: new Set<string>() }))

vi.mock('../../../auth/authContextValue', () => ({
  useAuth: () => ({ hasPermission: (code: string) => auth.permissions.has(code) }),
}))
vi.mock('../../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }),
}))

// Every sub-panel is stubbed — this test only exercises the header actions' permission gating.
vi.mock('../../components/RuleGridEditor', () => ({ RuleGridEditor: () => <div>rules</div> }))
vi.mock('../../components/AgreementAssignmentsPanel', () => ({ AgreementAssignmentsPanel: () => <div>assignments</div> }))
vi.mock('../../components/AgreementDerivationPanel', () => ({ AgreementDerivationPanel: () => <div>derivation</div> }))
vi.mock('../../components/AgreementSurchargesPanel', () => ({ AgreementSurchargesPanel: () => <div>surcharges</div> }))
vi.mock('../../components/AgreementVersionsPanel', () => ({ AgreementVersionsPanel: () => <div>versions</div> }))
vi.mock('../../components/CombinedDiscountsPanel', () => ({ CombinedDiscountsPanel: () => <div>discounts</div> }))
vi.mock('../../components/AgreementAdjustmentsPanel', () => ({ AgreementAdjustmentsPanel: () => <div>adjustments</div> }))
vi.mock('../../components/PricingImportDialog', () => ({ PricingImportDialog: () => <div role="dialog">import dialog</div> }))

const state = vi.hoisted(() => ({ agreement: null as PricingAgreement | null }))

vi.mock('../../api/pricingApi', async (importOriginal) => {
  const original = await importOriginal<typeof import('../../api/pricingApi')>()
  return { ...original, getPricingAgreement: () => Promise.resolve(state.agreement) }
})

function makeAgreement(overrides: Partial<PricingAgreement> = {}): PricingAgreement {
  return {
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
    ...overrides,
  }
}

function renderPage(initialPath = '/pricing/tables/agr-1') {
  return render(
    <MemoryRouter initialEntries={[initialPath]}>
      <Routes>
        <Route path="/pricing/tables/:id" element={<PricingTableDetailPage />} />
      </Routes>
    </MemoryRouter>,
  )
}

describe('PricingTableDetailPage — export/import header actions', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    state.agreement = makeAgreement()
  })

  it('shows Exporteren with view-only rights, hides Importeren', async () => {
    auth.permissions = new Set(['tariffs.view'])
    renderPage()

    expect(await screen.findByRole('button', { name: 'Exporteren' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Importeren' })).not.toBeInTheDocument()
  })

  it('shows Importeren with tariffs.import even without tariffs.manage', async () => {
    auth.permissions = new Set(['tariffs.view', 'tariffs.import'])
    renderPage()

    expect(await screen.findByRole('button', { name: 'Exporteren' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Importeren' })).toBeInTheDocument()
  })

  it('shows Importeren with tariffs.manage (which implies import rights)', async () => {
    auth.permissions = new Set(['tariffs.view', 'tariffs.manage'])
    renderPage()

    expect(await screen.findByRole('button', { name: 'Importeren' })).toBeInTheDocument()
  })

  it('auto-opens the import dialog when landing with ?import=1 and import rights', async () => {
    auth.permissions = new Set(['tariffs.view', 'tariffs.import'])
    renderPage('/pricing/tables/agr-1?import=1')

    expect(await screen.findByRole('dialog')).toBeInTheDocument()
  })

  it('does NOT auto-open the import dialog with ?import=1 when lacking import rights', async () => {
    auth.permissions = new Set(['tariffs.view'])
    renderPage('/pricing/tables/agr-1?import=1')

    await screen.findByRole('button', { name: 'Exporteren' })
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('blocks the whole page without tariffs.view or tariffs.manage', () => {
    auth.permissions = new Set<string>()
    renderPage()

    expect(screen.getByText('Je hebt geen rechten om tarieventabellen te bekijken.')).toBeInTheDocument()
  })
})
