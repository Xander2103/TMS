import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { TemplateFormModal } from '../TemplateFormModal'
import type { IssuedItemTemplate } from '../issuedItemsApi'

/**
 * Redesign 2026-10-07: the dialog asks four plain questions (what, where, how tracked, for whom)
 * and maps them onto the unchanged template fields. These tests pin the conditional behaviour:
 * which fields each Beheerwijze shows, that nothing stock-related is duplicated, the inline
 * variants, the role picker behind the function-code CSV, smart defaults, and both flows.
 */

const permissions = vi.hoisted(() => ({ value: new Set<string>(['issued_items.manage_templates', 'job_functions.view']) }))

vi.mock('../../auth/authContextValue', () => ({
  useAuth: () => ({ hasPermission: (code: string) => permissions.value.has(code) }),
}))

// Captures what the form hands to the category select (inline-create configuration).
const lookupSelectProps = vi.hoisted(() => ({ category: null as Record<string, unknown> | null }))

// Lookup selects fetch over HTTP; plain selects keep these tests focused on the dialog itself.
vi.mock('../../master-data/components/LookupSelect', () => ({
  LookupSelect: (props: { id?: string; basePath: string; value: string | null; onChange: (v: string | null) => void }) => {
    const isCategory = props.basePath.includes('issued-item-categories')
    if (isCategory) lookupSelectProps.category = props
    const options = isCategory
      ? [
          ['cat-kleding', 'Kleding'],
          ['cat-it', 'IT-materiaal'],
        ]
      : [
          ['CHAUF', 'Chauffeur'],
          ['PLAN', 'Planner'],
          ['MAGM', 'Magazijnier'],
        ]
    return (
      <select
        id={props.id}
        data-testid={isCategory ? 'category-select' : 'function-select'}
        value={props.value ?? ''}
        onChange={(e) => props.onChange(e.target.value || null)}
      >
        <option value="">—</option>
        {options.map(([value, label]) => (
          <option key={value} value={value}>
            {label}
          </option>
        ))}
      </select>
    )
  },
}))

vi.mock('../../master-data/hooks/useLookupOptions', () => ({
  useLookupOptions: () => ({
    options: [
      { id: 'jf-1', code: 'CHAUF', name: 'Chauffeur' },
      { id: 'jf-2', code: 'PLAN', name: 'Planner' },
      { id: 'jf-3', code: 'MAGM', name: 'Magazijnier' },
    ],
    isLoading: false,
    error: null,
    refresh: () => Promise.resolve(),
  }),
}))

const api = vi.hoisted(() => ({ create: vi.fn(), update: vi.fn(), createVariant: vi.fn(), getTemplateDetail: vi.fn() }))

vi.mock('../inventoryApi', async (importOriginal) => {
  const original = await importOriginal<typeof import('../inventoryApi')>()
  return {
    ...original,
    createVariant: api.createVariant,
    getTemplateDetail: api.getTemplateDetail,
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

function renderModal(editing: IssuedItemTemplate | null = null, extra: { existingItems?: IssuedItemTemplate[]; onSaved?: (...args: unknown[]) => void } = {}) {
  return render(
    <MemoryRouter>
      <TemplateFormModal editing={editing} existingItems={extra.existingItems} onSaved={extra.onSaved ?? (() => {})} onClose={() => {}} />
    </MemoryRouter>,
  )
}

const radio = (name: string) => screen.getByRole('radio', { name })
const openAdvanced = () => userEvent.click(screen.getByRole('button', { name: /Meer instellingen/ }))
/** Labels of the pending variant rows, in list order. */
const variantLabels = () => screen.queryAllByLabelText(/Maat of uitvoering van variant/).map((input) => (input as HTMLInputElement).value)
/** Free entry from an empty list (the tests below that type their own sizes start here). */
const chooseCustomVariants = () => userEvent.click(radio('Zelf varianten bepalen'))

describe('TemplateFormModal — create flow', () => {
  const fetchSpy = vi.fn()

  beforeEach(() => {
    vi.stubGlobal('fetch', fetchSpy)
    fetchSpy.mockReset()
    api.create.mockReset()
    api.update.mockReset()
    api.createVariant.mockReset()
    api.getTemplateDetail.mockReset()
    permissions.value = new Set(['issued_items.manage_templates', 'job_functions.view'])
    lookupSelectProps.category = null
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('opens calm: Individueel item preselected, no Actief, technical fields folded under Meer instellingen', async () => {
    renderModal()

    expect(screen.getByRole('dialog', { name: 'Nieuw bedrijfsmiddel' })).toBeInTheDocument()
    expect(radio('Individueel item')).toBeChecked()
    expect(radio('Voorraadartikel')).not.toBeChecked()
    expect(radio('Voorraad met varianten')).not.toBeChecked()

    // An individually tracked item leads with its identifier; no stock fields in sight.
    expect(screen.getByLabelText('Serienummer / identificatie verplicht')).toBeInTheDocument()
    expect(screen.queryByLabelText('Beginvoorraad')).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: '+ Variant toevoegen' })).not.toBeInTheDocument()

    // Secondary settings stay out of the default path — and "Actief" is never shown on create.
    expect(screen.queryByLabelText('Volgorde in lijst')).not.toBeInTheDocument()
    expect(screen.queryByLabelText('Eenheid')).not.toBeInTheDocument()
    await openAdvanced()
    expect(screen.getByLabelText('Volgorde in lijst')).toBeInTheDocument()
    expect(screen.getByLabelText('Eenheid')).toHaveValue('piece')
    expect(screen.getByLabelText('Standaardaantal bij uitgifte')).toHaveValue(1)
    expect(screen.queryByLabelText('Actief (kan uitgereikt worden)')).not.toBeInTheDocument()
    // The unit list is a fixed catalogue: no master-data request behind it.
    expect(fetchSpy).not.toHaveBeenCalled()
  })

  it('Voorraadartikel reveals one stock block (begin stock, minimum, location) and nothing variant-related', async () => {
    renderModal()
    await userEvent.click(radio('Voorraadartikel'))

    expect(screen.getByLabelText('Beginvoorraad')).toBeInTheDocument()
    expect(screen.getByLabelText('Minimumvoorraad')).toBeInTheDocument()
    expect(screen.getByLabelText('Opslaglocatie')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: '+ Variant toevoegen' })).not.toBeInTheDocument()
    expect(screen.getByLabelText('Serienummer / identificatie verplicht')).toBeInTheDocument()
  })

  it('Voorraad met varianten shows the inline variants table at once and no competing item-level stock fields', async () => {
    renderModal()
    await userEvent.click(radio('Voorraad met varianten'))

    expect(screen.getByRole('button', { name: '+ Variant toevoegen' })).toBeInTheDocument()
    expect(screen.getByLabelText('Meerdere tegelijk toevoegen')).toBeInTheDocument()
    // The list does not start empty: the Kledingmaten preset is selected and S–XL are rows already.
    expect(radio('Kledingmaten')).toBeChecked()
    expect(radio('Zelf varianten bepalen')).not.toBeChecked()
    expect(variantLabels()).toEqual(['S', 'M', 'L', 'XL'])
    expect(screen.queryByLabelText('Beginvoorraad')).not.toBeInTheDocument()
    expect(screen.queryByLabelText('Minimumvoorraad')).not.toBeInTheDocument()
    // Serial tracking is unusual for sizes: it waits under Meer instellingen, as does the
    // default minimum that the backend applies to variants without their own.
    expect(screen.queryByLabelText('Serienummer / identificatie verplicht')).not.toBeInTheDocument()
    await openAdvanced()
    expect(screen.getByLabelText('Serienummer / identificatie verplicht')).toBeInTheDocument()
    expect(screen.getByLabelText('Standaard minimum voor varianten')).toBeInTheDocument()
    // Exactly one threshold input exists for the item: the default for variants, nothing else.
    expect(document.querySelectorAll('#tpl-low')).toHaveLength(1)
    expect(screen.queryByLabelText('Beginvoorraad')).not.toBeInTheDocument()
  })

  it('creates "Kleding → T-shirt → S/M/L/XL" with stock per size, variants right after the item', async () => {
    api.create.mockResolvedValue({ id: 't-new', variantsEnabled: true })
    api.createVariant.mockResolvedValue({ id: 'v' })
    const onSaved = vi.fn()
    renderModal(null, { onSaved })

    await userEvent.type(screen.getByLabelText(/^Naam/), 'T-shirt')
    await userEvent.selectOptions(screen.getByTestId('category-select'), 'cat-kleding')
    await userEvent.click(radio('Voorraad met varianten'))
    await chooseCustomVariants()
    expect(variantLabels()).toEqual([])

    // Quick-add turns a comma list into rows; Enter never submits the item form.
    await userEvent.type(screen.getByLabelText('Meerdere tegelijk toevoegen'), 'S, M, L{Enter}')
    expect(screen.getAllByLabelText(/Maat of uitvoering van variant/)).toHaveLength(3)
    expect(api.create).not.toHaveBeenCalled()

    await userEvent.type(screen.getByLabelText('Voorraad van variant 1'), '5')
    await userEvent.type(screen.getByLabelText('Voorraad van variant 2'), '12')
    await userEvent.type(screen.getByLabelText('Minimum van variant 2'), '3')
    await userEvent.click(screen.getByRole('button', { name: '+ Variant toevoegen' }))
    const fourth = screen.getByLabelText('Maat of uitvoering van variant 4')
    expect(fourth).toHaveFocus()
    await userEvent.type(fourth, 'XL')
    expect(screen.getByText('Totale voorraad: 17 Stuk (som van de varianten)')).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Opslaan' }))

    expect(api.create).toHaveBeenCalledWith(
      expect.objectContaining({
        name: 'T-shirt',
        categoryId: 'cat-kleding',
        stockTrackingEnabled: true,
        variantsEnabled: true,
        stock: null,
        applicableJobFunctionCodes: null,
        isActive: true,
      }),
    )
    expect(api.createVariant).toHaveBeenCalledTimes(4)
    expect(api.createVariant).toHaveBeenNthCalledWith(1, 't-new', expect.objectContaining({ label: 'S', initialStock: 5, lowStockThreshold: null, sortOrder: 0 }))
    expect(api.createVariant).toHaveBeenNthCalledWith(2, 't-new', expect.objectContaining({ label: 'M', initialStock: 12, lowStockThreshold: 3, sortOrder: 1 }))
    expect(api.createVariant).toHaveBeenNthCalledWith(3, 't-new', expect.objectContaining({ label: 'L', initialStock: null }))
    expect(api.createVariant).toHaveBeenNthCalledWith(4, 't-new', expect.objectContaining({ label: 'XL', initialStock: null, sortOrder: 3 }))
    expect(onSaved).toHaveBeenCalledWith({ id: 't-new', variantsEnabled: true }, undefined)
  })

  it('starts from Kledingmaten: S–XL are plain rows, each removable; XS/XXL are one click away; saved as ordinary variants', async () => {
    api.create.mockResolvedValue({ id: 't-new', variantsEnabled: true })
    api.createVariant.mockResolvedValue({ id: 'v' })
    renderModal()
    await userEvent.type(screen.getByLabelText(/^Naam/), 'Polo')
    await userEvent.selectOptions(screen.getByTestId('category-select'), 'cat-kleding')
    await userEvent.click(radio('Voorraad met varianten'))

    expect(radio('Kledingmaten')).toBeChecked()
    expect(screen.getByText(/Start met S \(Small\), M \(Medium\), L \(Large\) en XL \(Extra Large\)/)).toBeInTheDocument()
    expect(variantLabels()).toEqual(['S', 'M', 'L', 'XL'])

    // A suggested size is an ordinary row: it can be removed on its own.
    await userEvent.click(screen.getByRole('button', { name: 'Variant M verwijderen' }))
    expect(variantLabels()).toEqual(['S', 'L', 'XL'])

    // Canonical sizes that are missing are one click away and land in wearing order; the
    // cursor goes straight to the stock of the new row.
    const sizes = screen.getByRole('group', { name: 'Veelgebruikte maten' })
    expect(within(sizes).getAllByRole('button').map((button) => button.textContent)).toEqual(['+ XS', '+ M', '+ XXL', '+ 3XL'])
    await userEvent.click(within(sizes).getByRole('button', { name: 'Variant XS toevoegen' }))
    expect(variantLabels()).toEqual(['XS', 'S', 'L', 'XL'])
    expect(screen.getByLabelText('Voorraad van variant 1')).toHaveFocus()
    await userEvent.keyboard('2')
    await userEvent.click(within(sizes).getByRole('button', { name: 'Variant XXL toevoegen' }))
    expect(variantLabels()).toEqual(['XS', 'S', 'L', 'XL', 'XXL'])
    expect(within(sizes).queryByRole('button', { name: 'Variant XS toevoegen' })).not.toBeInTheDocument()
    // Anything else still goes through the quick-add or the plain add button.
    await userEvent.type(screen.getByLabelText('Meerdere tegelijk toevoegen'), 'zwart{Enter}')
    expect(variantLabels()).toEqual(['XS', 'S', 'L', 'XL', 'XXL', 'zwart'])

    await userEvent.click(screen.getByRole('button', { name: 'Opslaan' }))

    expect(api.create).toHaveBeenCalledWith(expect.objectContaining({ stockTrackingEnabled: true, variantsEnabled: true }))
    // Plain variant records in list order — nothing about a preset reaches the backend.
    expect(api.createVariant.mock.calls.map(([, input]) => [input.label, input.sortOrder, input.initialStock])).toEqual([
      ['XS', 0, 2],
      ['S', 1, null],
      ['L', 2, null],
      ['XL', 3, null],
      ['XXL', 4, null],
      ['zwart', 5, null],
    ])
    expect(Object.keys(api.createVariant.mock.calls[0][1]).sort()).toEqual(['initialStock', 'isActive', 'label', 'lowStockThreshold', 'sortOrder', 'values'])
  })

  it('"Zelf varianten bepalen" empties the list for free entry; Kledingmaten brings S–XL back', async () => {
    renderModal()
    await userEvent.click(radio('Voorraad met varianten'))

    await chooseCustomVariants()
    expect(radio('Zelf varianten bepalen')).toBeChecked()
    expect(variantLabels()).toEqual([])
    expect(screen.getByText('Nog geen varianten. Voeg minstens één maat of uitvoering toe, bv. S, M, L, XL.')).toBeInTheDocument()
    expect(screen.queryByRole('group', { name: 'Veelgebruikte maten' })).not.toBeInTheDocument()

    await userEvent.type(screen.getByLabelText('Meerdere tegelijk toevoegen'), 'maat 42, maat 43{Enter}')
    expect(variantLabels()).toEqual(['maat 42', 'maat 43'])

    await userEvent.click(radio('Kledingmaten'))
    expect(variantLabels()).toEqual(['S', 'M', 'L', 'XL'])
  })

  it('rejects empty or duplicate sizes before anything is sent', async () => {
    renderModal()
    await userEvent.type(screen.getByLabelText(/^Naam/), 'Werkbroek')
    await userEvent.selectOptions(screen.getByTestId('category-select'), 'cat-kleding')
    await userEvent.click(radio('Voorraad met varianten'))
    await chooseCustomVariants()
    await userEvent.type(screen.getByLabelText('Meerdere tegelijk toevoegen'), '46{Enter}')
    await userEvent.click(screen.getByRole('button', { name: '+ Variant toevoegen' }))
    await userEvent.type(screen.getByLabelText('Maat of uitvoering van variant 2'), ' 46 ')
    await userEvent.click(screen.getByRole('button', { name: '+ Variant toevoegen' }))

    await userEvent.click(screen.getByRole('button', { name: 'Opslaan' }))

    expect(api.create).not.toHaveBeenCalled()
    expect(screen.getByText('Deze maat of uitvoering staat al in de lijst.')).toBeInTheDocument()
    expect(screen.getByText('Geef een maat of uitvoering op (bv. S, maat 43, zwart).')).toBeInTheDocument()
  })

  it('reports variants that failed after the item was created instead of failing the whole save', async () => {
    api.create.mockResolvedValue({ id: 't-new' })
    api.createVariant.mockImplementation((_id: string, input: { label: string }) =>
      input.label === 'M' ? Promise.reject(new Error('De variant "M" bestaat al.')) : Promise.resolve({ id: 'v' }),
    )
    const onSaved = vi.fn()
    renderModal(null, { onSaved })

    await userEvent.type(screen.getByLabelText(/^Naam/), 'Polo')
    await userEvent.selectOptions(screen.getByTestId('category-select'), 'cat-kleding')
    await userEvent.click(radio('Voorraad met varianten'))
    await chooseCustomVariants()
    await userEvent.type(screen.getByLabelText('Meerdere tegelijk toevoegen'), 'S, M{Enter}')
    await userEvent.click(screen.getByRole('button', { name: 'Opslaan' }))

    expect(onSaved).toHaveBeenCalledWith({ id: 't-new' }, { variantFailures: [{ label: 'M', message: 'De variant "M" bestaat al.' }] })
  })

  it('"Voor wie?" defaults to Iedereen and maps chosen functions onto the stored code list', async () => {
    api.create.mockResolvedValue({ id: 't-new' })
    renderModal()
    await userEvent.type(screen.getByLabelText(/^Naam/), 'Tankkaart')
    await userEvent.selectOptions(screen.getByTestId('category-select'), 'cat-it')

    expect(screen.getByRole('radio', { name: 'Iedereen' })).toBeChecked()
    expect(screen.queryByTestId('function-select')).not.toBeInTheDocument()

    await userEvent.click(screen.getByRole('radio', { name: 'Alleen bepaalde functies' }))
    await userEvent.selectOptions(screen.getByTestId('function-select'), 'CHAUF')
    await userEvent.selectOptions(screen.getByTestId('function-select'), 'PLAN')
    await userEvent.selectOptions(screen.getByTestId('function-select'), 'MAGM')
    await userEvent.click(screen.getByRole('button', { name: 'Planner verwijderen' }))
    // Chips are recognisable by their remove buttons (the stub select also lists the names).
    expect(screen.getByRole('button', { name: 'Chauffeur verwijderen' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Magazijnier verwijderen' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Planner verwijderen' })).not.toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Opslaan' }))
    expect(api.create).toHaveBeenCalledWith(expect.objectContaining({ applicableJobFunctionCodes: 'CHAUF,MAGM' }))
    // Raw comma-separated codes are never typed by the user.
    expect(screen.queryByLabelText(/functiecode/i)).not.toBeInTheDocument()
  })

  it('suggests the Beheerwijze and issue settings the category already uses, without overriding an explicit choice', async () => {
    const existingItems = [
      makeTemplate({ id: 'k1', categoryId: 'cat-kleding', stockTrackingEnabled: true, variantsEnabled: true, returnRequired: true }),
      makeTemplate({ id: 'k2', categoryId: 'cat-kleding', stockTrackingEnabled: true, variantsEnabled: true, returnRequired: true }),
      makeTemplate({ id: 'i1', categoryId: 'cat-it', requiresSerialNumber: true, returnRequired: true }),
    ]
    renderModal(null, { existingItems })

    await userEvent.selectOptions(screen.getByTestId('category-select'), 'cat-kleding')
    expect(radio('Voorraad met varianten')).toBeChecked()
    expect(screen.getByLabelText('Moet worden teruggebracht')).toBeChecked()
    expect(screen.getByRole('status')).toHaveTextContent('Voorgesteld op basis van 2 ander(e) bedrijfsmiddel(en)')

    // The user decides otherwise; a later category change must not undo that.
    await userEvent.click(radio('Voorraadartikel'))
    expect(screen.queryByRole('status')).not.toBeInTheDocument()
    await userEvent.selectOptions(screen.getByTestId('category-select'), 'cat-it')
    expect(radio('Voorraadartikel')).toBeChecked()
    expect(screen.queryByRole('status')).not.toBeInTheDocument()
  })

  it('configures the category dropdown for name-only inline creation, with the admin link only for category managers', () => {
    const { unmount } = renderModal()
    expect(lookupSelectProps.category).toEqual(
      expect.objectContaining({ createFields: 'name', createLabel: '+ Nieuwe categorie maken', createTitle: 'Nieuwe categorie', managePermission: 'inventory.manage' }),
    )
    expect(screen.queryByRole('link', { name: 'Categorieën beheren' })).not.toBeInTheDocument()
    unmount()

    permissions.value = new Set(['issued_items.manage_templates', 'inventory.manage'])
    renderModal()
    expect(screen.getByRole('link', { name: 'Categorieën beheren' })).toHaveAttribute('href', '/issued-items/categories')
  })

  it('asks for a name and a category before saving', async () => {
    renderModal()
    await userEvent.click(screen.getByRole('button', { name: 'Opslaan' }))
    expect(screen.getByRole('alert')).toHaveTextContent('Geef een naam op.')

    await userEvent.type(screen.getByLabelText(/^Naam/), 'Helm')
    await userEvent.click(screen.getByRole('button', { name: 'Opslaan' }))
    expect(screen.getByRole('alert')).toHaveTextContent('Kies een categorie of maak er een aan.')
    expect(api.create).not.toHaveBeenCalled()
  })

  it('persists the chosen catalogue unit code from Meer instellingen', async () => {
    api.create.mockResolvedValue({ id: 't-new' })
    renderModal()
    await userEvent.type(screen.getByLabelText(/^Naam/), 'Veiligheidsschoenen')
    await userEvent.selectOptions(screen.getByTestId('category-select'), 'cat-kleding')
    await openAdvanced()
    await userEvent.selectOptions(screen.getByLabelText('Eenheid'), 'Paar')
    expect(screen.getByText(/Paar — schoenen, handschoenen/)).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Opslaan' }))
    expect(api.create).toHaveBeenCalledWith(expect.objectContaining({ unit: 'pair' }))
  })
})

describe('TemplateFormModal — edit flow (existing records keep working)', () => {
  beforeEach(() => {
    api.create.mockReset()
    api.update.mockReset()
    api.createVariant.mockReset()
    api.getTemplateDetail.mockReset()
    permissions.value = new Set(['issued_items.manage_templates', 'job_functions.view'])
  })

  it('opens a legacy record: snapshot category, derived Beheerwijze, code chips (unknown codes kept), legacy unit → Overige', async () => {
    api.update.mockResolvedValue({ id: 't-1' })
    renderModal(
      makeTemplate({
        categoryId: null,
        category: 'PBM',
        applicableJobFunctionCodes: 'CHAUF, XYZ',
        unit: 'Paar',
        stockTrackingEnabled: true,
        currentStock: 7,
        lowStockThreshold: 2,
        storageLocation: 'Rek A',
      }),
    )

    expect(screen.getByRole('dialog', { name: 'Bedrijfsmiddel bewerken' })).toBeInTheDocument()
    expect(radio('Voorraadartikel')).toBeChecked()
    expect(screen.getByLabelText('Huidige voorraad')).toHaveValue(7)
    expect(screen.getByLabelText('Minimumvoorraad')).toHaveValue(2)
    expect(screen.getByLabelText('Opslaglocatie')).toHaveValue('Rek A')
    expect(screen.getByRole('radio', { name: 'Alleen bepaalde functies' })).toBeChecked()
    expect(screen.getByRole('button', { name: 'Chauffeur verwijderen' })).toBeInTheDocument()
    expect(screen.getByText('XYZ (onbekende functiecode)')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'XYZ verwijderen' })).toBeInTheDocument()
    await openAdvanced()
    expect(screen.getByLabelText('Eenheid')).toHaveValue('other')
    expect(screen.getByLabelText('Actief (kan uitgereikt worden)')).toBeChecked()

    await userEvent.click(screen.getByRole('button', { name: 'Opslaan' }))
    expect(api.update).toHaveBeenCalledWith(
      't-1',
      expect.objectContaining({
        category: 'PBM',
        categoryId: null,
        applicableJobFunctionCodes: 'CHAUF,XYZ',
        unit: 'other',
        stockTrackingEnabled: true,
        variantsEnabled: false,
        stock: 7,
        stockCorrectionReason: null,
        minimumStock: null,
      }),
    )
  })

  it('a stock change on an existing item needs a correction reason (ledger)', async () => {
    api.update.mockResolvedValue({ id: 't-1' })
    renderModal(makeTemplate({ stockTrackingEnabled: true, currentStock: 7 }))

    const stock = screen.getByLabelText('Huidige voorraad')
    await userEvent.clear(stock)
    await userEvent.type(stock, '9')
    const reason = screen.getByLabelText(/Reden voor correctie/)
    await userEvent.click(screen.getByRole('button', { name: 'Opslaan' }))
    expect(screen.getByRole('alert')).toHaveTextContent('Geef een reden op voor de voorraadcorrectie.')
    expect(api.update).not.toHaveBeenCalled()

    await userEvent.type(reason, 'Telling magazijn')
    await userEvent.click(screen.getByRole('button', { name: 'Opslaan' }))
    expect(api.update).toHaveBeenCalledWith('t-1', expect.objectContaining({ stock: 9, stockCorrectionReason: 'Telling magazijn' }))
  })

  it('switching an item back to stock offers (but does not force) a reason, since the ledger may hold history', async () => {
    api.update.mockResolvedValue({ id: 't-1' })
    // Tracked earlier, switched off: the cached count (4) is still on the row.
    renderModal(makeTemplate({ stockTrackingEnabled: false, currentStock: 4 }))

    await userEvent.click(radio('Voorraadartikel'))
    expect(screen.getByLabelText('Beginvoorraad')).toHaveValue(null)
    expect(screen.queryByLabelText(/Reden voor correctie/)).not.toBeInTheDocument()
    await userEvent.type(screen.getByLabelText('Beginvoorraad'), '10')
    expect(screen.getByLabelText('Reden voor correctie')).toBeInTheDocument()
    expect(screen.getByText('Alleen nodig als dit bedrijfsmiddel vroeger al voorraad had.')).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Opslaan' }))
    expect(api.update).toHaveBeenCalledWith('t-1', expect.objectContaining({ stockTrackingEnabled: true, stock: 10, stockCorrectionReason: null }))
  })

  it('an item that already has variants keeps its Beheerwijze and edits its variants live', async () => {
    api.getTemplateDetail.mockResolvedValue({
      template: makeTemplate({ stockTrackingEnabled: true, variantsEnabled: true, variantCount: 2 }),
      attributes: [],
      variants: [
        { id: 'v-1', label: 'S', currentStock: 4, isActive: true, sortOrder: 0, values: [], lowStockThreshold: null },
        { id: 'v-2', label: 'M', currentStock: 6, isActive: true, sortOrder: 1, values: [], lowStockThreshold: 2 },
      ],
    })
    renderModal(makeTemplate({ stockTrackingEnabled: true, variantsEnabled: true, variantCount: 2 }))

    expect(radio('Voorraad met varianten')).toBeChecked()
    expect(radio('Individueel item')).toBeDisabled()
    expect(radio('Voorraadartikel')).toBeDisabled()
    expect(screen.getByText(/De beheerwijze ligt vast zolang dit bedrijfsmiddel varianten heeft/)).toBeInTheDocument()
    expect(await screen.findByLabelText('Voorraad van S')).toHaveValue(4)
    expect(screen.getByLabelText('Minimum van M')).toHaveValue(2)
    expect(screen.getByText('Wijzigingen aan bestaande varianten worden meteen opgeslagen.')).toBeInTheDocument()
    // No pending-rows tools next to the live editor, and no item-level stock field.
    expect(screen.queryByLabelText('Meerdere tegelijk toevoegen')).not.toBeInTheDocument()
    expect(screen.queryByRole('radio', { name: 'Kledingmaten' })).not.toBeInTheDocument()
    expect(screen.queryByLabelText('Huidige voorraad')).not.toBeInTheDocument()
  })

  it('switching an existing stock item to variants accepts sizes before saving and creates them after the update', async () => {
    api.update.mockResolvedValue({ id: 't-1', variantsEnabled: true })
    api.createVariant.mockResolvedValue({ id: 'v' })
    renderModal(makeTemplate({ stockTrackingEnabled: true, currentStock: 3 }))

    await userEvent.click(radio('Voorraad met varianten'))
    expect(screen.queryByText(/Varianten worden actief na het opslaan/)).not.toBeInTheDocument()
    // The first switch to variants starts from the preset as well; this test keeps typing its own.
    expect(radio('Kledingmaten')).toBeChecked()
    expect(variantLabels()).toEqual(['S', 'M', 'L', 'XL'])
    await chooseCustomVariants()
    await userEvent.type(screen.getByLabelText('Meerdere tegelijk toevoegen'), 'S, M{Enter}')
    await userEvent.click(screen.getByRole('button', { name: 'Opslaan' }))

    expect(api.update).toHaveBeenCalledWith('t-1', expect.objectContaining({ stockTrackingEnabled: true, variantsEnabled: true, stock: null }))
    expect(api.createVariant).toHaveBeenCalledTimes(2)
    expect(api.createVariant).toHaveBeenNthCalledWith(1, 't-1', expect.objectContaining({ label: 'S' }))
    expect(api.createVariant).toHaveBeenNthCalledWith(2, 't-1', expect.objectContaining({ label: 'M' }))
  })

  it('a deactivated item says so and can be reactivated from Meer instellingen (opened automatically)', async () => {
    api.update.mockResolvedValue({ id: 't-1' })
    renderModal(makeTemplate({ isActive: false }))

    expect(screen.getByText('Dit bedrijfsmiddel is gedeactiveerd en kan niet meer uitgereikt worden.')).toBeInTheDocument()
    const active = screen.getByLabelText('Actief (kan uitgereikt worden)')
    expect(active).not.toBeChecked()
    await userEvent.click(active)
    await userEvent.click(screen.getByRole('button', { name: 'Opslaan' }))
    expect(api.update).toHaveBeenCalledWith('t-1', expect.objectContaining({ isActive: true }))
  })

  it('keeps a legacy "serial required" visible on a variants item through a pointer to Meer instellingen', () => {
    api.getTemplateDetail.mockResolvedValue({ template: makeTemplate({}), attributes: [], variants: [] })
    renderModal(makeTemplate({ stockTrackingEnabled: true, variantsEnabled: true, variantCount: 1, requiresSerialNumber: true }))
    const issue = screen.getByRole('region', { name: 'Uitgifte' })
    expect(within(issue).getByText(/Serienummer \/ identificatie is verplicht voor dit bedrijfsmiddel/)).toBeInTheDocument()
  })
})
