import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { LookupSelect } from '../components/LookupSelect'
import { LookupFormDialog } from '../components/LookupFormDialog'
import { LookupManager } from '../components/LookupManager'
import { findLookupResource, type LookupResourceConfig } from '../lookupRegistry'
import type { LookupApi } from '../api/lookupApi'
import type { LookupItem } from '../types'

/**
 * Non-technical lookups (bedrijfsmiddelcategorieën): a name is all an HR user needs. The inline
 * create from a select asks only for the name; the admin dialog leads with name, description and
 * active and folds code + sort order under "Meer instellingen"; the list hides the code column.
 * Codes are still generated and stored exactly as before.
 */

const auth = vi.hoisted(() => ({ permissions: [] as string[] }))
const lookup = vi.hoisted(() => ({
  refresh: vi.fn(async () => {}),
  create: vi.fn(),
  options: [
    { id: 'c-1', code: 'KLEDING', name: 'Kleding' },
    { id: 'c-2', code: 'IT', name: 'IT' },
  ],
}))
const paged = vi.hoisted(() => ({ items: [] as LookupItem[] }))

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
vi.mock('../hooks/useLookupOptions', () => ({
  useLookupOptions: () => ({ options: lookup.options, isLoading: false, error: null, refresh: lookup.refresh }),
}))
vi.mock('../api/lookupApi', () => ({
  createLookupApi: () => ({ create: lookup.create, search: vi.fn() }),
}))
vi.mock('../../../hooks/usePagedQuery', () => ({
  usePagedQuery: () => ({ items: paged.items, totalCount: paged.items.length, pageSize: 25, isLoading: false, error: null, reload: vi.fn() }),
}))
vi.mock('../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }),
}))

beforeEach(() => {
  auth.permissions = []
  lookup.refresh.mockClear()
  lookup.create.mockReset()
})

const CATEGORIES = findLookupResource('issued-item-categories')!

const DEPARTMENTS: LookupResourceConfig = {
  slug: 'departments',
  title: 'navigation.lookups.departments',
  singular: 'masterData.singular.departments',
  basePath: '/api/departments',
  group: 'organisatie',
  viewPermission: 'departments.view',
  managePermission: 'departments.manage',
}

function item(overrides: Partial<LookupItem>): LookupItem {
  return { id: 'c-1', code: 'KLEDING', name: 'Kleding', description: null, isActive: true, sortOrder: 0, createdAt: '', updatedAt: '', ...overrides }
}

describe('LookupSelect — name-only inline create', () => {
  it('asks only for a name, posts code: null and selects the created row', async () => {
    auth.permissions = ['inventory.manage']
    lookup.create.mockResolvedValue({ id: 'c-9', code: 'ITMATERIAA', name: 'IT-materiaal', description: null, isActive: true, sortOrder: 0 })
    const onChange = vi.fn()
    render(
      <LookupSelect
        id="cat"
        basePath="/api/issued-item-categories"
        managePermission="inventory.manage"
        singular="masterData.singular.issued-item-categories"
        createFields="name"
        createLabel="+ Nieuwe categorie maken"
        createTitle="Nieuwe categorie"
        value={null}
        onChange={onChange}
      />,
    )

    await userEvent.click(screen.getByRole('combobox'))
    await userEvent.click(screen.getByRole('option', { name: '+ Nieuwe categorie maken' }))

    const dialog = screen.getByRole('dialog', { name: 'Nieuwe categorie' })
    expect(dialog).toBeInTheDocument()
    expect(screen.getByLabelText(/^Naam/)).toHaveFocus()
    expect(screen.queryByLabelText(/^Code/)).not.toBeInTheDocument()
    expect(screen.queryByLabelText(/Omschrijving/)).not.toBeInTheDocument()

    await userEvent.type(screen.getByLabelText(/^Naam/), 'IT-materiaal')
    await userEvent.click(screen.getByRole('button', { name: 'Toevoegen en selecteren' }))

    await waitFor(() => expect(lookup.create).toHaveBeenCalledTimes(1))
    expect(lookup.create.mock.calls[0][0]).toEqual({ code: null, name: 'IT-materiaal', description: null, isActive: true, sortOrder: 0 })
    await waitFor(() => expect(onChange).toHaveBeenCalledWith('c-9', { id: 'c-9', code: 'ITMATERIAA', name: 'IT-materiaal' }))
  })

  it('keeps the full dialog (name, code, description) for every other caller', async () => {
    auth.permissions = ['departments.manage']
    render(
      <LookupSelect basePath="/api/departments" managePermission="departments.manage" singular="masterData.singular.departments" value={null} onChange={vi.fn()} />,
    )
    await userEvent.click(screen.getByRole('combobox'))
    await userEvent.click(screen.getByRole('option', { name: '+ Nieuwe afdeling' }))
    expect(screen.getByRole('dialog', { name: 'Nieuwe afdeling' })).toBeInTheDocument()
    expect(screen.getByLabelText(/^Code/)).toBeInTheDocument()
    expect(screen.getByLabelText(/Omschrijving/)).toBeInTheDocument()
  })
})

describe('LookupFormDialog — simple form for bedrijfsmiddelcategorieën', () => {
  it('is registered as a simple lookup', () => {
    expect(CATEGORIES.simpleForm).toBe(true)
    expect(DEPARTMENTS.simpleForm).toBeUndefined()
  })

  it('leads with name, description and active; code and order wait under Meer instellingen', async () => {
    const create = vi.fn().mockResolvedValue(item({ id: 'c-9', code: 'SCHOENEN', name: 'Schoenen' }))
    const api = { create, update: vi.fn() } as unknown as LookupApi
    const onSaved = vi.fn()
    render(<LookupFormDialog config={CATEGORIES} api={api} onSaved={onSaved} onClose={vi.fn()} />)

    expect(screen.getByRole('dialog', { name: 'Nieuwe bedrijfsmiddelcategorie' })).toBeInTheDocument()
    expect(screen.getByLabelText(/^Naam/)).toHaveFocus()
    expect(screen.getByLabelText('Actief')).toBeVisible()
    expect(screen.getByLabelText(/^Code/)).not.toBeVisible()
    expect(screen.getByLabelText(/^Volgorde/)).not.toBeVisible()

    await userEvent.click(screen.getByText('Meer instellingen'))
    expect(screen.getByLabelText(/^Code/)).toBeVisible()
    expect(screen.getByLabelText(/^Volgorde/)).toBeVisible()
    expect(screen.getByText('Code en volgorde worden automatisch bepaald; pas ze alleen aan als dat nodig is.')).toBeVisible()

    await userEvent.type(screen.getByLabelText(/^Naam/), 'Schoenen')
    await userEvent.click(screen.getByRole('button', { name: 'Opslaan' }))
    await waitFor(() => expect(create).toHaveBeenCalledTimes(1))
    expect(create.mock.calls[0][0]).toMatchObject({ code: null, name: 'Schoenen', isActive: true, sortOrder: 0 })
    await waitFor(() => expect(onSaved).toHaveBeenCalledWith(expect.objectContaining({ code: 'SCHOENEN' }), true))
  })

  it('opens Meer instellingen by itself when the (folded) code is what fails', async () => {
    const update = vi.fn()
    const api = { create: vi.fn(), update } as unknown as LookupApi
    render(<LookupFormDialog config={CATEGORIES} api={api} item={item({})} onSaved={vi.fn()} onClose={vi.fn()} />)

    expect(screen.getByLabelText(/^Code/)).not.toBeVisible()
    await userEvent.click(screen.getByText('Meer instellingen'))
    await userEvent.clear(screen.getByLabelText(/^Code/))
    await userEvent.click(screen.getByText('Meer instellingen'))
    expect(screen.getByLabelText(/^Code/)).not.toBeVisible()

    await userEvent.click(screen.getByRole('button', { name: 'Opslaan' }))
    expect(update).not.toHaveBeenCalled()
    expect(screen.getByText('Code is verplicht.')).toBeVisible()
    expect(screen.getByLabelText(/^Code/)).toBeVisible()
  })

  it('keeps the classic layout (code first, no disclosure) for technical lookups', () => {
    const api = { create: vi.fn(), update: vi.fn() } as unknown as LookupApi
    render(<LookupFormDialog config={DEPARTMENTS} api={api} onSaved={vi.fn()} onClose={vi.fn()} />)
    expect(screen.getByLabelText(/^Code/)).toBeVisible()
    expect(screen.getByLabelText(/^Volgorde/)).toBeVisible()
    expect(screen.queryByText('Meer instellingen')).not.toBeInTheDocument()
  })
})

describe('LookupManager — simple lookups hide the technical code column', () => {
  it('shows Naam, Omschrijving and Status for categories, and still the Code column for departments', () => {
    auth.permissions = ['inventory.manage', 'departments.manage']
    paged.items = [item({ description: 'Werkkledij' })]

    const { unmount } = render(
      <MemoryRouter>
        <LookupManager config={CATEGORIES} />
      </MemoryRouter>,
    )
    const headers = () => screen.getAllByRole('columnheader').map((h) => h.textContent)
    expect(headers()).not.toContain('Code')
    expect(headers()).toEqual(expect.arrayContaining(['Naam', 'Omschrijving', 'Status']))
    expect(screen.queryByText('KLEDING')).not.toBeInTheDocument()
    expect(screen.getByText('Kleding')).toBeInTheDocument()
    unmount()

    render(
      <MemoryRouter>
        <LookupManager config={DEPARTMENTS} />
      </MemoryRouter>,
    )
    expect(headers()).toContain('Code')
  })
})
