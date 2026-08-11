import { describe, expect, it, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { NewDossierPage } from '../pages/NewDossierPage'
import { dossierDetail } from './fixtures'

const navigateSpy = vi.hoisted(() => vi.fn())
vi.mock('react-router-dom', async (importOriginal) => ({
  ...(await importOriginal<typeof import('react-router-dom')>()),
  useNavigate: () => navigateSpy,
}))

vi.mock('../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }),
}))

vi.mock('../../customers/api/customersApi', () => ({
  searchCustomers: () => Promise.resolve({ items: [{ id: 'c-1', name: 'Nexans NV' }], totalCount: 1 }),
}))

vi.mock('../api/activityTypesApi', () => ({
  listActivityTypes: () =>
    Promise.resolve([
      {
        id: 'at-1', code: 'DIRECT_TRANSPORT', name: 'Direct transport', isActive: true, sortOrder: 2,
        icon: 'truck', kpiCategory: null, hasStops: true, supportsGoods: true, planningRelevant: true,
        warehouseRelevant: false, allowsDuration: false, isQuickStart: true, quickStartOrder: 2,
        isSystemDefaultTransport: true,
      },
      {
        id: 'at-2', code: 'KRAANWERK', name: 'Kraanwerk ter plaatse', isActive: true, sortOrder: 4,
        icon: 'crane', kpiCategory: null, hasStops: false, supportsGoods: false, planningRelevant: true,
        warehouseRelevant: false, allowsDuration: true, isQuickStart: true, quickStartOrder: 1,
        isSystemDefaultTransport: false,
      },
      {
        id: 'at-3', code: 'OVERIG', name: 'Overig', isActive: true, sortOrder: 9, icon: null,
        kpiCategory: null, hasStops: false, supportsGoods: false, planningRelevant: false,
        warehouseRelevant: false, allowsDuration: true, isQuickStart: false, quickStartOrder: 0,
        isSystemDefaultTransport: false,
      },
    ]),
}))

const createFast = vi.hoisted(() => vi.fn())
vi.mock('../api/dossiersApi', () => ({
  createDossierFast: createFast,
}))

async function pickCustomer(user: ReturnType<typeof userEvent.setup>) {
  await user.click(screen.getByRole('combobox'))
  await user.click(await screen.findByText('Nexans NV'))
}

describe('NewDossierPage', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    createFast.mockResolvedValue(dossierDetail({ id: 'd-9', dossierNumber: 'DOS-0009' }))
  })

  it('renders exactly klant + referentie + datum + template tiles — nothing else', async () => {
    render(
      <MemoryRouter>
        <NewDossierPage />
      </MemoryRouter>,
    )

    expect(screen.getByRole('combobox')).toBeInTheDocument() // Klant (SearchableSelect)
    expect(screen.getByLabelText('Klantreferentie')).toBeInTheDocument()
    const datum = screen.getByLabelText('Datum')
    expect(datum).toHaveValue(new Date().toISOString().slice(0, 10))

    // Quick-start tiles sorted on quickStartOrder, plus the always-present empty tile.
    expect(await screen.findByRole('radio', { name: /Kraanwerk ter plaatse/ })).toBeInTheDocument()
    expect(screen.getByRole('radio', { name: /Direct transport/ })).toBeInTheDocument()
    expect(screen.getByRole('radio', { name: /Leeg dossier/ })).toHaveAttribute('aria-checked', 'true')
    // Non-quick-start types never become tiles.
    expect(screen.queryByRole('radio', { name: /Overig/ })).not.toBeInTheDocument()

    // §8: no goods/route/price inputs at create — creation never asks for them.
    expect(screen.queryByLabelText(/adres/i)).not.toBeInTheDocument()
    expect(screen.queryByLabelText(/goederen/i)).not.toBeInTheDocument()
    expect(screen.queryByLabelText(/gewicht/i)).not.toBeInTheDocument()
    expect(screen.queryByLabelText(/prijs/i)).not.toBeInTheDocument()
    // Exactly one primary action.
    expect(screen.getByRole('button', { name: 'Dossier aanmaken' })).toBeInTheDocument()
  })

  it('submits with customer only and navigates to the new dossier', async () => {
    const user = userEvent.setup()
    render(
      <MemoryRouter>
        <NewDossierPage />
      </MemoryRouter>,
    )

    await pickCustomer(user)
    await user.click(screen.getByRole('button', { name: 'Dossier aanmaken' }))

    await waitFor(() =>
      expect(createFast).toHaveBeenCalledWith(
        expect.objectContaining({ customerId: 'c-1', activityTypeId: null }),
      ),
    )
    expect(navigateSpy).toHaveBeenCalledWith('/dossiers/d-9')
  })

  it('passes the selected template tile as activityTypeId', async () => {
    const user = userEvent.setup()
    render(
      <MemoryRouter>
        <NewDossierPage />
      </MemoryRouter>,
    )

    await pickCustomer(user)
    await user.click(await screen.findByRole('radio', { name: /Kraanwerk ter plaatse/ }))
    await user.click(screen.getByRole('button', { name: 'Dossier aanmaken' }))

    await waitFor(() =>
      expect(createFast).toHaveBeenCalledWith(expect.objectContaining({ customerId: 'c-1', activityTypeId: 'at-2' })),
    )
  })
})
