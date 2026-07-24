import { describe, expect, it, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { IssuedItemTemplateDetailPage } from '../pages/IssuedItemTemplateDetailPage'
import type { IssuedItemTemplateDetail } from '../inventoryApi'

vi.mock('../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }),
}))

vi.mock('../../auth/authContextValue', () => ({
  useAuth: () => ({ hasPermission: () => true }),
}))

const detail = vi.hoisted(() => ({ value: null as unknown as IssuedItemTemplateDetail }))
const generateSpy = vi.hoisted(() => vi.fn())

vi.mock('../inventoryApi', async () => {
  const actual = await vi.importActual<typeof import('../inventoryApi')>('../inventoryApi')
  return {
    ...actual,
    getTemplateDetail: () => Promise.resolve(detail.value),
    listAttributeDefinitions: () => Promise.resolve([]),
    listStockMovements: () => Promise.resolve([]),
    listCurrentHolders: () => Promise.resolve([]),
    generateVariants: generateSpy.mockImplementation(() => Promise.resolve(detail.value)),
  }
})

function makeDetail(overrides?: Partial<IssuedItemTemplateDetail['template']>): IssuedItemTemplateDetail {
  return {
    template: {
      id: 't-1',
      name: 'Werkbroek',
      category: 'Kleding',
      categoryId: 'cat-1',
      applicableJobFunctionCodes: null,
      defaultQuantity: 1,
      requiresSerialNumber: false,
      requiresReceivedDate: true,
      returnRequired: true,
      isActive: true,
      sortOrder: 0,
      description: null,
      unit: 'stuks',
      notes: null,
      stockTrackingEnabled: true,
      variantsEnabled: true,
      allowNegativeStock: false,
      lowStockThreshold: 3,
      minimumStock: null,
      storageLocation: null,
      currentStock: 0,
      totalAvailable: 14,
      lowStock: false,
      variantCount: 2,
      ...overrides,
    },
    attributes: [
      {
        id: 'attr-maat',
        name: 'Maat',
        allowCustomValues: false,
        isShared: true,
        sortOrder: 0,
        isActive: true,
        options: [
          { id: 'opt-46', value: '46', sortOrder: 0, isActive: true },
          { id: 'opt-48', value: '48', sortOrder: 1, isActive: true },
        ],
      },
    ],
    variants: [
      {
        id: 'v-1',
        label: '46',
        currentStock: 10,
        isActive: true,
        sortOrder: 0,
        values: [{ attributeDefinitionId: 'attr-maat', attributeName: 'Maat', attributeOptionId: 'opt-46', value: '46' }],
      },
      {
        id: 'v-2',
        label: '48',
        currentStock: 4,
        isActive: true,
        sortOrder: 1,
        values: [{ attributeDefinitionId: 'attr-maat', attributeName: 'Maat', attributeOptionId: 'opt-48', value: '48' }],
      },
    ],
  }
}

function renderPage(initialEntry = '/settings/issued-item-templates/t-1') {
  return render(
    <MemoryRouter initialEntries={[initialEntry]}>
      <Routes>
        <Route path="/settings/issued-item-templates/:id" element={<IssuedItemTemplateDetailPage />} />
      </Routes>
    </MemoryRouter>,
  )
}

describe('IssuedItemTemplateDetailPage tabs', () => {
  beforeEach(() => {
    detail.value = makeDetail()
    generateSpy.mockClear()
  })

  it('shows tabs and opens the variants tab via deep link', async () => {
    renderPage('/settings/issued-item-templates/t-1?tab=varianten')

    await waitFor(() => expect(screen.getByRole('tab', { name: /Varianten & voorraad/ })).toBeInTheDocument())
    expect(screen.getByRole('tab', { name: /Varianten & voorraad/ })).toHaveAttribute('aria-selected', 'true')
    expect(screen.getByText(/totale voorraad: 14 \(berekend\)/)).toBeInTheDocument()
    // Variant rows show label + per-variant stock ("46" also exists as an option chip).
    expect(screen.getAllByText('46').length).toBeGreaterThan(0)
    expect(screen.getByText('10')).toBeInTheDocument()
    expect(screen.getByText('4')).toBeInTheDocument()
  })

  it('generates variants from selected attribute values', async () => {
    renderPage('/settings/issued-item-templates/t-1?tab=varianten')
    await waitFor(() => expect(screen.getByRole('button', { name: 'Varianten genereren' })).toBeInTheDocument())

    await userEvent.click(screen.getByRole('button', { name: 'Varianten genereren' }))
    await userEvent.click(screen.getByRole('checkbox', { name: '46' }))
    await userEvent.click(screen.getByRole('checkbox', { name: '48' }))
    await userEvent.click(screen.getByRole('button', { name: 'Genereren' }))

    await waitFor(() =>
      expect(generateSpy).toHaveBeenCalledWith('t-1', [
        { attributeDefinitionId: 'attr-maat', optionIds: ['opt-46', 'opt-48'] },
      ]),
    )
  })

  it('hides the variants tab and shows a Voorraad tab for non-variant templates', async () => {
    detail.value = makeDetail({ variantsEnabled: false, currentStock: 25, totalAvailable: 25 })
    renderPage()

    await waitFor(() => expect(screen.getByRole('tab', { name: 'Voorraad' })).toBeInTheDocument())
    expect(screen.queryByRole('tab', { name: /Varianten/ })).not.toBeInTheDocument()

    await userEvent.click(screen.getByRole('tab', { name: 'Voorraad' }))
    expect(screen.getByText(/25 stuks beschikbaar/)).toBeInTheDocument()
    expect(screen.getByText('Lage-voorraadgrens: 3')).toBeInTheDocument()
  })
})
