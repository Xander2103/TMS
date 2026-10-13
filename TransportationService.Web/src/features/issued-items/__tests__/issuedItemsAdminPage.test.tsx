import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom'
import { IssuedItemsAdminPage } from '../pages/IssuedItemsAdminPage'

const permissions = vi.hoisted(() => ({ value: new Set<string>() }))

vi.mock('../../auth/authContextValue', () => ({
  useAuth: () => ({ hasPermission: (code: string) => permissions.value.has(code) }),
}))

vi.mock('../components/IssuedItemTemplatesPanel', () => ({
  IssuedItemTemplatesPanel: () => <div data-testid="items-panel" />,
}))

vi.mock('../../master-data/components/LookupManager', () => ({
  LookupManager: () => <div data-testid="categories-panel" />,
}))

function LocationProbe() {
  const location = useLocation()
  return <output data-testid="location">{location.pathname}</output>
}

function renderAt(path: string) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route
          path="/issued-items/:tab"
          element={
            <>
              <IssuedItemsAdminPage />
              <LocationProbe />
            </>
          }
        />
      </Routes>
    </MemoryRouter>,
  )
}

describe('IssuedItemsAdminPage — "what do we manage?" comes first', () => {
  it('lists Bedrijfsmiddelen before Categorieën, never mentions templates, and keeps the URL slugs', () => {
    permissions.value = new Set(['issued_items.manage_templates', 'inventory.manage'])
    renderAt('/issued-items/templates')

    const tabs = screen.getAllByRole('tab').map((tab) => tab.textContent)
    expect(tabs).toEqual(['Bedrijfsmiddelen', 'Categorieën'])
    expect(screen.getByRole('tab', { name: 'Bedrijfsmiddelen' })).toHaveAttribute('aria-selected', 'true')
    expect(screen.getByTestId('items-panel')).toBeInTheDocument()
    expect(screen.queryByText(/Sjabl/)).not.toBeInTheDocument()
    expect(screen.getByText(/Wat reiken we uit aan medewerkers/)).toBeInTheDocument()
  })

  it('shows the categories tab on its own slug', () => {
    permissions.value = new Set(['issued_items.manage_templates', 'inventory.manage'])
    renderAt('/issued-items/categories')
    expect(screen.getByRole('tab', { name: 'Categorieën' })).toHaveAttribute('aria-selected', 'true')
    expect(screen.getByTestId('categories-panel')).toBeInTheDocument()
  })

  it('lands a user without the items permission on the first tab they may open', () => {
    permissions.value = new Set(['inventory.manage'])
    renderAt('/issued-items/templates')
    expect(screen.getByTestId('location')).toHaveTextContent('/issued-items/categories')
    expect(screen.getAllByRole('tab').map((tab) => tab.textContent)).toEqual(['Categorieën'])
  })

  it('explains the missing permission instead of rendering an empty page', () => {
    permissions.value = new Set()
    renderAt('/issued-items/templates')
    expect(screen.getByText('Je hebt geen rechten om bedrijfsmiddelen te beheren.')).toBeInTheDocument()
  })
})
