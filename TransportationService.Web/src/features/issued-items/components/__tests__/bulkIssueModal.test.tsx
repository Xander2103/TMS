import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { BulkIssueModal } from '../BulkIssueModal'
import { ApiError } from '../../../../api/apiClient'
import type { IssuedItemTemplate } from '../../issuedItemsApi'

const saveSpy = vi.hoisted(() => ({ fn: vi.fn() }))

vi.mock('../../issuedItemsApi', async () => {
  const actual = await vi.importActual<typeof import('../../issuedItemsApi')>('../../issuedItemsApi')
  return {
    ...actual,
    saveEmployeeIssuedItem: (...args: unknown[]) => {
      const result = saveSpy.fn(...args) as Promise<unknown> | undefined
      return result ?? Promise.resolve({})
    },
  }
})

vi.mock('../../inventoryApi', async () => {
  const actual = await vi.importActual<typeof import('../../inventoryApi')>('../../inventoryApi')
  return {
    ...actual,
    getTemplateDetail: () =>
      Promise.resolve({
        template: {} as IssuedItemTemplate,
        attributes: [],
        variants: [
          { id: 'v-m', label: 'M', currentStock: 3, isActive: true, sortOrder: 0, values: [] },
          { id: 'v-l', label: 'L', currentStock: 0, isActive: true, sortOrder: 1, values: [] },
        ],
      }),
  }
})

function makeTemplate(overrides: Partial<IssuedItemTemplate>): IssuedItemTemplate {
  return {
    id: 't-1',
    name: 'Werkschoenen',
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

describe('BulkIssueModal', () => {
  afterEach(cleanup)

  it('issues two save calls sharing the same issuedDate/notes when two templates are selected', async () => {
    saveSpy.fn.mockReset()
    saveSpy.fn.mockResolvedValue({})
    const templates = [
      makeTemplate({ id: 't-1', name: 'Werkschoenen', category: 'PBM' }),
      makeTemplate({ id: 't-2', name: 'Helm', category: 'PBM' }),
    ]
    const onCompleted = vi.fn()
    render(
      <BulkIssueModal
        employeeId="emp-1"
        templates={templates}
        canOverrideStock={false}
        onClose={vi.fn()}
        onItemIssued={vi.fn()}
        onCompleted={onCompleted}
      />,
    )

    await userEvent.click(screen.getByLabelText('Werkschoenen'))
    await userEvent.click(screen.getByLabelText('Helm'))
    await userEvent.type(screen.getByLabelText(/Opmerking/i), 'Startpakket')

    await userEvent.click(screen.getByRole('button', { name: /^Uitgeven \(2\)/ }))

    await waitFor(() => expect(saveSpy.fn).toHaveBeenCalledTimes(2))
    const [empA, idA, payloadA] = saveSpy.fn.mock.calls[0] as [string, string | null, { issuedDate: string; notes: string }]
    const [, , payloadB] = saveSpy.fn.mock.calls[1] as [string, string | null, { issuedDate: string; notes: string }]
    expect(empA).toBe('emp-1')
    expect(idA).toBeNull()
    expect(payloadA.issuedDate).toBe(payloadB.issuedDate)
    expect(payloadA.notes).toBe('Startpakket')
    expect(payloadB.notes).toBe('Startpakket')
    await waitFor(() => expect(onCompleted).toHaveBeenCalledWith('2 middelen uitgegeven'))
  })

  it('requires a variant before submitting a variant-enabled template', async () => {
    saveSpy.fn.mockReset()
    saveSpy.fn.mockResolvedValue({})
    const templates = [
      makeTemplate({ id: 't-3', name: 'Veiligheidsschoenen', category: 'PBM', variantsEnabled: true, stockTrackingEnabled: true }),
    ]
    render(
      <BulkIssueModal
        employeeId="emp-2"
        templates={templates}
        canOverrideStock={false}
        onClose={vi.fn()}
        onItemIssued={vi.fn()}
        onCompleted={vi.fn()}
      />,
    )

    await userEvent.click(screen.getByLabelText('Veiligheidsschoenen'))
    // Variant select appears once checked, with stock counts loaded lazily.
    const variantSelect = await screen.findByLabelText(/Variant/i)
    expect(variantSelect).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: /^Uitgeven \(1\)/ }))
    expect(await screen.findByRole('alert')).toHaveTextContent('Kies een variant.')
    expect(saveSpy.fn).not.toHaveBeenCalled()

    await userEvent.selectOptions(variantSelect, 'v-m')
    await userEvent.click(screen.getByRole('button', { name: /^Uitgeven \(1\)/ }))
    await waitFor(() => expect(saveSpy.fn).toHaveBeenCalledTimes(1))
    const payload = saveSpy.fn.mock.calls[0][2] as { variantId: string | null }
    expect(payload.variantId).toBe('v-m')
  })

  it('requires a serial number before submitting a template that mandates one', async () => {
    saveSpy.fn.mockReset()
    saveSpy.fn.mockResolvedValue({})
    const templates = [makeTemplate({ id: 't-5', name: 'PDA Zebra', category: 'IT', requiresSerialNumber: true })]
    render(
      <BulkIssueModal
        employeeId="emp-5"
        templates={templates}
        canOverrideStock={false}
        onClose={vi.fn()}
        onItemIssued={vi.fn()}
        onCompleted={vi.fn()}
      />,
    )

    await userEvent.click(screen.getByLabelText('PDA Zebra'))
    await userEvent.click(screen.getByRole('button', { name: /^Uitgeven \(1\)/ }))
    expect(await screen.findByRole('alert')).toHaveTextContent('Een serienummer is verplicht voor dit middel.')
    expect(saveSpy.fn).not.toHaveBeenCalled()

    await userEvent.type(screen.getByLabelText(/Serienummer/i), 'SN-42')
    await userEvent.click(screen.getByRole('button', { name: /^Uitgeven \(1\)/ }))
    await waitFor(() => expect(saveSpy.fn).toHaveBeenCalledTimes(1))
    const payload = saveSpy.fn.mock.calls[0][2] as { serialNumber: string | null }
    expect(payload.serialNumber).toBe('SN-42')
  })

  it('keeps the submit button disabled across the whole batch, including while a negative-stock confirmation is pending', async () => {
    saveSpy.fn.mockReset()
    let resolveSecondSave: (() => void) | undefined
    saveSpy.fn
      .mockRejectedValueOnce(
        new ApiError('Bevestiging vereist', 409, {
          code: 'negative_stock_confirmation_required',
          templateId: 't-1',
          variantId: null,
          itemName: 'Werkschoenen',
          variantLabel: null,
          currentStock: 1,
          requestedDelta: -2,
          projectedStock: -1,
          version: 'ver-1',
          requiresReason: false,
          versionMismatch: false,
        }),
      )
      .mockResolvedValueOnce({}) // confirmed retry for the first template
      .mockImplementationOnce(
        () =>
          new Promise((resolve) => {
            resolveSecondSave = () => resolve({})
          }),
      ) // second template's save stays pending until we resolve it manually

    const templates = [
      makeTemplate({ id: 't-1', name: 'Werkschoenen', category: 'PBM', allowNegativeStock: true }),
      makeTemplate({ id: 't-2', name: 'Helm', category: 'PBM' }),
    ]
    const onCompleted = vi.fn()
    render(
      <BulkIssueModal
        employeeId="emp-4"
        templates={templates}
        canOverrideStock
        onClose={vi.fn()}
        onItemIssued={vi.fn()}
        onCompleted={onCompleted}
      />,
    )

    await userEvent.click(screen.getByLabelText('Werkschoenen'))
    await userEvent.click(screen.getByLabelText('Helm'))

    const submitButton = screen.getByRole('button', { name: /^Uitgeven \(2\)/ })
    await userEvent.click(submitButton)

    // The negative-stock modal opens for the first item; the batch button stays disabled while it waits.
    await screen.findByText('Deze uitgifte brengt de voorraad onder nul.')
    expect(submitButton).toBeDisabled()

    await userEvent.click(screen.getByRole('button', { name: 'Bevestig negatieve voorraad' }))

    // Regression: previously the confirm handler's own `finally { setBusy(false) }` re-enabled the
    // button here, before the loop resumed onto the second (Helm) item — opening a double-submit
    // window. It must stay disabled while that second save is still in flight.
    await waitFor(() => expect(saveSpy.fn).toHaveBeenCalledTimes(3))
    expect(submitButton).toBeDisabled()

    // A click while still disabled must not fire another batch.
    await userEvent.click(submitButton)
    expect(saveSpy.fn).toHaveBeenCalledTimes(3)

    resolveSecondSave?.()
    await waitFor(() => expect(onCompleted).toHaveBeenCalledWith('2 middelen uitgegeven'))

    // Each template was issued exactly once: the initial 409 for t-1 doesn't count as a duplicate issue.
    const templateIds = saveSpy.fn.mock.calls.map((call) => (call[2] as { templateId: string }).templateId)
    expect(templateIds).toEqual(['t-1', 't-1', 't-2'])
  })

  it('groups templates by category', () => {
    const templates = [
      makeTemplate({ id: 't-1', name: 'Werkschoenen', category: 'PBM' }),
      makeTemplate({ id: 't-4', name: 'Laptop', category: 'IT' }),
    ]
    render(
      <BulkIssueModal
        employeeId="emp-3"
        templates={templates}
        canOverrideStock={false}
        onClose={vi.fn()}
        onItemIssued={vi.fn()}
        onCompleted={vi.fn()}
      />,
    )

    expect(screen.getByText('PBM')).toBeInTheDocument()
    expect(screen.getByText('IT')).toBeInTheDocument()
    const pbmGroup = screen.getByText('PBM').closest('fieldset')!
    const itGroup = screen.getByText('IT').closest('fieldset')!
    expect(within(pbmGroup).getByText('Werkschoenen')).toBeInTheDocument()
    expect(within(pbmGroup).queryByText('Laptop')).not.toBeInTheDocument()
    expect(within(itGroup).getByText('Laptop')).toBeInTheDocument()
  })
})
