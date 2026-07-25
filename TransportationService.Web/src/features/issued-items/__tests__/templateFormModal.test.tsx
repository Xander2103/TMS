import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { TemplateFormModal } from '../TemplateFormModal'

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

const api = vi.hoisted(() => ({ create: vi.fn(), createVariant: vi.fn() }))

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
    listInventoryUnitOptions: () =>
      Promise.resolve([
        { id: 'u-stuk', code: 'PIECE', name: 'Stuks', symbol: 'st' },
        { id: 'u-paar', code: 'PAAR', name: 'Paar', symbol: null },
        { id: 'u-doos', code: 'BOX', name: 'Doos', symbol: null },
      ]),
    createIssuedItemTemplate: api.create,
    updateIssuedItemTemplate: vi.fn(),
  }
})

function renderModal() {
  return render(
    <MemoryRouter>
      <TemplateFormModal editing={null} onSaved={() => {}} onClose={() => {}} />
    </MemoryRouter>,
  )
}

describe('TemplateFormModal', () => {
  it('renders the category dropdown with a manage link for authorized users', () => {
    permissions.value = new Set(['inventory.manage'])
    renderModal()

    expect(screen.getByTestId('category-select')).toBeInTheDocument()
    const manageLink = screen.getByRole('link', { name: '+ Categorieën beheren' })
    expect(manageLink).toHaveAttribute('href', '/master-data/issued-item-categories')
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

  it('offers the stock unit as a managed dropdown, not free text', async () => {
    permissions.value = new Set(['unit_types.manage'])
    renderModal()

    await userEvent.click(screen.getByLabelText('Voorraadbeheer'))
    const unitSelect = await screen.findByLabelText('Eenheid')
    expect(unitSelect.tagName).toBe('SELECT')
    expect(await screen.findByRole('option', { name: 'Paar' })).toBeInTheDocument()
    expect(screen.getByRole('option', { name: 'Doos' })).toBeInTheDocument()

    // Managed master data stays one click away for authorized users.
    expect(screen.getByRole('link', { name: '+ Eenheden beheren' })).toHaveAttribute('href', '/master-data/eenheden')
  })

  it('hides the unit manage action without permission and persists the chosen unit', async () => {
    permissions.value = new Set()
    api.create.mockResolvedValue({ id: 't-1' })
    renderModal()

    await userEvent.click(screen.getByLabelText('Voorraadbeheer'))
    await screen.findByRole('option', { name: 'Paar' })
    expect(screen.queryByRole('link', { name: '+ Eenheden beheren' })).not.toBeInTheDocument()

    await userEvent.type(screen.getByLabelText(/^Naam/), 'Veiligheidsschoenen')
    await userEvent.selectOptions(screen.getByTestId('category-select'), 'cat-kleding')
    await userEvent.selectOptions(screen.getByLabelText('Eenheid'), 'Paar')
    await userEvent.click(screen.getByRole('button', { name: 'Opslaan' }))

    expect(api.create).toHaveBeenCalledWith(expect.objectContaining({ unit: 'Paar' }))
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
