import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { CustomerDetailPage } from '../CustomerDetailPage'
import type { CustomerDetail } from '../../types'
import type { CustomerMessage } from '../../api/customerMessagesApi'

vi.mock('../../../auth/authContextValue', () => ({
  useAuth: () => ({
    hasPermission: (code: string) => code === 'customer_messages.view' || code === 'customer_messages.send',
    hasAnyPermission: (codes: string[]) => codes.includes('customers.view'),
  }),
}))

vi.mock('../../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }),
}))

function customer(): CustomerDetail {
  return {
    id: 'c1', customerNumber: 'KL-1', name: 'Haven BV', legalName: null, vatNumber: null,
    categoryId: null, categoryName: null, email: null, phoneNumber: null, website: null,
    street: null, houseNumber: null, postalCode: null, city: null, countryCode: null,
    invoiceEmail: null, paymentTermDays: 30, defaultLanguageCode: null, notes: null,
    isActive: true, isBlocked: false, blockReason: null, nickname: null, companyNumber: null,
    currencyCode: 'EUR', iban: null, bic: null, bankName: null, bankAccountNumber: null,
    defaultLegalEntityId: null, contacts: [],
    vatTreatment: 'DomesticVat', defaultVatRatePercent: null, vatCountryCode: null, vatNotes: null,
    peppolId: null, peppolScheme: null, invoiceLanguageCode: null, purchaseOrderRequired: false,
    signedDeliveryNoteRequired: false, customerReferenceRequired: false,
  } as CustomerDetail
}

vi.mock('../../api/customersApi', () => ({
  getCustomer: () => Promise.resolve(customer()),
  changeCustomerNumber: vi.fn(),
}))

const unreadCount = vi.hoisted(() => ({ value: 3 }))
const markReadSpy = vi.hoisted(() => vi.fn())
const messages = vi.hoisted(() => ({ value: [] as CustomerMessage[] }))

vi.mock('../../api/customerMessagesApi', () => ({
  getCustomerMessagesUnreadCount: () => Promise.resolve({ count: unreadCount.value }),
  listCustomerMessages: () => Promise.resolve(messages.value),
  markCustomerMessagesRead: (...args: unknown[]) => {
    markReadSpy(...args)
    return Promise.resolve(undefined)
  },
  sendCustomerMessage: vi.fn(),
}))

function renderPage() {
  return render(
    <MemoryRouter initialEntries={['/customers/c1']}>
      <Routes>
        <Route path="/customers/:id" element={<CustomerDetailPage />} />
      </Routes>
    </MemoryRouter>,
  )
}

describe('CustomerDetailPage — Berichten tab unread badge (fix round 1, spec gap #4)', () => {
  beforeEach(() => {
    unreadCount.value = 3
    markReadSpy.mockReset()
    messages.value = []
  })

  it('shows the unread count as a badge on the Berichten tab', async () => {
    renderPage()

    await waitFor(() => expect(screen.getByRole('heading', { name: 'Haven BV' })).toBeInTheDocument())
    const tab = await screen.findByRole('tab', { name: /Berichten/ })
    expect(tab).toHaveTextContent('3')
  })

  it('clears the badge once the thread is marked read', async () => {
    const user = userEvent.setup()
    renderPage()

    await waitFor(() => expect(screen.getByRole('heading', { name: 'Haven BV' })).toBeInTheDocument())
    const tab = await screen.findByRole('tab', { name: /Berichten/ })
    expect(tab).toHaveTextContent('3')

    await user.click(tab)

    await waitFor(() => expect(markReadSpy).toHaveBeenCalledWith('c1', null))
    await waitFor(() => expect(screen.getByRole('tab', { name: /Berichten/ })).not.toHaveTextContent('3'))
  })
})
