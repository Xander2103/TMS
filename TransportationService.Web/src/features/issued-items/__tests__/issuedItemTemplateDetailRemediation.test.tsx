import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { ApiError } from '../../../api/apiClient'
import { IssuedItemTemplateDetailPage } from '../pages/IssuedItemTemplateDetailPage'
import type { IssuedItemTemplateDetail } from '../inventoryApi'

// Warehouse remediation on the item page: WH-M10 (403 vs 404), WH-M3 (stale correction refused
// with "De voorraad is intussen gewijzigd"), WH-M8 (whole numbers), WH-B1 (variant with history →
// Deactiveren), WH-M4 (history shows who, serial and to/from).

vi.mock('../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }),
}))
vi.mock('../../auth/authContextValue', () => ({
  useAuth: () => ({ hasPermission: () => true }),
}))

const api = vi.hoisted(() => ({
  detail: vi.fn(),
  correct: vi.fn(),
  receive: vi.fn(),
  deleteVariant: vi.fn(),
  updateVariant: vi.fn(),
  movements: vi.fn(),
}))
vi.mock('../inventoryApi', async () => {
  const actual = await vi.importActual<typeof import('../inventoryApi')>('../inventoryApi')
  return {
    ...actual,
    getTemplateDetail: (...args: unknown[]) => api.detail(...args),
    listAttributeDefinitions: () => Promise.resolve([]),
    listStockMovements: (...args: unknown[]) => api.movements(...args),
    listCurrentHolders: () => Promise.resolve([]),
    correctStock: (...args: unknown[]) => api.correct(...args),
    receiveStock: (...args: unknown[]) => api.receive(...args),
    deleteVariant: (...args: unknown[]) => api.deleteVariant(...args),
    updateVariant: (...args: unknown[]) => api.updateVariant(...args),
  }
})

function makeDetail(stock: number, variants: IssuedItemTemplateDetail['variants'] = []): IssuedItemTemplateDetail {
  return {
    template: {
      id: 't-1', code: 'BM-0003', name: 'Helm', category: 'PBM', categoryId: null, applicableJobFunctionCodes: null,
      defaultQuantity: 1, requiresSerialNumber: false, requiresReceivedDate: true, returnRequired: false,
      isActive: true, sortOrder: 0, description: null, unit: 'piece', notes: null,
      stockTrackingEnabled: true, variantsEnabled: variants.length > 0, allowNegativeStock: false,
      lowStockThreshold: null, minimumStock: null, targetStockLevel: null, reorderQuantity: null,
      negativeStockRequiresReason: true, stockStatus: 'Normal', storageLocation: null,
      currentStock: stock, totalAvailable: stock, lowStock: false, variantCount: variants.length, isConsumable: false,
    },
    attributes: [],
    variants,
  }
}

function renderPage(entry = '/issued-items/templates/t-1') {
  return render(
    <MemoryRouter initialEntries={[entry]}>
      <Routes>
        <Route path="/issued-items/templates/:id" element={<IssuedItemTemplateDetailPage />} />
      </Routes>
    </MemoryRouter>,
  )
}

describe('IssuedItemTemplateDetailPage — warehouse remediation', () => {
  beforeEach(() => {
    Object.values(api).forEach((fn) => fn.mockReset())
    api.movements.mockResolvedValue([])
  })

  it('says "Geen toegang" for a 403 and "niet gevonden" for a 404 — not one generic error', async () => {
    api.detail.mockRejectedValue(new ApiError('Forbidden', 403, {}))
    const { unmount } = renderPage()
    expect(await screen.findByText(/Geen toegang/)).toBeInTheDocument()
    unmount()

    api.detail.mockRejectedValue(new ApiError('Not found', 404, {}))
    renderPage('/issued-items/templates/onbekend')
    expect(await screen.findByText('Bedrijfsmiddel niet gevonden.')).toBeInTheDocument()
  })

  it('shows the item code next to the name', async () => {
    api.detail.mockResolvedValue(makeDetail(16))
    renderPage()
    expect(await screen.findByText(/BM-0003/)).toBeInTheDocument()
  })

  it('a stale correction is refused with "De voorraad is intussen gewijzigd" and the fresh stock is loaded', async () => {
    api.detail.mockResolvedValueOnce(makeDetail(16)).mockResolvedValue(makeDetail(15))
    api.correct.mockRejectedValue(
      new ApiError('De voorraad is intussen gewijzigd. Controleer opnieuw.', 409, {
        message: 'De voorraad is intussen gewijzigd. Controleer opnieuw.',
        code: 'common.concurrency_conflict',
      }),
    )
    renderPage('/issued-items/templates/t-1?tab=voorraad')

    await userEvent.click(await screen.findByRole('button', { name: 'Corrigeren' }))
    const dialog = await screen.findByRole('dialog', { name: 'Voorraad corrigeren' })
    await userEvent.type(within(dialog).getByLabelText(/Reden/), 'telling')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Opslaan' }))

    expect(api.correct).toHaveBeenCalledWith('t-1', expect.objectContaining({ newQuantity: 16, expectedCurrentStock: 16 }))
    expect(await within(dialog).findByText('De voorraad is intussen gewijzigd. Controleer opnieuw.')).toBeInTheDocument()
    await waitFor(() => expect(within(dialog).getByLabelText(/Nieuwe voorraad/)).toHaveValue(15))
  })

  it('a massive or decimal receipt is refused before it is sent', async () => {
    api.detail.mockResolvedValue(makeDetail(25))
    renderPage('/issued-items/templates/t-1?tab=voorraad')

    await userEvent.click(await screen.findByRole('button', { name: 'Voorraad toevoegen' }))
    const dialog = await screen.findByRole('dialog')
    const quantity = within(dialog).getByRole('spinbutton')
    await userEvent.type(quantity, '2147483647')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Opslaan' }))
    expect(await within(dialog).findByText('Het aantal is te groot: maximaal 100.000.')).toBeInTheDocument()

    await userEvent.clear(quantity)
    await userEvent.type(quantity, '2.5')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Opslaan' }))
    expect(await within(dialog).findByText('Geef een geheel aantal in (geen decimalen).')).toBeInTheDocument()
    expect(api.receive).not.toHaveBeenCalled()
  })

  it('a variant with history cannot be deleted: the 409 offers Deactiveren', async () => {
    const variant = { id: 'v-1', label: 'M', currentStock: 0, isActive: true, sortOrder: 0, values: [], lowStockThreshold: null }
    api.detail.mockResolvedValue(makeDetail(0, [variant]))
    api.deleteVariant.mockRejectedValue(
      new ApiError('Deze variant kan niet worden verwijderd…', 409, {
        code: 'common.record_referenced',
        message: 'Deze variant kan niet worden verwijderd omdat er historische of operationele gegevens aan gekoppeld zijn: 3 voorraadbewegingen.',
        references: [{ key: 'movements', count: 3, label: '3 voorraadbewegingen' }],
        canDeactivate: true,
      }),
    )
    api.updateVariant.mockResolvedValue({ ...variant, isActive: false })
    renderPage('/issued-items/templates/t-1?tab=varianten')

    await userEvent.click(await screen.findByRole('button', { name: 'Verwijderen' }))
    await userEvent.click(within(await screen.findByRole('dialog', { name: 'Variant verwijderen' })).getByRole('button', { name: 'Verwijderen' }))
    const conflict = await screen.findByRole('dialog', { name: 'Variant verwijderen' })
    expect(within(conflict).getByText('3 voorraadbewegingen')).toBeInTheDocument()

    await userEvent.click(within(conflict).getByRole('button', { name: 'Deactiveren' }))
    await waitFor(() => expect(api.updateVariant).toHaveBeenCalledWith('t-1', 'v-1', expect.objectContaining({ isActive: false, label: 'M' })))
  })

  it('history shows who booked it, the serial and to/from whom', async () => {
    api.detail.mockResolvedValue(makeDetail(4))
    api.movements.mockResolvedValue([
      {
        id: 'm-1', templateId: 't-1', variantId: null, variantLabel: null, movementType: 'Issue', quantity: -1, resultingStock: 4,
        reason: null, notes: null, employeeId: 'e-1', employeeName: 'Anna Aerts', performedByUserId: 'u-1',
        timestamp: '2026-10-18T09:00:00Z', performedByName: 'Mia Magazijn', employeeNumber: 'MED-0009', serialNumber: 'SER-GOLD-001',
      },
    ])
    renderPage('/issued-items/templates/t-1?tab=bewegingen')

    expect(await screen.findByText('aan Anna Aerts (MED-0009)')).toBeInTheDocument()
    expect(screen.getByText('Mia Magazijn')).toBeInTheDocument()
    expect(screen.getByText('SER-GOLD-001')).toBeInTheDocument()
  })
})
