import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { OrderLegalEntityDialog } from '../OrderLegalEntityDialog'
import type { OrderLegalEntityChangeImpact } from '../../api/transportOrdersApi'
import type { TransportOrderDetail } from '../../types'

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

const api = vi.hoisted(() => ({
  getCustomer: vi.fn(),
  getLegalEntityOptions: vi.fn(),
  getOrderLegalEntityChangeImpact: vi.fn(),
  changeOrderLegalEntity: vi.fn(),
}))
vi.mock('../../../customers/api/customersApi', async (orig) => ({
  ...(await orig<typeof import('../../../customers/api/customersApi')>()),
  getCustomer: api.getCustomer,
}))
vi.mock('../../../legal-entities/api/legalEntitiesApi', () => ({
  getLegalEntityOptions: api.getLegalEntityOptions,
}))
vi.mock('../../api/transportOrdersApi', async (orig) => ({
  ...(await orig<typeof import('../../api/transportOrdersApi')>()),
  getOrderLegalEntityChangeImpact: api.getOrderLegalEntityChangeImpact,
  changeOrderLegalEntity: api.changeOrderLegalEntity,
}))

const entity = (id: string, displayName: string, isDefault = false) => ({ id, displayName, vatNumber: null, isDefault, isActive: true })

const order = {
  id: 'order-1', orderNumber: 'ORD-1', customerId: 'cust-1', customerName: 'Klant X', legalEntityId: 'ent-a', version: 'v1',
} as unknown as TransportOrderDetail

function impact(overrides: Partial<OrderLegalEntityChangeImpact> = {}): OrderLegalEntityChangeImpact {
  return {
    orderId: 'order-1', currentLegalEntityId: 'ent-a', targetLegalEntityId: 'ent-b', customerDefaultLegalEntityId: 'ent-a',
    deviatesFromCustomerDefault: true, requiresOverridePermission: false, blockedReason: null, draftInvoiceLinesReleased: 0,
    ...overrides,
  }
}

beforeEach(() => {
  auth.permissions = new Set(['orders.edit', 'dossiers.override_entity'])
  api.getLegalEntityOptions.mockReset().mockResolvedValue([entity('ent-a', 'Entiteit A', true), entity('ent-b', 'Entiteit B'), entity('ent-c', 'Entiteit C')])
  api.getCustomer.mockReset().mockResolvedValue({ id: 'cust-1', defaultLegalEntityId: 'ent-a', allowedLegalEntityIds: ['ent-a', 'ent-b'] })
  api.getOrderLegalEntityChangeImpact.mockReset().mockResolvedValue(impact())
  api.changeOrderLegalEntity.mockReset().mockResolvedValue({ ...order, legalEntityId: 'ent-b' })
})

describe('OrderLegalEntityDialog', () => {
  it('offers only the allowed entities, marks the customer default, and requires a reason to deviate', async () => {
    const onChanged = vi.fn()
    render(<OrderLegalEntityDialog order={order} onClose={vi.fn()} onChanged={onChanged} />)

    await screen.findByRole('option', { name: 'Entiteit A (klantstandaard)' })
    expect(screen.getByRole('option', { name: 'Entiteit B' })).toBeInTheDocument()
    expect(screen.queryByRole('option', { name: /Entiteit C/ })).not.toBeInTheDocument()

    await userEvent.selectOptions(screen.getByRole('combobox', { name: /Facturerende entiteit/ }), 'ent-b')
    await screen.findByText('Deze entiteit wijkt af van de klantstandaard: een reden is verplicht en de wijziging wordt vastgelegd.')
    const confirm = screen.getByRole('button', { name: 'Entiteit wijzigen' })
    expect(confirm).toBeDisabled()

    await userEvent.type(screen.getByLabelText(/Reden/), 'Klant factureert via B')
    expect(confirm).toBeEnabled()
    await userEvent.click(confirm)

    await waitFor(() => expect(api.changeOrderLegalEntity).toHaveBeenCalledWith('order-1', 'ent-b', 'Klant factureert via B', 'v1'))
    expect(onChanged).toHaveBeenCalledWith(expect.objectContaining({ legalEntityId: 'ent-b' }))
  })

  it('without the override right a deviating entity is explained and cannot be applied', async () => {
    auth.permissions = new Set(['orders.edit'])
    api.getOrderLegalEntityChangeImpact.mockResolvedValue(impact({ requiresOverridePermission: true }))
    render(<OrderLegalEntityDialog order={order} onClose={vi.fn()} onChanged={vi.fn()} />)

    await screen.findByRole('option', { name: 'Entiteit B' })
    await userEvent.selectOptions(screen.getByRole('combobox', { name: /Facturerende entiteit/ }), 'ent-b')

    await screen.findByRole('alert')
    expect(screen.getByRole('alert')).toHaveTextContent('Je hebt geen rechten om af te wijken van de klantstandaard.')
    expect(screen.queryByLabelText(/Reden/)).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Entiteit wijzigen' })).toBeDisabled()
  })

  it('shows the backend block (sent invoice) and the draft-line impact', async () => {
    api.getOrderLegalEntityChangeImpact.mockResolvedValue(impact({ blockedReason: 'Deze opdracht staat op een verzonden factuur.' }))
    render(<OrderLegalEntityDialog order={order} onClose={vi.fn()} onChanged={vi.fn()} />)
    await screen.findByRole('option', { name: 'Entiteit B' })
    await userEvent.selectOptions(screen.getByRole('combobox', { name: /Facturerende entiteit/ }), 'ent-b')

    await screen.findByText('Deze opdracht staat op een verzonden factuur.')
    expect(screen.getByRole('button', { name: 'Entiteit wijzigen' })).toBeDisabled()
  })
})
