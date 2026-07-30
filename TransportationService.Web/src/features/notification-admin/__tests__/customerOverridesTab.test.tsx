import { describe, expect, it, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { CustomerOverridesTab } from '../components/CustomerOverridesTab'
import type { CustomerNotificationOverride } from '../types'

vi.mock('../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }),
}))

vi.mock('../../customers/api/customersApi', () => ({
  searchCustomers: vi.fn().mockResolvedValue({
    items: [{ id: 'cust-1', customerNumber: 'KL-1', name: 'Haven BV', city: null, countryCode: null, categoryName: null, isActive: true, isBlocked: false }],
    totalCount: 1,
    page: 1,
    pageSize: 500,
  }),
}))

const api = vi.hoisted(() => ({
  listCustomerNotificationOverrides: vi.fn(),
  setCustomerNotificationOverride: vi.fn(),
}))
vi.mock('../api/notificationAdminApi', () => api)

function row(overrides: Partial<CustomerNotificationOverride> = {}): CustomerNotificationOverride {
  return { eventKey: 'order_accepted', label: 'Opdracht geaccepteerd', allowCustomerOverride: true, enabled: null, ...overrides }
}

async function selectCustomer(user: ReturnType<typeof userEvent.setup>) {
  const combobox = await screen.findByRole('combobox', { name: 'Klant' })
  await user.click(combobox)
  await user.click(await screen.findByRole('option', { name: 'KL-1 — Haven BV' }))
}

describe('CustomerOverridesTab', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    api.setCustomerNotificationOverride.mockResolvedValue(undefined)
  })

  it('shows "Overgenomen van standaard" for inherited events and "Afwijkend" for overridden ones', async () => {
    const user = userEvent.setup()
    api.listCustomerNotificationOverrides.mockResolvedValue([
      row({ eventKey: 'order_accepted', label: 'Opdracht geaccepteerd', enabled: null }),
      row({ eventKey: 'order_rejected', label: 'Opdracht geweigerd', enabled: false }),
    ])

    render(<CustomerOverridesTab canManage />)
    await selectCustomer(user)

    expect(await screen.findByText('Overgenomen van standaard')).toBeInTheDocument()
    expect(screen.getByText('Afwijkend (uit)')).toBeInTheDocument()
  })

  it('sets a customer override via PUT when the segmented control is used', async () => {
    const user = userEvent.setup()
    api.listCustomerNotificationOverrides.mockResolvedValue([row()])

    render(<CustomerOverridesTab canManage />)
    await selectCustomer(user)

    await screen.findByText('Overgenomen van standaard')
    const group = screen.getByRole('group', { name: 'Afwijking voor Opdracht geaccepteerd' })
    await user.click(within(group).getByRole('button', { name: 'Uit' }))

    await waitFor(() => expect(api.setCustomerNotificationOverride).toHaveBeenCalledWith('cust-1', 'order_accepted', false))
  })

  it('hides the segmented control without manage permission', async () => {
    const user = userEvent.setup()
    api.listCustomerNotificationOverrides.mockResolvedValue([row()])

    render(<CustomerOverridesTab canManage={false} />)
    await selectCustomer(user)

    await screen.findByText('Overgenomen van standaard')
    expect(screen.queryByRole('group', { name: 'Afwijking voor Opdracht geaccepteerd' })).not.toBeInTheDocument()
  })
})
