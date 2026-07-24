import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
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
})
