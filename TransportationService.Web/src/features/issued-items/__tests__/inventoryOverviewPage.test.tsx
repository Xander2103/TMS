import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { InventoryOverviewPage } from '../pages/InventoryOverviewPage'
import type { InventoryOverviewRow } from '../inventoryApi'

const overview = vi.hoisted(() => ({ value: [] as InventoryOverviewRow[] }))

vi.mock('../inventoryApi', async () => {
  const actual = await vi.importActual<typeof import('../inventoryApi')>('../inventoryApi')
  return {
    ...actual,
    getInventoryOverview: () => Promise.resolve(overview.value),
  }
})

function makeRow(overrides: Partial<InventoryOverviewRow>): InventoryOverviewRow {
  return {
    templateId: 't-1',
    variantId: null,
    name: 'Handschoenen',
    variantLabel: null,
    category: 'PBM',
    storageLocation: 'Rek A1',
    unit: 'paar',
    currentStock: 12,
    warningLevel: 5,
    minimumLevel: 2,
    targetLevel: 20,
    reorderQuantity: 10,
    status: 'Normal',
    lastMovementAt: '2026-07-30T10:00:00Z',
    allowNegativeStock: false,
    returnRequired: false,
    ...overrides,
  }
}

function renderPage() {
  return render(
    <MemoryRouter>
      <InventoryOverviewPage />
    </MemoryRouter>,
  )
}

describe('InventoryOverviewPage', () => {
  afterEach(cleanup)

  it('renders overview rows with status badges and detail links', async () => {
    overview.value = [
      makeRow({ templateId: 't-1', name: 'Handschoenen', status: 'Normal' }),
      makeRow({ templateId: 't-2', name: 'Veiligheidsbril', status: 'LowStock', currentStock: 3 }),
      makeRow({ templateId: 't-3', name: 'Helm', variantId: 'v-1', variantLabel: 'maat M', status: 'NegativeStock', currentStock: -2 }),
    ]
    renderPage()

    await waitFor(() => expect(screen.getByText('Handschoenen')).toBeInTheDocument())
    expect(screen.getByText('Veiligheidsbril')).toBeInTheDocument()
    expect(screen.getByText('Helm')).toBeInTheDocument()
    expect(screen.getByText('— maat M')).toBeInTheDocument()
    // "Normaal" also exists as a filter option; assert specifically on the status badge.
    expect(screen.getByText('Normaal', { selector: '.ui-badge' })).toBeInTheDocument()

    const detailLinks = screen.getAllByRole('link', { name: 'Detail' })
    expect(detailLinks).toHaveLength(3)
    expect(detailLinks[0]).toHaveAttribute('href', '/settings/issued-item-templates/t-1?tab=voorraad')
  })

  it('filters rows via the status select', async () => {
    overview.value = [
      makeRow({ templateId: 't-1', name: 'Handschoenen', status: 'Normal' }),
      makeRow({ templateId: 't-2', name: 'Veiligheidsbril', status: 'LowStock' }),
    ]
    renderPage()

    await waitFor(() => expect(screen.getByText('Handschoenen')).toBeInTheDocument())
    await userEvent.selectOptions(screen.getByLabelText('Statusfilter'), 'LowStock')

    expect(screen.queryByText('Handschoenen')).not.toBeInTheDocument()
    expect(screen.getByText('Veiligheidsbril')).toBeInTheDocument()
  })

  it('counts statuses on the tiles and filters when a tile is clicked', async () => {
    overview.value = [
      makeRow({ templateId: 't-1', name: 'Handschoenen', status: 'Normal' }),
      makeRow({ templateId: 't-2', name: 'Veiligheidsbril', status: 'LowStock' }),
      makeRow({ templateId: 't-3', name: 'Helm', status: 'NegativeStock' }),
    ]
    renderPage()

    await waitFor(() => expect(screen.getByText('Handschoenen')).toBeInTheDocument())

    const negativeTile = screen.getByRole('button', { name: /Negatief/ })
    expect(negativeTile).toHaveTextContent('1')

    await userEvent.click(negativeTile)
    expect(negativeTile).toHaveAttribute('aria-pressed', 'true')
    expect(screen.queryByText('Handschoenen')).not.toBeInTheDocument()
    expect(screen.queryByText('Veiligheidsbril')).not.toBeInTheDocument()
    expect(screen.getByText('Helm')).toBeInTheDocument()

    // Clicking the active tile again clears the filter.
    await userEvent.click(negativeTile)
    expect(negativeTile).toHaveAttribute('aria-pressed', 'false')
    expect(screen.getByText('Handschoenen')).toBeInTheDocument()
  })

  it('searches by article name client-side', async () => {
    overview.value = [
      makeRow({ templateId: 't-1', name: 'Handschoenen' }),
      makeRow({ templateId: 't-2', name: 'Veiligheidsbril' }),
    ]
    renderPage()

    await waitFor(() => expect(screen.getByText('Handschoenen')).toBeInTheDocument())
    await userEvent.type(screen.getByLabelText('Zoeken'), 'bril')

    expect(screen.queryByText('Handschoenen')).not.toBeInTheDocument()
    expect(screen.getByText('Veiligheidsbril')).toBeInTheDocument()
  })
})
