import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom'
import { InventoryIssuesPage } from '../pages/InventoryIssuesPage'
import type { InventoryIssueRow } from '../inventoryApi'

// WH-H3/H4: the warehouse desk — who has what, what must come back, "Retour verwerken" — with the
// filters in the URL (WH-M9) and no HR permission involved.

const toast = vi.hoisted(() => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }))
vi.mock('../../../components/ui/toastContext', () => ({ useToast: () => toast }))

const permissions = vi.hoisted(() => ({ value: new Set<string>() }))
vi.mock('../../auth/authContextValue', () => ({
  useAuth: () => ({ hasPermission: (code: string) => permissions.value.has(code) }),
}))

const api = vi.hoisted(() => ({ list: vi.fn(), returnItem: vi.fn() }))
vi.mock('../inventoryApi', async () => {
  const actual = await vi.importActual<typeof import('../inventoryApi')>('../inventoryApi')
  return {
    ...actual,
    listInventoryIssues: (...args: unknown[]) => api.list(...args),
    returnToWarehouse: (...args: unknown[]) => api.returnItem(...args),
  }
})

function row(overrides: Partial<InventoryIssueRow> = {}): InventoryIssueRow {
  return {
    itemId: 'i-1', employeeId: 'e-1', employeeNumber: 'MED-0009', employeeName: 'Anna Aerts', employeeActive: true,
    templateId: 't-1', itemCode: 'BM-0001', itemName: 'TEST WH Golden Scanner', variantLabel: null,
    serialNumber: 'SER-GOLD-001', quantity: 1, status: 'Issued', returnDisposition: null,
    issuedDate: '2026-10-18', expectedReturnDate: '2026-10-10', returnedDate: null,
    isOverdue: true, returnRequired: true, isConsumable: false,
    issuedByName: 'Mia Magazijn', receivedBackByName: null, duplicateOpenSerial: false,
    ...overrides,
  }
}

function LocationProbe() {
  const location = useLocation()
  return <output data-testid="location">{location.search}</output>
}

function renderPage(entry = '/inventory/issues') {
  return render(
    <MemoryRouter initialEntries={[entry]}>
      <Routes>
        <Route path="/inventory/issues" element={<><InventoryIssuesPage /><LocationProbe /></>} />
      </Routes>
    </MemoryRouter>,
  )
}

describe('InventoryIssuesPage', () => {
  beforeEach(() => {
    api.list.mockReset()
    api.returnItem.mockReset()
    toast.showSuccess.mockReset()
    permissions.value = new Set(['inventory.issue', 'inventory.return'])
  })

  it('answers "who has SER-GOLD-001" with employee, number, code, serial and overdue flag', async () => {
    api.list.mockResolvedValue([row()])
    renderPage()

    const cell = await screen.findByText('Anna Aerts')
    const tableRow = cell.closest('tr')!
    expect(within(tableRow).getByText('MED-0009')).toBeInTheDocument()
    expect(within(tableRow).getByText('BM-0001')).toBeInTheDocument()
    expect(within(tableRow).getByText('SER-GOLD-001')).toBeInTheDocument()
    expect(within(tableRow).getByText('Te laat')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Uitgeven' })).toBeInTheDocument()
  })

  it('keeps search and filters in the URL and sends them to the server', async () => {
    api.list.mockResolvedValue([])
    renderPage('/inventory/issues?q=ser-gold&overdue=1')

    await waitFor(() => expect(api.list).toHaveBeenCalledWith(expect.objectContaining({ scope: 'open', search: 'ser-gold', overdueOnly: true })))
    await userEvent.click(screen.getByRole('checkbox', { name: 'Inactieve medewerkers' }))
    await waitFor(() => expect(screen.getByTestId('location')).toHaveTextContent('inactive=1'))
    expect(screen.getByTestId('location')).toHaveTextContent('q=ser-gold')
  })

  it('processes a return from the warehouse and reloads the list', async () => {
    api.list.mockResolvedValueOnce([row()]).mockResolvedValue([])
    api.returnItem.mockResolvedValue({})
    renderPage()

    await userEvent.click(await screen.findByRole('button', { name: 'Retour verwerken' }))
    const dialog = await screen.findByRole('dialog', { name: 'Middel teruggebracht' })
    expect(within(dialog).getByText(/SER-GOLD-001/)).toBeInTheDocument()
    await userEvent.selectOptions(within(dialog).getByLabelText(/Retourconditie/), 'lost')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Markeer als teruggebracht' }))

    await waitFor(() => expect(api.returnItem).toHaveBeenCalledWith('i-1', expect.objectContaining({ returnDisposition: 'lost', restoreStock: false })))
    await waitFor(() => expect(screen.getByText('Er staan geen uitgiftes open.')).toBeInTheDocument())
  })

  it('history labels a lost return as "Verloren", never as plain "Teruggebracht"', async () => {
    api.list.mockResolvedValue([row({ status: 'Returned', returnDisposition: 'lost', returnedDate: '2026-10-18', receivedBackByName: 'Mia Magazijn' })])
    renderPage('/inventory/issues?scope=history')

    expect(await screen.findByText('Verloren')).toBeInTheDocument()
    expect(screen.queryByText('Teruggebracht')).not.toBeInTheDocument()
    expect(screen.getByText('Teruggenomen door Mia Magazijn')).toBeInTheDocument()
  })

  it('a viewer without issue/return rights sees the list but no actions', async () => {
    permissions.value = new Set(['inventory.loans_view'])
    api.list.mockResolvedValue([row()])
    renderPage()

    await screen.findByText('Anna Aerts')
    expect(screen.queryByRole('button', { name: 'Uitgeven' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Retour verwerken' })).not.toBeInTheDocument()
  })
})
