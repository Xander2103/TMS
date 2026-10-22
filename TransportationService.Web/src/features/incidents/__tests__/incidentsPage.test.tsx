import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { IncidentsPage } from '../pages/IncidentsPage'
import type { IncidentListItem } from '../types'

/**
 * PERF-H3: the register is paged and sorted by the SERVER. It used to receive at most 500 rows,
 * sort and slice them in the browser and report "500" as the total of 1,587 incidents.
 */
const auth = vi.hoisted(() => ({ permissions: new Set<string>(['incidents.view', 'incidents.manage']) }))
vi.mock('../../auth/authContextValue', () => ({
  useAuth: () => ({ hasPermission: (code: string) => auth.permissions.has(code), user: { id: 'u-1' } }),
}))

const api = vi.hoisted(() => ({ listIncidents: vi.fn(), listProblems: vi.fn() }))
vi.mock('../api/incidentsApi', () => api)

const lookups = vi.hoisted(() => ({ searchCustomers: vi.fn(), getCustomer: vi.fn(), searchDrivers: vi.fn(), getDriver: vi.fn(), getVehicleOptions: vi.fn() }))
vi.mock('../../customers/api/customersApi', () => ({ searchCustomers: lookups.searchCustomers, getCustomer: lookups.getCustomer }))
vi.mock('../../drivers/api/driversApi', () => ({ searchDrivers: lookups.searchDrivers, getDriver: lookups.getDriver }))
vi.mock('../../vehicles/api/vehiclesApi', () => ({ getVehicleOptions: lookups.getVehicleOptions }))

function incident(overrides: Partial<IncidentListItem> = {}): IncidentListItem {
  return {
    id: 'inc-1',
    title: 'Pallet beschadigd',
    incidentType: 'Damage',
    customTypeName: null,
    status: 'New',
    severity: 'High',
    customerName: 'Klant BV',
    responsibleName: null,
    dossierNumber: null,
    dueDate: null,
    isOverdue: false,
    createdAt: '2026-10-20T08:00:00Z',
    ...overrides,
  } as IncidentListItem
}

function paged(items: IncidentListItem[], totalCount: number, page = 1) {
  return { items, totalCount, page, pageSize: 25 }
}

function renderPage(initialEntry = '/incidents') {
  const router = createMemoryRouter([{ path: '/incidents', element: <IncidentsPage /> }], { initialEntries: [initialEntry] })
  render(<RouterProvider router={router} />)
  return router
}

const lastListCall = () => api.listIncidents.mock.calls.at(-1)?.[0] as Record<string, unknown>

describe('IncidentsPage (server-side paging and sort)', () => {
  beforeEach(() => {
    api.listIncidents.mockReset()
    api.listProblems.mockReset().mockResolvedValue([])
    lookups.searchCustomers.mockReset().mockResolvedValue({ items: [], totalCount: 0, page: 1, pageSize: 25 })
    lookups.searchDrivers.mockReset().mockResolvedValue({ items: [], totalCount: 0, page: 1, pageSize: 25 })
    lookups.getVehicleOptions.mockReset().mockResolvedValue([])
    lookups.getCustomer.mockReset().mockResolvedValue({ id: 'c-7', name: 'Van Caudenberg BV' })
    lookups.getDriver.mockReset()
  })

  it('asks the server for one page and shows the real total, not the page length', async () => {
    api.listIncidents.mockResolvedValue(paged([incident()], 1587))
    renderPage()

    expect(await screen.findByText('Pallet beschadigd')).toBeInTheDocument()
    expect(lastListCall()).toMatchObject({ page: 1, pageSize: 25 })
    expect(lastListCall().sort).toBeUndefined()
    // The pager reads the server's totalCount (1,587), never "500" or the page size.
    const pager = screen.getByRole('navigation', { name: 'Paginering' })
    expect(pager).toHaveTextContent('1–25 van 1587')
    expect(pager).toHaveTextContent('Pagina 1 / 64')
  })

  it('passes page and sort to the server instead of slicing and sorting in the browser', async () => {
    api.listIncidents.mockResolvedValue(paged([incident()], 1587, 3))
    const user = userEvent.setup()
    renderPage('/incidents?page=3')

    await screen.findByText('Pallet beschadigd')
    expect(lastListCall()).toMatchObject({ page: 3, pageSize: 25 })

    // Sorting a column goes to the server (first click = ascending, as every DataTable) and returns to page 1.
    await user.click(within(screen.getByRole('table')).getByRole('button', { name: /Ernst/ }))
    await waitFor(() => expect(lastListCall()).toMatchObject({ sort: 'severity', dir: 'asc', page: 1 }))
  })

  it('forwards the operational filters unchanged', async () => {
    api.listIncidents.mockResolvedValue(paged([], 0))
    renderPage('/incidents?status=New&severity=High&mine=1&customer=c-7&driver=d-2&vehicle=v-3&from=2026-10-01&to=2026-10-31')

    await waitFor(() => expect(api.listIncidents).toHaveBeenCalled())
    expect(lastListCall()).toMatchObject({
      status: 'New', severity: 'High', mine: true, customerId: 'c-7', driverId: 'd-2', vehicleId: 'v-3', from: '2026-10-01', to: '2026-10-31',
    })
  })

  it('names a customer filter restored from the URL through one detail call, and searches customers server-side', async () => {
    api.listIncidents.mockResolvedValue(paged([], 0))
    lookups.searchCustomers.mockResolvedValue({
      items: [{ id: 'c-8', customerNumber: 'KL-0008', name: 'Nexans NV', city: 'Gent', countryCode: 'BE', categoryName: null, isActive: true, isBlocked: false, vatNumber: 'BE0123456749' }],
      totalCount: 2173, page: 1, pageSize: 25,
    })
    const user = userEvent.setup()
    renderPage('/incidents?customer=c-7')

    const customerBox = await screen.findByRole('combobox', { name: 'Klant' })
    await waitFor(() => expect(customerBox).toHaveValue('Van Caudenberg BV'))
    expect(lookups.getCustomer).toHaveBeenCalledWith('c-7')

    // Typing asks the server (name, number, VAT), one short page — never a 200-row preload.
    await user.click(customerBox)
    await user.type(customerBox, 'nex')
    await waitFor(() =>
      expect(lookups.searchCustomers).toHaveBeenLastCalledWith(
        expect.objectContaining({ search: 'nex', page: 1, pageSize: 25 }),
        expect.any(AbortSignal),
      ),
    )
    expect(await screen.findByRole('option', { name: /Nexans NV/ })).toHaveTextContent('KL-0008 · BE0123456749 · Gent')
  })
})
