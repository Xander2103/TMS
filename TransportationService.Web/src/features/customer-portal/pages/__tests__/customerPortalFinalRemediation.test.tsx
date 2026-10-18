import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { CustomerPortalOrderDetailPage } from '../CustomerPortalOrderDetailPage'
import { CustomerPortalNewOrderPage } from '../CustomerPortalNewOrderPage'
import { todayLocalIsoDate } from '../../../../utils/dates'

/**
 * Customer portal final remediation: the portal only TRANSLATES the server's customer status (no
 * internal "Afgerond"/"Gefactureerd"), the info request can be answered right on the order, and
 * the request form defaults to the carrier's local date, offers a country and never submits twice.
 */
const auth = vi.hoisted(() => ({ permissions: ['customer_portal.view', 'customer_portal.submit_orders', 'customer_portal.messages'] }))

vi.mock('../../../auth/authContextValue', () => ({
  useAuth: () => ({
    status: 'authenticated' as const,
    user: { id: 'u1', firstName: 'Anna', customerId: 'cust-a' },
    login: vi.fn(),
    logout: vi.fn(),
    hasPermission: (code: string) => auth.permissions.includes(code),
    hasAnyPermission: (codes: string[]) => codes.some((c) => auth.permissions.includes(c)),
  }),
}))

vi.mock('../../../../components/ui/toastContext', () => ({
  useToast: () => ({ showSuccess: vi.fn(), showError: vi.fn() }),
}))

const api = vi.hoisted(() => ({
  getPortalOrder: vi.fn(),
  listPortalDocuments: vi.fn(),
  listPortalLocations: vi.fn(),
  submitPortalOrder: vi.fn(),
  createPortalLocation: vi.fn(),
  sendPortalMessage: vi.fn(),
}))
vi.mock('../../api/customerPortalApi', () => ({
  getPortalOrder: api.getPortalOrder,
  listPortalDocuments: api.listPortalDocuments,
  listPortalLocations: api.listPortalLocations,
  submitPortalOrder: api.submitPortalOrder,
  createPortalLocation: api.createPortalLocation,
  sendPortalMessage: api.sendPortalMessage,
  downloadPortalDocument: vi.fn(),
}))

function order(overrides: Record<string, unknown> = {}) {
  return {
    id: 'order-1', orderNumber: 'ORD-0101', orderDate: '2026-10-20', status: 'Received',
    customerReference: 'PO-A', goodsDescription: 'Paletten',
    stops: [], cargoItems: [], timeline: [], exceptions: [],
    ...overrides,
  }
}

function renderDetail() {
  return render(
    <MemoryRouter initialEntries={['/klantportaal/orders/order-1']}>
      <Routes>
        <Route path="/klantportaal/orders/:id" element={<CustomerPortalOrderDetailPage />} />
      </Routes>
    </MemoryRouter>,
  )
}

beforeEach(() => {
  auth.permissions = ['customer_portal.view', 'customer_portal.submit_orders', 'customer_portal.messages']
  api.getPortalOrder.mockReset()
  api.listPortalDocuments.mockReset().mockResolvedValue([])
  api.listPortalLocations.mockReset().mockResolvedValue([])
  api.submitPortalOrder.mockReset().mockResolvedValue(order())
  api.sendPortalMessage.mockReset().mockResolvedValue({})
})

describe('CustomerPortalOrderDetailPage — customer-facing status', () => {
  it('shows the delivery truth and invoice availability side by side, never billing as the status', async () => {
    api.getPortalOrder.mockResolvedValue(order({
      status: 'Delivered', invoiceAvailable: true,
      timeline: [{ status: 'Received', changedAt: '2026-10-18T08:00:00Z' }, { status: 'Delivered', changedAt: '2026-10-20T09:00:00Z' }],
    }))
    renderDetail()

    expect((await screen.findAllByText('Geleverd')).length).toBeGreaterThan(0)
    expect(screen.getByText('Factuur beschikbaar')).toBeInTheDocument()
    expect(screen.getByText('Aanvraag ontvangen')).toBeInTheDocument()
    expect(screen.queryByText('Gefactureerd')).not.toBeInTheDocument()
    expect(screen.queryByText('Afgerond')).not.toBeInTheDocument()
  })

  it('tells a failed delivery as "Niet geleverd" with its explanation', async () => {
    api.getPortalOrder.mockResolvedValue(order({ status: 'NotDelivered', deliveryOutcome: 'NotDelivered' }))
    renderDetail()

    expect(await screen.findByText('Niet geleverd')).toBeInTheDocument()
    expect(screen.getByText(/De levering kon niet worden uitgevoerd/)).toBeInTheDocument()
  })
})

describe('CustomerPortalOrderDetailPage — info request round trip', () => {
  it('shows the planner question and sends the answer on the order thread', async () => {
    api.getPortalOrder
      .mockResolvedValueOnce(order({
        status: 'InfoRequested',
        infoRequest: { question: 'Wat is het gewicht per pallet?', askedAt: '2026-10-18T08:00:00Z', answeredAt: null },
      }))
      .mockResolvedValueOnce(order({
        status: 'InReview',
        infoRequest: { question: 'Wat is het gewicht per pallet?', askedAt: '2026-10-18T08:00:00Z', answeredAt: '2026-10-18T09:00:00Z' },
      }))
    const user = userEvent.setup()
    renderDetail()

    expect(await screen.findByText('Wat is het gewicht per pallet?')).toBeInTheDocument()
    expect(screen.getByText('Informatie gevraagd')).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Antwoord versturen' }))
    expect(screen.getByRole('alert')).toHaveTextContent('Schrijf eerst uw antwoord.')
    expect(api.sendPortalMessage).not.toHaveBeenCalled()

    await user.type(screen.getByLabelText('Uw antwoord'), '650 kg per pallet.')
    await user.click(screen.getByRole('button', { name: 'Antwoord versturen' }))

    await waitFor(() => expect(api.sendPortalMessage).toHaveBeenCalledWith('order-1', '650 kg per pallet.'))
    expect(await screen.findByText(/Uw antwoord is ontvangen/)).toBeInTheDocument()
    expect(screen.getByText('Wordt beoordeeld')).toBeInTheDocument()
  })

  it('explains what to do when the user has no message right', async () => {
    auth.permissions = ['customer_portal.view']
    api.getPortalOrder.mockResolvedValue(order({
      status: 'InfoRequested', infoRequest: { question: 'Laadadres?', askedAt: '2026-10-18T08:00:00Z', answeredAt: null },
    }))
    renderDetail()

    expect(await screen.findByText(/U kunt zelf geen berichten sturen/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Antwoord versturen' })).not.toBeInTheDocument()
  })
})

describe('CustomerPortalNewOrderPage — intake', () => {
  it('defaults to the local date, sends the chosen country, submits once and opens the request', async () => {
    let resolveSubmit: (value: unknown) => void = () => {}
    api.submitPortalOrder.mockReturnValue(new Promise((resolve) => { resolveSubmit = resolve }))
    const { container } = render(
      <MemoryRouter initialEntries={['/klantportaal/new']}>
        <Routes>
          <Route path="/klantportaal/new" element={<CustomerPortalNewOrderPage />} />
          <Route path="/klantportaal/orders/:id" element={<p>detail page</p>} />
        </Routes>
      </MemoryRouter>,
    )

    expect(container.querySelector<HTMLInputElement>('#cp-date')!.value).toBe(todayLocalIsoDate())

    const cities = container.querySelectorAll<HTMLInputElement>('input[id^="cp-city-"]')
    fireEvent.change(cities[0], { target: { value: 'Antwerpen' } })
    fireEvent.change(cities[1], { target: { value: 'Breda' } })
    const countries = container.querySelectorAll<HTMLSelectElement>('select[id^="cp-country-"]')
    expect(countries).toHaveLength(2)
    fireEvent.change(countries[1], { target: { value: 'NL' } })

    const submit = screen.getByRole('button', { name: 'Opdracht indienen' })
    fireEvent.click(submit)
    fireEvent.click(submit) // a double click before React re-renders
    resolveSubmit(order({ id: 'order-9' }))

    expect(await screen.findByText('detail page')).toBeInTheDocument()
    expect(api.submitPortalOrder).toHaveBeenCalledTimes(1)
    const payload = api.submitPortalOrder.mock.calls[0][0]
    expect(payload.stops[0].countryCode).toBe('BE')
    expect(payload.stops[1].countryCode).toBe('NL')
  })
})
