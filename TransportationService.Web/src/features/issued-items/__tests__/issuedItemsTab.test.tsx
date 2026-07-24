import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { IssuedItemsTab } from '../IssuedItemsTab'
import type { EmployeeIssuedItem, IssuedItemTemplate } from '../issuedItemsApi'

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
const templates = vi.hoisted(() => ({ value: [] as IssuedItemTemplate[] }))
const saveSpy = vi.hoisted(() => ({ fn: vi.fn() }))

vi.mock('../issuedItemsApi', async () => {
  const actual = await vi.importActual<typeof import('../issuedItemsApi')>('../issuedItemsApi')
  return {
    ...actual,
    listEmployeeIssuedItems: () => Promise.resolve(items.value),
    listIssuedItemTemplates: () => Promise.resolve(templates.value),
    saveEmployeeIssuedItem: (...args: unknown[]) => {
      saveSpy.fn(...args)
      return Promise.resolve(items.value[0])
    },
  }
})

vi.mock('../inventoryApi', async () => {
  const actual = await vi.importActual<typeof import('../inventoryApi')>('../inventoryApi')
  return {
    ...actual,
    getTemplateDetail: (templateId: string) =>
      Promise.resolve({
        template: templates.value.find((t) => t.id === templateId)!,
        attributes: [],
        variants: [
          { id: 'v-m', label: 'M', currentStock: 3, isActive: true, sortOrder: 0, values: [] },
          { id: 'v-l', label: 'L', currentStock: 0, isActive: true, sortOrder: 1, values: [] },
        ],
      }),
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
    variantId: null,
    variantLabel: null,
    ...overrides,
  }
}

function makeTemplate(overrides: Partial<IssuedItemTemplate>): IssuedItemTemplate {
  return {
    id: 't-1',
    name: 'T-shirt',
    category: 'PBM',
    categoryId: null,
    applicableJobFunctionCodes: null,
    defaultQuantity: 1,
    requiresSerialNumber: false,
    requiresReceivedDate: true,
    returnRequired: true,
    isActive: true,
    sortOrder: 0,
    description: null,
    unit: null,
    notes: null,
    stockTrackingEnabled: true,
    variantsEnabled: true,
    allowNegativeStock: false,
    lowStockThreshold: null,
    minimumStock: null,
    storageLocation: null,
    currentStock: 0,
    totalAvailable: 3,
    lowStock: false,
    variantCount: 2,
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
    templates.value = []
    render(<IssuedItemsTab employeeId="emp-2" />)

    await waitFor(() => expect(screen.getByText('Bedrijfsmiddel toevoegen')).toBeInTheDocument())
  })

  it('requires a variant for a variant-enabled template and shows stock preview', async () => {
    auth.permissions = ['issued_items.view', 'issued_items.manage']
    items.value = []
    templates.value = [makeTemplate({})]
    saveSpy.fn.mockClear()
    render(<IssuedItemsTab employeeId="emp-3" />)

    await waitFor(() => expect(screen.getByText('Bedrijfsmiddel toevoegen')).toBeInTheDocument())
    await userEvent.click(screen.getByText('Bedrijfsmiddel toevoegen'))
    await userEvent.selectOptions(screen.getByLabelText(/Uit sjabloon/i), 't-1')

    // Variant list loads with stock counts.
    const variantSelect = await screen.findByLabelText(/Variant \(maat\/uitvoering\)/i)
    expect(variantSelect).toBeInTheDocument()

    // Submitting without a variant is blocked client-side.
    await userEvent.click(screen.getByRole('button', { name: /^Opslaan/ }))
    expect(await screen.findByRole('alert')).toHaveTextContent(/kies een variant/i)
    expect(saveSpy.fn).not.toHaveBeenCalled()

    // Choosing a variant shows its available stock and lets the save proceed.
    await userEvent.selectOptions(variantSelect, 'v-m')
    expect(screen.getByRole('status')).toHaveTextContent(/beschikbare voorraad: 3/i)
    await userEvent.click(screen.getByRole('button', { name: /^Opslaan/ }))
    await waitFor(() => expect(saveSpy.fn).toHaveBeenCalled())
    const payload = saveSpy.fn.mock.calls[0][2] as { variantId: string | null }
    expect(payload.variantId).toBe('v-m')
  })

  it('offers return dispositions when returning an issued item', async () => {
    auth.permissions = ['issued_items.view', 'issued_items.manage']
    items.value = [makeItem({ status: 'Issued' })]
    saveSpy.fn.mockClear()
    render(<IssuedItemsTab employeeId="emp-4" />)

    await waitFor(() => expect(screen.getByText('Laptop')).toBeInTheDocument())
    await userEvent.click(screen.getByText('Bewerken'))
    await userEvent.selectOptions(screen.getByLabelText(/^Status/), 'Returned')

    // Disposition select with "good" restoring stock by default.
    const disposition = screen.getByLabelText(/Retourconditie/i)
    expect(disposition).toHaveValue('good')
    expect(screen.getByLabelText(/Terug in voorraad opnemen/i)).toBeChecked()

    // A damaged return hides the restore option.
    await userEvent.selectOptions(disposition, 'damaged')
    expect(screen.queryByLabelText(/Terug in voorraad opnemen/i)).not.toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: /^Opslaan/ }))
    await waitFor(() => expect(saveSpy.fn).toHaveBeenCalled())
    const payload = saveSpy.fn.mock.calls[0][2] as { returnDisposition: string | null; restoreStock: boolean | null }
    expect(payload.returnDisposition).toBe('damaged')
    expect(payload.restoreStock).toBeNull()
  })
})
