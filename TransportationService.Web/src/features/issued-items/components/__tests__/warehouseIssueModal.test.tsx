import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ApiError } from '../../../../api/apiClient'
import { WarehouseIssueModal } from '../WarehouseIssueModal'
import type { IssuedItemTemplate } from '../../issuedItemsApi'

// WH-H4: "Uitgeven" from the warehouse with the safe employee picker; WH-M2/M8 client validation;
// the server's Dutch refusal (second holder of a serial) is shown in the form.

const api = vi.hoisted(() => ({ issue: vi.fn(), employees: vi.fn(), templates: vi.fn() }))
vi.mock('../../inventoryApi', async () => {
  const actual = await vi.importActual<typeof import('../../inventoryApi')>('../../inventoryApi')
  return {
    ...actual,
    issueFromWarehouse: (...args: unknown[]) => api.issue(...args),
    searchInventoryEmployees: (...args: unknown[]) => api.employees(...args),
  }
})
vi.mock('../../issuedItemsApi', async () => {
  const actual = await vi.importActual<typeof import('../../issuedItemsApi')>('../../issuedItemsApi')
  return { ...actual, listIssuedItemTemplates: () => api.templates() }
})

function template(overrides: Partial<IssuedItemTemplate>): IssuedItemTemplate {
  return {
    id: 't-1', code: 'BM-0001', name: 'TEST WH Golden Scanner', category: 'Magazijn', categoryId: null,
    applicableJobFunctionCodes: null, defaultQuantity: 1, requiresSerialNumber: true, requiresReceivedDate: true,
    returnRequired: true, isActive: true, sortOrder: 0, description: null, unit: 'piece', notes: null,
    stockTrackingEnabled: true, variantsEnabled: false, allowNegativeStock: false, lowStockThreshold: null,
    minimumStock: null, targetStockLevel: null, reorderQuantity: null, negativeStockRequiresReason: true,
    stockStatus: 'Normal', storageLocation: null, currentStock: 5, totalAvailable: 5, lowStock: false, variantCount: 0,
    isConsumable: false,
    ...overrides,
  }
}

async function pick(label: RegExp, option: RegExp) {
  await userEvent.click(screen.getByLabelText(label))
  await userEvent.click(await screen.findByRole('option', { name: option }))
}

describe('WarehouseIssueModal', () => {
  beforeEach(() => {
    api.issue.mockReset()
    api.employees.mockResolvedValue([{ id: 'e-1', employeeNumber: 'MED-0009', name: 'Anna Aerts', isActive: true }])
    api.templates.mockResolvedValue([
      template({}),
      template({ id: 't-2', code: 'BM-0002', name: 'TEST WH Gloves', requiresSerialNumber: false, returnRequired: false, isConsumable: true, defaultQuantity: 1 }),
    ])
  })

  it('refuses to send without employee and item', async () => {
    render(<WarehouseIssueModal canOverrideStock={false} onClose={vi.fn()} onIssued={vi.fn()} />)
    await userEvent.click(screen.getByRole('button', { name: 'Uitgeven' }))
    expect(screen.getByText('Kies een medewerker.')).toBeInTheDocument()
    expect(screen.getByText('Kies een artikel.')).toBeInTheDocument()
    expect(api.issue).not.toHaveBeenCalled()
  })

  it('a serialized item needs a serial and is one piece; sends the full issue', async () => {
    const onIssued = vi.fn()
    api.issue.mockResolvedValue({})
    render(<WarehouseIssueModal canOverrideStock={false} onClose={vi.fn()} onIssued={onIssued} />)
    await pick(/Medewerker/, /Anna Aerts \(MED-0009\)/)
    await pick(/^Artikel/, /BM-0001 · TEST WH Golden Scanner/)

    expect(screen.getByLabelText(/^Aantal/)).toBeDisabled()
    await userEvent.click(screen.getByRole('button', { name: 'Uitgeven' }))
    expect(screen.getByText(/serienummer is verplicht/i)).toBeInTheDocument()

    await userEvent.type(screen.getByLabelText(/Serienummer/), 'SER-GOLD-001')
    await userEvent.click(screen.getByRole('button', { name: 'Uitgeven' }))
    await waitFor(() =>
      expect(api.issue).toHaveBeenCalledWith(expect.objectContaining({
        employeeId: 'e-1', templateId: 't-1', quantity: 1, serialNumber: 'SER-GOLD-001',
      })),
    )
    expect(onIssued).toHaveBeenCalledWith('BM-0001 · TEST WH Golden Scanner uitgegeven aan Anna Aerts (MED-0009).')
  })

  it('refuses 0 and 1.5 with Dutch messages (never coerced to 1)', async () => {
    render(<WarehouseIssueModal canOverrideStock={false} onClose={vi.fn()} onIssued={vi.fn()} />)
    await pick(/Medewerker/, /Anna Aerts/)
    await pick(/^Artikel/, /TEST WH Gloves/)
    expect(screen.getByText(/Verbruiksartikel: wordt definitief uitgegeven/)).toBeInTheDocument()

    const quantity = screen.getByLabelText(/^Aantal/)
    await userEvent.clear(quantity)
    await userEvent.type(quantity, '0')
    await userEvent.click(screen.getByRole('button', { name: 'Uitgeven' }))
    expect(screen.getByText('Het aantal moet minstens 1 zijn.')).toBeInTheDocument()

    await userEvent.clear(quantity)
    await userEvent.type(quantity, '1.5')
    await userEvent.click(screen.getByRole('button', { name: 'Uitgeven' }))
    expect(screen.getByText('Geef een geheel aantal in (geen decimalen).')).toBeInTheDocument()
    expect(api.issue).not.toHaveBeenCalled()
  })

  it('shows the server refusal for a serial that already has a holder', async () => {
    const message = 'Serienummer SER-GOLD-001 is al uitgereikt aan Anna Aerts (MED-0009). Eén serienummer kan maar één houder hebben: verwerk eerst de retour.'
    api.issue.mockRejectedValue(new ApiError(message, 400, { detail: message, code: 'common.validation_failed' }))
    render(<WarehouseIssueModal canOverrideStock={false} onClose={vi.fn()} onIssued={vi.fn()} />)
    await pick(/Medewerker/, /Anna Aerts/)
    await pick(/^Artikel/, /Golden Scanner/)
    await userEvent.type(screen.getByLabelText(/Serienummer/), 'ser-gold-001')
    await userEvent.click(screen.getByRole('button', { name: 'Uitgeven' }))

    expect(await screen.findByText(message)).toBeInTheDocument()
  })
})
