import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { TemplateFormModal } from '../TemplateFormModal'
import * as issuedItemsApi from '../issuedItemsApi'
import type { IssuedItemTemplate } from '../issuedItemsApi'

const permissions = vi.hoisted(() => ({ value: new Set<string>(['inventory.manage']) }))

vi.mock('../../auth/authContextValue', () => ({
  useAuth: () => ({ hasPermission: (code: string) => permissions.value.has(code) }),
}))

// The lookup select fetches options over HTTP; a plain select keeps this test focused on the modal.
vi.mock('../../master-data/components/LookupSelect', () => ({
  LookupSelect: ({ id, value, onChange }: { id?: string; value: string | null; onChange: (v: string | null) => void }) => (
    <select id={id} data-testid="category-select" value={value ?? ''} onChange={(e) => onChange(e.target.value || null)}>
      <option value="">—</option>
      <option value="cat-kleding">Kleding</option>
    </select>
  ),
}))

const api = vi.hoisted(() => ({ create: vi.fn(), update: vi.fn(), createVariant: vi.fn() }))

vi.mock('../inventoryApi', async (importOriginal) => {
  const original = await importOriginal<typeof import('../inventoryApi')>()
  return {
    ...original,
    createVariant: api.createVariant,
    getTemplateDetail: vi.fn(),
  }
})

vi.mock('../issuedItemsApi', async (importOriginal) => {
  const original = await importOriginal<typeof import('../issuedItemsApi')>()
  return {
    ...original,
    createIssuedItemTemplate: api.create,
    updateIssuedItemTemplate: api.update,
  }
})

const CATALOGUE_LABELS = ['Stuk', 'Paar', 'Set', 'Doos', 'Pak', 'Rol', 'Meter', 'Liter', 'Kilogram', 'Overige']

function makeTemplate(overrides: Partial<IssuedItemTemplate>): IssuedItemTemplate {
  return {
    id: 't-1',
    name: 'Handschoenen',
    category: 'PBM',
    categoryId: 'cat-kleding',
    applicableJobFunctionCodes: null,
    defaultQuantity: 1,
    requiresSerialNumber: false,
    requiresReceivedDate: true,
    returnRequired: false,
    isActive: true,
    sortOrder: 0,
    description: null,
    unit: 'pair',
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

function renderModal(editing: IssuedItemTemplate | null = null) {
  return render(
    <MemoryRouter>
      <TemplateFormModal editing={editing} onSaved={() => {}} onClose={() => {}} />
    </MemoryRouter>,
  )
}

describe('TemplateFormModal', () => {
  const fetchSpy = vi.fn()

  beforeEach(() => {
    // No network is allowed from this modal: the unit list is a fixed catalogue, not master data.
    vi.stubGlobal('fetch', fetchSpy)
    fetchSpy.mockReset()
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('renders the category dropdown with a manage link to the Bedrijfsmiddelen admin page', () => {
    permissions.value = new Set(['inventory.manage'])
    renderModal()

    expect(screen.getByTestId('category-select')).toBeInTheDocument()
    const manageLink = screen.getByRole('link', { name: '+ Categorieën beheren' })
    expect(manageLink).toHaveAttribute('href', '/issued-items/categories')
  })

  it('hides the manage link without the inventory.manage permission', () => {
    permissions.value = new Set()
    renderModal()

    expect(screen.queryByRole('link', { name: '+ Categorieën beheren' })).not.toBeInTheDocument()
  })

  it('labels sort order clearly and no longer exposes Minimumvoorraad', () => {
    permissions.value = new Set()
    renderModal()

    expect(screen.getByLabelText('Volgorde in lijst')).toBeInTheDocument()
    expect(screen.queryByLabelText('Minimumvoorraad')).not.toBeInTheDocument()
  })

  it('offers the unit as the fixed 10-option catalogue, defaulting to Stuk, without stock tracking', () => {
    permissions.value = new Set()
    renderModal()

    // Selectable straight away when creating a template — no stock toggle needed.
    const unitSelect = screen.getByLabelText('Eenheid')
    expect(unitSelect.tagName).toBe('SELECT')
    expect(unitSelect).toHaveValue('piece')
    const options = Array.from(unitSelect.querySelectorAll('option'))
    expect(options.map((o) => o.textContent)).toEqual(CATALOGUE_LABELS)
    expect(options.map((o) => o.value)).toEqual(['piece', 'pair', 'set', 'box', 'pack', 'roll', 'meter', 'liter', 'kilogram', 'other'])
    // The hint explains what the selected unit is for.
    expect(screen.getByText(/Stuk — laptop, telefoon/)).toBeInTheDocument()
  })

  it('does not depend on the unit-type master data any more', () => {
    permissions.value = new Set(['unit_types.manage', 'tariffs.manage'])
    renderModal()

    expect('listInventoryUnitOptions' in issuedItemsApi).toBe(false)
    expect(fetchSpy).not.toHaveBeenCalled()
    expect(screen.queryByRole('link', { name: '+ Eenheden beheren' })).not.toBeInTheDocument()
  })

  it('persists the chosen catalogue code, not its label', async () => {
    permissions.value = new Set()
    api.create.mockResolvedValue({ id: 't-1' })
    renderModal()

    await userEvent.type(screen.getByLabelText(/^Naam/), 'Veiligheidsschoenen')
    await userEvent.selectOptions(screen.getByTestId('category-select'), 'cat-kleding')
    await userEvent.selectOptions(screen.getByLabelText('Eenheid'), 'Paar')
    expect(screen.getByText(/Paar — schoenen, handschoenen/)).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Opslaan' }))

    expect(api.create).toHaveBeenCalledWith(expect.objectContaining({ unit: 'pair' }))
    expect(fetchSpy).not.toHaveBeenCalled()
  })

  it('preselects the stored code when editing and maps a legacy free-text unit to Overige', async () => {
    permissions.value = new Set()
    api.update.mockResolvedValue({ id: 't-1' })
    const { unmount } = renderModal(makeTemplate({ unit: 'box' }))
    expect(screen.getByLabelText('Eenheid')).toHaveValue('box')
    unmount()

    renderModal(makeTemplate({ unit: 'Paar' }))
    expect(screen.getByLabelText('Eenheid')).toHaveValue('other')
    await userEvent.click(screen.getByRole('button', { name: 'Opslaan' }))
    expect(api.update).toHaveBeenCalledWith('t-1', expect.objectContaining({ unit: 'other' }))
  })

  it('shows a real Voorraad field when stock tracking is on without variants', async () => {
    permissions.value = new Set()
    renderModal()

    await userEvent.click(screen.getByLabelText('Voorraadbeheer'))
    expect(screen.getByLabelText('Voorraad')).toBeInTheDocument()
    expect(screen.getByLabelText('Lage-voorraadgrens')).toBeInTheDocument()

    // Enabling variants replaces the field with the direct variant editor.
    await userEvent.click(screen.getByLabelText('Varianten gebruiken (maat/uitvoering) — voorraad per variant'))
    expect(screen.queryByLabelText('Voorraad', { selector: '#tpl-stock' })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: '+ Variant toevoegen' })).toBeInTheDocument()
  })

  it('creates the entered variants (with stock) right after the template', async () => {
    permissions.value = new Set()
    api.create.mockResolvedValue({ id: 't-1' })
    api.createVariant.mockResolvedValue({ id: 'v-1' })
    renderModal()

    await userEvent.click(screen.getByLabelText('Voorraadbeheer'))
    await userEvent.click(screen.getByLabelText('Varianten gebruiken (maat/uitvoering) — voorraad per variant'))

    // Explicit "+ Variant toevoegen" flow: Small 10, then Medium 15.
    await userEvent.type(screen.getByLabelText(/Variantnaam/), 'Small')
    await userEvent.type(screen.getByLabelText('Voorraad', { selector: '#tpl-variant-stock' }), '10')
    await userEvent.click(screen.getByRole('button', { name: '+ Variant toevoegen' }))
    await userEvent.type(screen.getByLabelText(/Variantnaam/), 'Medium')
    await userEvent.type(screen.getByLabelText('Voorraad', { selector: '#tpl-variant-stock' }), '15')
    await userEvent.click(screen.getByRole('button', { name: '+ Variant toevoegen' }))

    // The template total is shown as a computed, read-only sum.
    expect(screen.getByText(/Totale voorraad: 25/)).toBeInTheDocument()

    await userEvent.type(screen.getByLabelText(/^Naam/), 'Werkshirt')
    await userEvent.selectOptions(screen.getByTestId('category-select'), 'cat-kleding')
    await userEvent.click(screen.getByRole('button', { name: 'Opslaan' }))

    expect(api.create).toHaveBeenCalled()
    expect(api.createVariant).toHaveBeenCalledTimes(2)
    expect(api.createVariant).toHaveBeenNthCalledWith(1, 't-1', expect.objectContaining({ label: 'Small', initialStock: 10 }))
    expect(api.createVariant).toHaveBeenNthCalledWith(2, 't-1', expect.objectContaining({ label: 'Medium', initialStock: 15 }))
  })
})
