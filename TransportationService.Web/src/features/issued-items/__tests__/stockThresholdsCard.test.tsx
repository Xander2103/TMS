import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { StockThresholdsCard } from '../components/StockThresholdsCard'
import type { IssuedItemTemplate } from '../issuedItemsApi'

const auth = vi.hoisted(() => ({ permissions: [] as string[] }))
const updateSpy = vi.hoisted(() => vi.fn())

vi.mock('../../auth/authContextValue', () => ({
  useAuth: () => ({ hasPermission: (code: string) => auth.permissions.includes(code) }),
}))

vi.mock('../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }),
}))

vi.mock('../inventoryApi', async () => {
  const actual = await vi.importActual<typeof import('../inventoryApi')>('../inventoryApi')
  return {
    ...actual,
    updateStockThresholds: updateSpy,
  }
})

function makeTemplate(overrides?: Partial<IssuedItemTemplate>): IssuedItemTemplate {
  return {
    id: 't-1',
    name: 'Werkbroek',
    category: 'Kleding',
    categoryId: null,
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
    variantsEnabled: false,
    allowNegativeStock: false,
    lowStockThreshold: 5,
    minimumStock: 2,
    targetStockLevel: null,
    reorderQuantity: null,
    negativeStockRequiresReason: false,
    stockStatus: 'Normal',
    storageLocation: null,
    currentStock: 10,
    totalAvailable: 10,
    lowStock: false,
    variantCount: 0,
    ...overrides,
  }
}

describe('StockThresholdsCard', () => {
  afterEach(cleanup)

  it('submits the thresholds payload for an authorized user', async () => {
    auth.permissions = ['inventory.manage_thresholds']
    updateSpy.mockReset()
    updateSpy.mockResolvedValue(makeTemplate())
    const onSaved = vi.fn()
    render(<StockThresholdsCard template={makeTemplate()} onSaved={onSaved} />)

    // Existing values prefill the form.
    expect(screen.getByLabelText(/Waarschuwingsgrens/)).toHaveValue(5)

    await userEvent.clear(screen.getByLabelText(/Waarschuwingsgrens/))
    await userEvent.type(screen.getByLabelText(/Waarschuwingsgrens/), '8')
    await userEvent.type(screen.getByLabelText(/Doelvoorraad/), '25')
    await userEvent.type(screen.getByLabelText(/Bestelhoeveelheid/), '12')
    await userEvent.click(screen.getByLabelText('Negatieve voorraad toestaan'))
    await userEvent.click(screen.getByLabelText('Reden verplicht bij negatieve voorraad'))
    await userEvent.click(screen.getByRole('button', { name: 'Voorraadregels opslaan' }))

    await waitFor(() => expect(updateSpy).toHaveBeenCalledTimes(1))
    expect(updateSpy).toHaveBeenCalledWith('t-1', {
      lowStockThreshold: 8,
      minimumStock: 2,
      targetStockLevel: 25,
      reorderQuantity: 12,
      allowNegativeStock: true,
      negativeStockRequiresReason: true,
    })
    expect(onSaved).toHaveBeenCalled()
  })

  it('blocks a minimum above the warning threshold client-side', async () => {
    auth.permissions = ['issued_items.manage_templates']
    updateSpy.mockReset()
    render(<StockThresholdsCard template={makeTemplate()} onSaved={vi.fn()} />)

    await userEvent.clear(screen.getByLabelText(/Minimumniveau/))
    await userEvent.type(screen.getByLabelText(/Minimumniveau/), '9')
    await userEvent.click(screen.getByRole('button', { name: 'Voorraadregels opslaan' }))

    expect(await screen.findByRole('alert')).toHaveTextContent(/minimumniveau mag niet hoger/i)
    expect(updateSpy).not.toHaveBeenCalled()
  })

  it('renders nothing without a thresholds permission', () => {
    auth.permissions = ['inventory.view']
    const { container } = render(<StockThresholdsCard template={makeTemplate()} onSaved={vi.fn()} />)

    expect(container).toBeEmptyDOMElement()
    expect(screen.queryByText('Voorraadregels')).not.toBeInTheDocument()
  })
})
