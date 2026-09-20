import { describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { IssuedItemTemplatesPanel } from '../components/IssuedItemTemplatesPanel'
import type { IssuedItemTemplate } from '../issuedItemsApi'

vi.mock('../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }),
}))

function makeTemplate(overrides: Partial<IssuedItemTemplate>): IssuedItemTemplate {
  return {
    id: 't-1',
    name: 'Toegangsbadge',
    category: 'Algemeen',
    categoryId: null,
    applicableJobFunctionCodes: null,
    defaultQuantity: 1,
    requiresSerialNumber: false,
    requiresReceivedDate: true,
    returnRequired: false,
    isActive: true,
    sortOrder: 0,
    description: null,
    unit: 'piece',
    notes: null,
    stockTrackingEnabled: false,
    variantsEnabled: false,
    allowNegativeStock: false,
    lowStockThreshold: null,
    minimumStock: null,
    targetStockLevel: null,
    reorderQuantity: null,
    negativeStockRequiresReason: false,
    stockStatus: 'Normal',
    storageLocation: null,
    currentStock: 0,
    totalAvailable: 0,
    lowStock: false,
    variantCount: 0,
    ...overrides,
  }
}

const templates = vi.hoisted(() => ({ value: [] as IssuedItemTemplate[] }))

vi.mock('../issuedItemsApi', async () => {
  const actual = await vi.importActual<typeof import('../issuedItemsApi')>('../issuedItemsApi')
  return {
    ...actual,
    listIssuedItemTemplates: () => Promise.resolve(templates.value),
  }
})

function renderPanel() {
  return render(
    <MemoryRouter>
      <IssuedItemTemplatesPanel />
    </MemoryRouter>,
  )
}

describe('IssuedItemTemplatesPanel stock overview', () => {
  it('shows stock availability with the catalogue unit label, variants and a low-stock warning', async () => {
    templates.value = [
      makeTemplate({ id: 't-1', name: 'Veiligheidsschoenen', stockTrackingEnabled: true, variantsEnabled: true, variantCount: 4, totalAvailable: 2, lowStockThreshold: 3, lowStock: true, unit: 'pair' }),
      makeTemplate({ id: 't-2', name: 'Toegangsbadge' }),
    ]
    renderPanel()

    await waitFor(() => expect(screen.getByText('Veiligheidsschoenen')).toBeInTheDocument())
    const shoeRow = screen.getByRole('link', { name: 'Veiligheidsschoenen' }).closest('tr')!
    // The stored code "pair" renders as its Dutch label.
    expect(shoeRow).toHaveTextContent('2 Paar')
    expect(shoeRow).toHaveTextContent('Lage voorraad')
    expect(shoeRow).toHaveTextContent('4') // variant count
    // Non-stock template shows no availability.
    const badgeRow = screen.getByRole('link', { name: 'Toegangsbadge' }).closest('tr')!
    expect(badgeRow).toHaveTextContent('Nee')
  })

  it('renders a legacy free-text unit as "Overige" instead of leaking the raw value', async () => {
    templates.value = [makeTemplate({ id: 't-1', name: 'Handschoenen', stockTrackingEnabled: true, totalAvailable: 7, unit: 'Paar' })]
    renderPanel()

    await waitFor(() => expect(screen.getByText('Handschoenen')).toBeInTheDocument())
    expect(screen.getByRole('link', { name: 'Handschoenen' }).closest('tr')!).toHaveTextContent('7 Overige')
  })

  it('filters on low stock', async () => {
    templates.value = [
      makeTemplate({ id: 't-1', name: 'Veiligheidsschoenen', stockTrackingEnabled: true, totalAvailable: 1, lowStockThreshold: 3, lowStock: true }),
      makeTemplate({ id: 't-2', name: 'Toegangsbadge', stockTrackingEnabled: true, totalAvailable: 50, lowStock: false }),
    ]
    renderPanel()
    await waitFor(() => expect(screen.getByText('Toegangsbadge')).toBeInTheDocument())

    await userEvent.selectOptions(screen.getByLabelText('Voorraad'), 'low')

    expect(screen.getByText('Veiligheidsschoenen')).toBeInTheDocument()
    expect(screen.queryByText('Toegangsbadge')).not.toBeInTheDocument()
  })

  it('links a template to its detail page under /issued-items', async () => {
    templates.value = [makeTemplate({ id: 't-9', name: 'Scanner' })]
    renderPanel()
    await waitFor(() => expect(screen.getByText('Scanner')).toBeInTheDocument())
    expect(screen.getByRole('link', { name: 'Scanner' })).toHaveAttribute('href', '/issued-items/templates/t-9')
  })
})
