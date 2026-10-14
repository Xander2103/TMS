import { describe, expect, it, vi } from 'vitest'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { UnplannedPanel, type UnplannedFilters } from '../UnplannedPanel'
import { isPlannable, type UnplannedOrder } from '../../types'

const FILTERS: UnplannedFilters = {
  search: '', status: '', priority: '', fromDate: '', toDate: '', onlyWithAttention: false, includeDrafts: false,
}

function order(overrides: Partial<UnplannedOrder> = {}): UnplannedOrder {
  return {
    id: 'o-1',
    orderNumber: 'ORD-0028',
    orderDate: '2026-10-14',
    customerId: 'c-1',
    customerName: 'Müller Logistik GmbH',
    status: 'Confirmed',
    priority: 'Normal',
    firstLoadingCity: 'Aachen',
    lastUnloadingCity: 'Düsseldorf',
    requestedFrom: null,
    requestedTo: null,
    goodsDescription: 'Paletten',
    stopCount: 2,
    cargoLineCount: 1,
    packageCount: 0,
    totalWeightKg: 11000,
    totalVolumeM3: null,
    adrRequired: false,
    craneRequired: false,
    attentionBadges: ['MissingVolume', 'AppointmentRequired'],
    plannable: true,
    customerReference: 'PO-77',
    ...overrides,
  }
}

function renderPanel(orders: UnplannedOrder[], props: Partial<Parameters<typeof UnplannedPanel>[0]> = {}) {
  const onPlanRequest = vi.fn()
  const onSelect = vi.fn()
  const router = createMemoryRouter(
    [{
      path: '/planning-center',
      element: (
        <UnplannedPanel
          orders={orders}
          totalCount={orders.length}
          page={1}
          pageSize={50}
          isLoading={false}
          filters={FILTERS}
          selectedOrderId={null}
          onFiltersChange={vi.fn()}
          onPageChange={vi.fn()}
          onSelect={onSelect}
          onPlanRequest={onPlanRequest}
          onPriorityChange={vi.fn().mockResolvedValue(undefined)}
          {...props}
        />
      ),
    }],
    { initialEntries: ['/planning-center'] },
  )
  render(<RouterProvider router={router} />)
  return { onPlanRequest, onSelect }
}

describe('UnplannedPanel (B09)', () => {
  it('renders one compact line per order: number · customer · route · weight · ⚠ n', () => {
    renderPanel([order()])
    const row = screen.getByRole('listitem')
    expect(row).toHaveTextContent('ORD-0028')
    expect(row).toHaveTextContent('Müller Logistik GmbH')
    expect(row).toHaveTextContent('Aachen → Düsseldorf')
    expect(row).toHaveTextContent('11 t')
    expect(within(row).getByLabelText('2 aandachtspunten')).toBeInTheDocument()
    // The details (priority, Plan in…) stay folded until the row is selected.
    expect(within(row).queryByRole('button', { name: 'Plan in…' })).not.toBeInTheDocument()
    expect(row).toHaveAttribute('draggable', 'true')
  })

  it('the warning chip explains the reasons without a selection, in plain words', () => {
    renderPanel([order()])
    const tooltip = screen.getByRole('tooltip')
    expect(tooltip).toHaveTextContent('Aandachtspunten')
    expect(tooltip).toHaveTextContent('Volume ontbreekt')
    expect(tooltip).toHaveTextContent('Afspraak vereist')
    expect(tooltip).not.toHaveTextContent('MissingVolume')
  })

  it('a selected row unfolds its details and the one plan action', () => {
    const { onPlanRequest } = renderPanel([order()], { selectedOrderId: 'o-1' })
    const row = screen.getByRole('listitem')
    expect(row).toHaveTextContent('Ref. PO-77')
    expect(row).toHaveTextContent('2 stops')
    screen.getByRole('button', { name: 'Plan in…' }).click()
    expect(onPlanRequest).toHaveBeenCalledWith(expect.objectContaining({ id: 'o-1' }))
  })

  it('shows Draft work subdued and never draggable, with the reason and a way to the order', async () => {
    const user = userEvent.setup()
    const draft = order({ id: 'o-2', orderNumber: 'ORD-0029', status: 'Draft', plannable: false, attentionBadges: [] })
    const { onPlanRequest, onSelect } = renderPanel([draft])
    const row = screen.getByRole('listitem')
    expect(row).toHaveAttribute('draggable', 'false')
    expect(row).toHaveClass('pc-order-draft')
    expect(row).toHaveTextContent('Concept · nog niet planbaar')
    // Enter on a non-plannable row selects (details) instead of opening the planner.
    row.focus()
    await user.keyboard('{Enter}')
    expect(onPlanRequest).not.toHaveBeenCalled()
    expect(onSelect).toHaveBeenCalledWith(expect.objectContaining({ id: 'o-2' }))
  })

  it('a selected Draft row spells out the lifecycle reason and links to the order', () => {
    const draft = order({ id: 'o-2', orderNumber: 'ORD-0029', status: 'Draft', plannable: false })
    renderPanel([draft], { selectedOrderId: 'o-2' })
    expect(screen.getByText(/bevestig de opdracht eerst/)).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Opdracht openen →' })).toHaveAttribute('href', '/transport-orders/o-2')
    expect(screen.queryByRole('button', { name: 'Plan in…' })).not.toBeInTheDocument()
  })

  it('the Draft checkbox drives the includeDrafts filter', async () => {
    const user = userEvent.setup()
    const onFiltersChange = vi.fn()
    renderPanel([], { onFiltersChange })
    await user.click(screen.getByRole('checkbox', { name: 'Toon concepten' }))
    expect(onFiltersChange).toHaveBeenCalledWith(expect.objectContaining({ includeDrafts: true }))
  })

  it('isPlannable follows the server flag and falls back to the status on older payloads', () => {
    expect(isPlannable(order({ plannable: false }))).toBe(false)
    expect(isPlannable(order({ plannable: undefined, status: 'Submitted' }))).toBe(false)
    expect(isPlannable(order({ plannable: undefined, status: 'Confirmed' }))).toBe(true)
  })
})
