import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor } from '@testing-library/react'
import { IssuedItemsTab } from '../IssuedItemsTab'
import type { EmployeeIssuedItem } from '../issuedItemsApi'

const auth = vi.hoisted(() => ({ permissions: ['issued_items.view'] }))

vi.mock('../../auth/authContextValue', () => ({
  useAuth: () => ({
    status: 'authenticated' as const,
    user: null,
    login: vi.fn(),
    logout: vi.fn(),
    hasPermission: (code: string) => auth.permissions.includes(code),
    hasAnyPermission: (codes: string[]) => codes.some((c) => auth.permissions.includes(c)),
  }),
}))

vi.mock('../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }),
}))

const items = vi.hoisted(() => ({ value: [] as EmployeeIssuedItem[] }))

vi.mock('../issuedItemsApi', async () => {
  const actual = await vi.importActual<typeof import('../issuedItemsApi')>('../issuedItemsApi')
  return {
    ...actual,
    listEmployeeIssuedItems: () => Promise.resolve(items.value),
    listIssuedItemTemplates: () => Promise.resolve([]),
  }
})

function makeItem(overrides: Partial<EmployeeIssuedItem>): EmployeeIssuedItem {
  return {
    id: 'ii-1',
    templateId: null,
    name: 'Laptop',
    category: 'Algemeen',
    status: 'Issued',
    issuedDate: '2026-07-01',
    quantity: 1,
    serialNumber: 'SN-9',
    notes: null,
    issuedByUserId: null,
    returnedDate: null,
    returnCondition: null,
    receivedBackByUserId: null,
    ...overrides,
  }
}

describe('IssuedItemsTab', () => {
  afterEach(cleanup)

  it('renders issued items with a status badge and PDF download for a read-only user', async () => {
    auth.permissions = ['issued_items.view']
    items.value = [makeItem({})]
    render(<IssuedItemsTab employeeId="emp-1" />)

    await waitFor(() => expect(screen.getByText('Laptop')).toBeInTheDocument())
    expect(screen.getByText('SN-9')).toBeInTheDocument()
    // Status badge label "Uitgereikt" appears alongside the same-named date column header.
    expect(screen.getAllByText('Uitgereikt').length).toBeGreaterThanOrEqual(2)
    expect(screen.getByText('Ontvangstbewijs (PDF)')).toBeInTheDocument()
    // A read-only user gets no manage action.
    expect(screen.queryByText('Bedrijfsmiddel toevoegen')).not.toBeInTheDocument()
  })

  it('shows the add button for a manager', async () => {
    auth.permissions = ['issued_items.view', 'issued_items.manage']
    items.value = []
    render(<IssuedItemsTab employeeId="emp-2" />)

    await waitFor(() => expect(screen.getByText('Bedrijfsmiddel toevoegen')).toBeInTheDocument())
  })
})
