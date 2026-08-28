import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { TransportOrderDetailPage } from '../TransportOrderDetailPage'
import { orderDetail } from '../../../dossiers/__tests__/fixtures'

const auth = vi.hoisted(() => ({ permissions: new Set<string>(['orders.edit']) }))
vi.mock('../../../auth/authContextValue', () => ({
  useAuth: () => ({
    status: 'authenticated' as const,
    user: null,
    login: vi.fn(),
    logout: vi.fn(),
    hasPermission: (code: string) => auth.permissions.has(code),
    hasAnyPermission: (codes: string[]) => codes.some((c) => auth.permissions.has(c)),
  }),
}))
vi.mock('../../../../components/ui/toastContext', () => ({
  useToast: () => ({ showSuccess: vi.fn(), showError: vi.fn() }),
}))
vi.mock('../../../master-data/hooks/useLookupOptions', () => ({
  useLookupOptions: () => ({ options: [], isLoading: false, error: null }),
}))
vi.mock('../../components/OrderDocumentsPanel', () => ({ OrderDocumentsPanel: () => <div /> }))
vi.mock('../../components/OrderTimelinePanel', () => ({ OrderTimelinePanel: () => <div /> }))
vi.mock('../../components/StopExecutionPlanDialog', () => ({ StopExecutionPlanDialog: () => <div /> }))
vi.mock('../../../packages/components/OrderPackagesPanel', () => ({ OrderPackagesPanel: () => <div /> }))
vi.mock('../../../packages/components/CustomerPackagesSummary', () => ({ CustomerPackagesSummary: () => <div /> }))

const api = vi.hoisted(() => ({ getTransportOrder: vi.fn(), getLegalEntityOptions: vi.fn() }))
vi.mock('../../api/transportOrdersApi', async (orig) => ({
  ...(await orig<typeof import('../../api/transportOrdersApi')>()),
  getTransportOrder: api.getTransportOrder,
}))
vi.mock('../../../legal-entities/api/legalEntitiesApi', () => ({ getLegalEntityOptions: api.getLegalEntityOptions }))

function renderPage() {
  return render(
    <MemoryRouter initialEntries={['/transport-orders/order-1']}>
      <Routes>
        <Route path="/transport-orders/:id" element={<TransportOrderDetailPage />} />
      </Routes>
    </MemoryRouter>,
  )
}

beforeEach(() => {
  auth.permissions = new Set(['orders.edit'])
  api.getLegalEntityOptions.mockReset().mockResolvedValue([
    { id: 'ent-a', displayName: 'Entiteit A', vatNumber: null, isDefault: true, isActive: true },
  ])
  api.getTransportOrder.mockReset().mockResolvedValue(orderDetail({ id: 'order-1', orderNumber: 'ORD-1', customerName: 'Klant X', legalEntityId: null }))
})

describe('order commercial bar (sprint 6)', () => {
  it('shows the customer with a change action, and the invoicing entity (customer default when none is set)', async () => {
    renderPage()
    const bar = await screen.findByTestId('order-commercial-bar')
    expect(bar).toHaveTextContent('Klant: Klant X')
    expect(bar).toHaveTextContent('Facturerende entiteit: Klantstandaard')
    expect(screen.getByRole('button', { name: 'Klant van deze opdracht wijzigen' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Facturerende entiteit van deze opdracht wijzigen' })).toBeInTheDocument()
    expect(screen.queryByText('Tijdelijke klant')).not.toBeInTheDocument()
  })

  it('marks a temporary customer and resolves the entity name', async () => {
    api.getTransportOrder.mockResolvedValue(orderDetail({ id: 'order-1', orderNumber: 'ORD-1', customerName: 'VCB tijdelijk', legalEntityId: 'ent-a' }))
    renderPage()
    const bar = await screen.findByTestId('order-commercial-bar')
    expect(bar).toHaveTextContent('Tijdelijke klant')
    await screen.findByText('Entiteit A')
    expect(screen.getByText('Deze opdracht staat op een tijdelijke klant. Koppel de echte klant zodra die bekend is.')).toBeInTheDocument()
  })

  it('hides both change actions without orders.edit', async () => {
    auth.permissions = new Set<string>()
    renderPage()
    await screen.findByTestId('order-commercial-bar')
    expect(screen.queryByRole('button', { name: 'Klant van deze opdracht wijzigen' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Facturerende entiteit van deze opdracht wijzigen' })).not.toBeInTheDocument()
  })
})
