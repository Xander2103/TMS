import { describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom'
import { ApiError } from '../../../api/apiClient'
import { IssuedItemTemplatesPanel } from '../components/IssuedItemTemplatesPanel'
import type { IssuedItemTemplate } from '../issuedItemsApi'

const toast = vi.hoisted(() => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }))

vi.mock('../../../components/ui/toastContext', () => ({
  useToast: () => toast,
}))

// The dialog has its own suite; here it is a stub that reports the two possible outcomes.
vi.mock('../TemplateFormModal', () => ({
  TemplateFormModal: ({
    editing,
    existingItems,
    onSaved,
  }: {
    editing: IssuedItemTemplate | null
    existingItems?: IssuedItemTemplate[]
    onSaved: (t: IssuedItemTemplate, r?: { variantFailures: { label: string; message: string }[] }) => void
  }) => (
    <div data-testid="form-stub" data-editing={editing?.id ?? ''} data-existing={existingItems?.length ?? -1}>
      <button type="button" onClick={() => onSaved(makeTemplate({ id: 't-saved', variantsEnabled: true, stockTrackingEnabled: true }))}>
        stub-save-ok
      </button>
      <button
        type="button"
        onClick={() =>
          onSaved(makeTemplate({ id: 't-saved', variantsEnabled: true, stockTrackingEnabled: true }), {
            variantFailures: [{ label: 'M', message: 'bestaat al' }],
          })
        }
      >
        stub-save-partial
      </button>
    </div>
  ),
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
const deleteTemplate = vi.hoisted(() => vi.fn())

vi.mock('../issuedItemsApi', async () => {
  const actual = await vi.importActual<typeof import('../issuedItemsApi')>('../issuedItemsApi')
  return {
    ...actual,
    listIssuedItemTemplates: () => Promise.resolve(templates.value),
    deleteIssuedItemTemplate: (...args: unknown[]) => deleteTemplate(...args),
  }
})

function LocationProbe() {
  const location = useLocation()
  return <output data-testid="location">{location.pathname + location.search}</output>
}

function renderPanel() {
  return render(
    <MemoryRouter initialEntries={['/issued-items/templates']}>
      <Routes>
        <Route
          path="*"
          element={
            <>
              <IssuedItemTemplatesPanel />
              <LocationProbe />
            </>
          }
        />
      </Routes>
    </MemoryRouter>,
  )
}

describe('IssuedItemTemplatesPanel — the Bedrijfsmiddelen overview', () => {
  it('describes each item by its Beheerwijze and availability instead of technical yes/no columns', async () => {
    templates.value = [
      makeTemplate({
        id: 't-1',
        name: 'Veiligheidsschoenen',
        stockTrackingEnabled: true,
        variantsEnabled: true,
        variantCount: 4,
        totalAvailable: 2,
        lowStockThreshold: 3,
        lowStock: true,
        unit: 'pair',
        returnRequired: true,
      }),
      makeTemplate({ id: 't-2', name: 'Toegangsbadge', requiresSerialNumber: true }),
      makeTemplate({ id: 't-3', name: 'Helm', stockTrackingEnabled: true, totalAvailable: 12 }),
    ]
    renderPanel()

    await waitFor(() => expect(screen.getByText('Veiligheidsschoenen')).toBeInTheDocument())
    expect(screen.getByRole('columnheader', { name: 'Beheerwijze' })).toBeInTheDocument()
    expect(screen.queryByRole('columnheader', { name: 'Voorraadbeheer' })).not.toBeInTheDocument()
    expect(screen.queryByRole('columnheader', { name: 'Varianten' })).not.toBeInTheDocument()

    const shoeRow = screen.getByRole('link', { name: 'Veiligheidsschoenen' }).closest('tr')!
    expect(shoeRow).toHaveTextContent('Voorraad met varianten')
    expect(shoeRow).toHaveTextContent('4 varianten')
    // The stored code "pair" renders as its Dutch label, with the low-stock warning.
    expect(shoeRow).toHaveTextContent('2 Paar')
    expect(shoeRow).toHaveTextContent('Lage voorraad')
    expect(shoeRow).toHaveTextContent('Terugbrengen')

    const badgeRow = screen.getByRole('link', { name: 'Toegangsbadge' }).closest('tr')!
    expect(badgeRow).toHaveTextContent('Individueel item')
    expect(badgeRow).toHaveTextContent('Serienummer')
    expect(badgeRow).not.toHaveTextContent('Ja')

    const helmetRow = screen.getByRole('link', { name: 'Helm' }).closest('tr')!
    expect(helmetRow).toHaveTextContent('Voorraadartikel')
    expect(helmetRow).toHaveTextContent('12 Stuk')
  })

  it('renders a legacy free-text unit as "Overige" instead of leaking the raw value', async () => {
    templates.value = [makeTemplate({ id: 't-1', name: 'Handschoenen', stockTrackingEnabled: true, totalAvailable: 7, unit: 'Paar' })]
    renderPanel()

    await waitFor(() => expect(screen.getByText('Handschoenen')).toBeInTheDocument())
    expect(screen.getByRole('link', { name: 'Handschoenen' }).closest('tr')!).toHaveTextContent('7 Overige')
  })

  it('filters on Beheerwijze and on low stock', async () => {
    templates.value = [
      makeTemplate({ id: 't-1', name: 'Veiligheidsschoenen', stockTrackingEnabled: true, variantsEnabled: true, totalAvailable: 1, lowStockThreshold: 3, lowStock: true }),
      makeTemplate({ id: 't-2', name: 'Toegangsbadge' }),
      makeTemplate({ id: 't-3', name: 'Helm', stockTrackingEnabled: true, totalAvailable: 50 }),
    ]
    renderPanel()
    await waitFor(() => expect(screen.getByText('Toegangsbadge')).toBeInTheDocument())

    await userEvent.selectOptions(screen.getByLabelText('Beheerwijze'), 'individual')
    expect(screen.getByText('Toegangsbadge')).toBeInTheDocument()
    expect(screen.queryByText('Helm')).not.toBeInTheDocument()
    expect(screen.queryByText('Veiligheidsschoenen')).not.toBeInTheDocument()

    await userEvent.selectOptions(screen.getByLabelText('Beheerwijze'), 'low')
    expect(screen.getByText('Veiligheidsschoenen')).toBeInTheDocument()
    expect(screen.queryByText('Toegangsbadge')).not.toBeInTheDocument()
    expect(screen.queryByText('Helm')).not.toBeInTheDocument()
  })

  it('B14: keeps edit and delete in one row menu instead of stacked links', async () => {
    templates.value = [makeTemplate({ id: 't-9', name: 'Scanner' })]
    renderPanel()
    const row = (await screen.findByRole('link', { name: 'Scanner' })).closest('tr')!
    expect(within(row).queryByRole('button', { name: 'Bewerken' })).not.toBeInTheDocument()
    expect(within(row).queryByRole('button', { name: 'Verwijderen' })).not.toBeInTheDocument()

    await userEvent.click(within(row).getByRole('button', { name: 'Acties Scanner' }))
    expect(within(screen.getByRole('menu')).getAllByRole('menuitem').map((item) => item.textContent)).toEqual(['Bewerken', 'Verwijderen'])
    await userEvent.click(screen.getByRole('menuitem', { name: 'Bewerken' }))
    expect(screen.getByTestId('form-stub')).toHaveAttribute('data-editing', 't-9')
  })

  it('links an item to its detail page under /issued-items', async () => {
    templates.value = [makeTemplate({ id: 't-9', name: 'Scanner' })]
    renderPanel()
    await waitFor(() => expect(screen.getByText('Scanner')).toBeInTheDocument())
    expect(screen.getByRole('link', { name: 'Scanner' })).toHaveAttribute('href', '/issued-items/templates/t-9')
  })

  it('opens "Nieuw bedrijfsmiddel" with the current items for smart defaults and stays on the list after a complete save', async () => {
    templates.value = [makeTemplate({ id: 't-1', name: 'Helm' }), makeTemplate({ id: 't-2', name: 'Badge' })]
    toast.showSuccess.mockClear()
    renderPanel()
    await waitFor(() => expect(screen.getByText('Helm')).toBeInTheDocument())

    await userEvent.click(screen.getByRole('button', { name: 'Nieuw bedrijfsmiddel' }))
    const stub = screen.getByTestId('form-stub')
    expect(stub).toHaveAttribute('data-editing', '')
    expect(stub).toHaveAttribute('data-existing', '2')

    await userEvent.click(screen.getByRole('button', { name: 'stub-save-ok' }))
    expect(toast.showSuccess).toHaveBeenCalledWith('Bedrijfsmiddel toegevoegd.')
    // Variants were entered inline, so there is nothing left to configure elsewhere.
    expect(screen.getByTestId('location')).toHaveTextContent('/issued-items/templates')
    expect(screen.queryByTestId('form-stub')).not.toBeInTheDocument()
  })

  it('sends the user to the Varianten tab with the failed labels when some variants could not be created', async () => {
    templates.value = [makeTemplate({ id: 't-1', name: 'Helm' })]
    toast.showError.mockClear()
    renderPanel()
    await waitFor(() => expect(screen.getByText('Helm')).toBeInTheDocument())

    await userEvent.click(screen.getByRole('button', { name: 'Nieuw bedrijfsmiddel' }))
    await userEvent.click(screen.getByRole('button', { name: 'stub-save-partial' }))

    expect(toast.showError).toHaveBeenCalledWith(expect.stringContaining('M'))
    expect(screen.getByTestId('location')).toHaveTextContent('/issued-items/templates/t-saved?tab=varianten')
  })

  it('explains a refused delete (409 record_referenced) with the server message and the counts', async () => {
    const message =
      'Dit artikel kan niet worden verwijderd omdat er historische of operationele gegevens aan gekoppeld zijn: 4 uitgiftes. Zet het artikel inactief.'
    deleteTemplate.mockRejectedValue(
      new ApiError(message, 409, {
        code: 'common.record_referenced',
        message,
        references: [{ key: 'issuedItems', count: 4, label: '4 uitgiftes' }],
        canDeactivate: true,
      }),
    )
    templates.value = [makeTemplate({ id: 't-4', name: 'Fluohesje' })]
    toast.showError.mockClear()
    renderPanel()
    const row = (await screen.findByRole('link', { name: 'Fluohesje' })).closest('tr')!

    await userEvent.click(within(row).getByRole('button', { name: 'Acties Fluohesje' }))
    await userEvent.click(screen.getByRole('menuitem', { name: 'Verwijderen' }))
    await userEvent.click(within(await screen.findByRole('dialog', { name: 'Bedrijfsmiddel verwijderen' })).getByRole('button', { name: 'Verwijderen' }))

    const dialog = await screen.findByRole('dialog', { name: 'Bedrijfsmiddel kan niet worden verwijderd' })
    expect(within(dialog).getByText(message)).toBeInTheDocument()
    expect(within(dialog).getByRole('listitem')).toHaveTextContent('4 uitgiftes')
    expect(toast.showError).not.toHaveBeenCalled()

    // The way out: open the item's edit form (where "Actief" lives).
    await userEvent.click(within(dialog).getByRole('button', { name: 'Artikel bewerken' }))
    expect(screen.getByTestId('form-stub')).toHaveAttribute('data-editing', 't-4')
  })
})
