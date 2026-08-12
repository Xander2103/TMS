import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { OrderDocumentStrategyPanel } from '../OrderDocumentStrategyPanel'
import type { OrderDocumentStrategy } from '../../api/transportDocumentsApi'

const auth = vi.hoisted(() => ({ permissions: new Set<string>(['orders.view', 'orders.manage']) }))
vi.mock('../../../auth/authContextValue', () => ({
  useAuth: () => ({ hasPermission: (code: string) => auth.permissions.has(code) }),
}))

const toast = vi.hoisted(() => ({ showSuccess: vi.fn(), showError: vi.fn() }))
vi.mock('../../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: toast.showSuccess, showError: toast.showError }),
}))

const api = vi.hoisted(() => ({
  getStrategy: vi.fn(),
  setPreference: vi.fn(),
}))
vi.mock('../../api/transportDocumentsApi', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../api/transportDocumentsApi')>()),
  getOrderDocumentStrategy: api.getStrategy,
  setOrderDocumentPreference: api.setPreference,
}))

function strategy(overrides: Partial<OrderDocumentStrategy> = {}): OrderDocumentStrategy {
  return {
    kind: 'DeliveryNote',
    usesCustomerDocument: false,
    noneRequired: false,
    undecided: false,
    source: 'BuiltInDefault',
    reason: 'Standaard: binnenlandse opdracht zonder ADR krijgt een leveringsbon.',
    orderPreference: null,
    customerStrategy: 'GenerateOwn',
    ...overrides,
  }
}

describe('OrderDocumentStrategyPanel', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    auth.permissions = new Set(['orders.view', 'orders.manage'])
  })

  it('shows the resolver reason as explanation', async () => {
    api.getStrategy.mockResolvedValue(strategy())
    render(<OrderDocumentStrategyPanel orderId="o1" />)

    expect(
      await screen.findByText('Standaard: binnenlandse opdracht zonder ADR krijgt een leveringsbon.'),
    ).toBeInTheDocument()
    expect(api.getStrategy).toHaveBeenCalledWith('o1')
  })

  it('shows a "geen eigen document nodig" notice when the customer supplies the document', async () => {
    api.getStrategy.mockResolvedValue(
      strategy({ usesCustomerDocument: true, kind: null, reason: 'De klant levert het document aan.' }),
    )
    render(<OrderDocumentStrategyPanel orderId="o1" />)

    expect(await screen.findByText('Geen eigen document nodig: De klant levert het document aan.')).toBeInTheDocument()
  })

  it('saves an order preference and shows the refreshed strategy', async () => {
    api.getStrategy.mockResolvedValue(strategy())
    api.setPreference.mockResolvedValue(
      strategy({ kind: 'Cmr', orderPreference: 'Own', source: 'OrderOverride', reason: 'Orderkeuze: eigen document.' }),
    )
    const user = userEvent.setup()
    render(<OrderDocumentStrategyPanel orderId="o1" />)

    const select = await screen.findByLabelText('Documentkeuze voor deze opdracht')
    await user.selectOptions(select, 'Own')

    await waitFor(() => expect(api.setPreference).toHaveBeenCalledWith('o1', 'Own'))
    expect(await screen.findByText('Orderkeuze: eigen document.')).toBeInTheDocument()
    expect(toast.showSuccess).toHaveBeenCalled()
  })

  it('hides the preference select without orders.manage', async () => {
    auth.permissions = new Set(['orders.view'])
    api.getStrategy.mockResolvedValue(strategy())
    render(<OrderDocumentStrategyPanel orderId="o1" />)

    expect(
      await screen.findByText('Standaard: binnenlandse opdracht zonder ADR krijgt een leveringsbon.'),
    ).toBeInTheDocument()
    expect(screen.queryByLabelText('Documentkeuze voor deze opdracht')).not.toBeInTheDocument()
  })
})
