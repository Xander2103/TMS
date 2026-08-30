import { afterAll, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { CustomerPortalOrderDetailPage } from '../CustomerPortalOrderDetailPage'
import { CustomerPortalNewOrderPage } from '../CustomerPortalNewOrderPage'

/**
 * C-03 in the customer portal. The deliberate split: the DATE FORMAT follows the portal
 * language (src/i18n/formatters.ts), the WALL CLOCK follows the carrier's (tenant) zone — a
 * stop window is an appointment at the carrier's dock, so it must read the same for the
 * planner and for the customer, whatever device or country the customer is on.
 *
 * The process zone is forced to America/New_York to prove the browser zone plays no part.
 */
declare const process: { env: Record<string, string | undefined> }

const ORIGINAL_TZ = process.env.TZ

beforeAll(() => {
  process.env.TZ = 'America/New_York'
})

afterAll(() => {
  if (ORIGINAL_TZ === undefined) delete process.env.TZ
  else process.env.TZ = ORIGINAL_TZ
})

const auth = vi.hoisted(() => ({ permissions: ['customer_portal.view', 'customer_portal.create_orders'] }))

vi.mock('../../../auth/authContextValue', () => ({
  useAuth: () => ({
    status: 'authenticated' as const,
    user: { id: 'u1', firstName: 'Kaat', customerId: 'cust-1' },
    login: vi.fn(),
    logout: vi.fn(),
    hasPermission: (code: string) => auth.permissions.includes(code),
    hasAnyPermission: (codes: string[]) => codes.some((c) => auth.permissions.includes(c)),
  }),
}))

const toast = vi.hoisted(() => ({ success: vi.fn(), error: vi.fn() }))
vi.mock('../../../../components/ui/toastContext', () => ({
  useToast: () => ({ showSuccess: toast.success, showError: toast.error }),
}))

const api = vi.hoisted(() => ({
  getPortalOrder: vi.fn(),
  listPortalDocuments: vi.fn(),
  listPortalLocations: vi.fn(),
  submitPortalOrder: vi.fn(),
  createPortalLocation: vi.fn(),
}))
vi.mock('../../api/customerPortalApi', () => ({
  getPortalOrder: api.getPortalOrder,
  listPortalDocuments: api.listPortalDocuments,
  listPortalLocations: api.listPortalLocations,
  submitPortalOrder: api.submitPortalOrder,
  createPortalLocation: api.createPortalLocation,
  downloadPortalDocument: vi.fn(),
}))

function portalOrder() {
  return {
    id: 'order-1', orderNumber: 'ORD-0001', orderDate: '2026-07-15',
    status: 'Confirmed', customerReference: null, goodsDescription: 'Pallets',
    notes: null, cancellationReason: null,
    stops: [{
      sequence: 1, stopType: 'Loading', locationName: 'Magazijn Antwerpen', city: 'Antwerpen',
      // 08:00–10:00 Europe/Amsterdam, as the API stores and serves it.
      requestedFrom: '2026-07-15T06:00:00Z', requestedTo: '2026-07-15T08:00:00Z',
      reference: null, instructions: null,
    }],
    cargoItems: [], timeline: [], exceptions: [],
  }
}

beforeEach(() => {
  auth.permissions = ['customer_portal.view', 'customer_portal.create_orders']
  api.getPortalOrder.mockReset()
  api.listPortalDocuments.mockReset().mockResolvedValue([])
  api.listPortalLocations.mockReset().mockResolvedValue([])
  api.submitPortalOrder.mockReset().mockResolvedValue(portalOrder())
  toast.success.mockReset()
  toast.error.mockReset()
})

describe('CustomerPortalOrderDetailPage — window rendering', () => {
  it('shows the requested window in the carrier zone, not the customer browser zone', async () => {
    api.getPortalOrder.mockResolvedValue(portalOrder())
    render(
      <MemoryRouter initialEntries={['/klantportaal/opdracht/order-1']}>
        <Routes>
          <Route path="/klantportaal/opdracht/:id" element={<CustomerPortalOrderDetailPage />} />
        </Routes>
      </MemoryRouter>,
    )

    // 08:00 Amsterdam, in the Dutch portal's short notation. New York would have said 02:00.
    const cell = await screen.findByText(/08:00/)
    expect(cell.textContent).toContain('10:00')
    expect(screen.queryByText(/02:00/)).not.toBeInTheDocument()
    expect(screen.queryByText(/06:00/)).not.toBeInTheDocument()
  })
})

describe('CustomerPortalNewOrderPage — window encoding', () => {
  it('submits the typed wall clock as a UTC instant with an explicit Z', async () => {
    const user = userEvent.setup()
    const { container } = render(
      <MemoryRouter>
        <CustomerPortalNewOrderPage />
      </MemoryRouter>,
    )

    const cities = container.querySelectorAll<HTMLInputElement>('input[id^="cp-city-"]')
    expect(cities).toHaveLength(2)
    fireEvent.change(cities[0], { target: { value: 'Antwerpen' } })
    fireEvent.change(cities[1], { target: { value: 'Gent' } })

    const from = container.querySelector<HTMLInputElement>('input[id^="cp-from-"]')!
    fireEvent.change(from, { target: { value: '2026-07-15T08:00' } })

    await user.click(screen.getByRole('button', { name: 'Opdracht indienen' }))

    await waitFor(() => expect(api.submitPortalOrder).toHaveBeenCalled())
    const payload = api.submitPortalOrder.mock.calls[0][0]
    // Was "2026-07-15T08:00" — no zone at all, and read as 08:00 UTC downstream.
    expect(payload.stops[0].requestedFrom).toBe('2026-07-15T06:00:00Z')
    expect(payload.stops[0].requestedTo).toBeNull()
  })
})
