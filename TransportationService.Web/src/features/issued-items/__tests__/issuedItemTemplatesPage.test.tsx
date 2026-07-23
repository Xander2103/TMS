import { describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { IssuedItemTemplatesPage } from '../pages/IssuedItemTemplatesPage'
import type { IssuedItemTemplate } from '../issuedItemsApi'

vi.mock('../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }),
}))

function makeTemplate(overrides: Partial<IssuedItemTemplate>): IssuedItemTemplate {
  return {
    id: 't-1',
    name: 'Toegangsbadge',
    category: 'Algemeen',
    applicableJobFunctionCodes: null,
    defaultQuantity: 1,
    requiresSerialNumber: false,
    requiresReceivedDate: true,
    returnRequired: false,
    isActive: true,
    sortOrder: 0,
    description: null,
    unit: null,
    notes: null,
    stockTrackingEnabled: false,
    variantsEnabled: false,
    allowNegativeStock: false,
    lowStockThreshold: null,
    minimumStock: null,
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

function renderPage() {
  return render(
    <MemoryRouter>
      <IssuedItemTemplatesPage />
    </MemoryRouter>,
  )
}

describe('IssuedItemTemplatesPage stock overview', () => {
  it('shows stock availability, variants and a low-stock warning', async () => {
    templates.value = [
      makeTemplate({ id: 't-1', name: 'Veiligheidsschoenen', stockTrackingEnabled: true, variantsEnabled: true, variantCount: 4, totalAvailable: 2, lowStockThreshold: 3, lowStock: true, unit: 'paar' }),
      makeTemplate({ id: 't-2', name: 'Toegangsbadge' }),
    ]
    renderPage()

    await waitFor(() => expect(screen.getByText('Veiligheidsschoenen')).toBeInTheDocument())
    const shoeRow = screen.getByRole('link', { name: 'Veiligheidsschoenen' }).closest('tr')!
    expect(shoeRow).toHaveTextContent('2 paar')
    expect(shoeRow).toHaveTextContent('Lage voorraad')
    expect(shoeRow).toHaveTextContent('4') // variant count
    // Non-stock template shows no availability.
    const badgeRow = screen.getByRole('link', { name: 'Toegangsbadge' }).closest('tr')!
    expect(badgeRow).toHaveTextContent('Nee')
  })

  it('filters on low stock', async () => {
    templates.value = [
      makeTemplate({ id: 't-1', name: 'Veiligheidsschoenen', stockTrackingEnabled: true, totalAvailable: 1, lowStockThreshold: 3, lowStock: true }),
      makeTemplate({ id: 't-2', name: 'Toegangsbadge', stockTrackingEnabled: true, totalAvailable: 50, lowStock: false }),
    ]
    renderPage()
    await waitFor(() => expect(screen.getByText('Toegangsbadge')).toBeInTheDocument())

    await userEvent.selectOptions(screen.getByLabelText('Voorraad'), 'low')

    expect(screen.getByText('Veiligheidsschoenen')).toBeInTheDocument()
    expect(screen.queryByText('Toegangsbadge')).not.toBeInTheDocument()
  })

  it('links a template to its detail page', async () => {
    templates.value = [makeTemplate({ id: 't-9', name: 'Scanner' })]
    renderPage()
    await waitFor(() => expect(screen.getByText('Scanner')).toBeInTheDocument())
    expect(screen.getByRole('link', { name: 'Scanner' })).toHaveAttribute('href', '/settings/issued-item-templates/t-9')
  })
})
