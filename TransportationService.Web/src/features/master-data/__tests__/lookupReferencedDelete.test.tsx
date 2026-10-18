import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { ApiError } from '../../../api/apiClient'
import { LookupManager } from '../components/LookupManager'
import { LookupFormDialog } from '../components/LookupFormDialog'
import { findLookupResource } from '../lookupRegistry'
import type { LookupApi } from '../api/lookupApi'
import type { LookupItem } from '../types'

/**
 * Master-data remediation 2026-10-18: a lookup referenced by history cannot be deleted. The
 * server's message (counts + the "Deactiveren" hint) is shown instead of a generic error, with
 * that alternative one click away; server field errors land on their field; a dirty dialog asks
 * before it discards input.
 */

const api = vi.hoisted(() => ({ remove: vi.fn(), update: vi.fn(), create: vi.fn() }))
const toast = vi.hoisted(() => ({ showSuccess: vi.fn(), showError: vi.fn() }))
const paged = vi.hoisted(() => ({ items: [] as LookupItem[], reload: vi.fn() }))

vi.mock('../../auth/authContextValue', () => ({
  useAuth: () => ({ hasPermission: () => true, hasAnyPermission: () => true }),
}))
vi.mock('../api/lookupApi', () => ({
  createLookupApi: () => ({ remove: api.remove, update: api.update, create: api.create, search: vi.fn() }),
}))
vi.mock('../../../hooks/usePagedQuery', () => ({
  usePagedQuery: () => ({ items: paged.items, totalCount: paged.items.length, pageSize: 25, isLoading: false, error: null, reload: paged.reload }),
}))
vi.mock('../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), ...toast }),
}))

const CATEGORIES = findLookupResource('customer-categories')!
const REFERENCED =
  'Deze klantcategorie kan niet worden verwijderd omdat ze nog gebruikt wordt: 3 klanten. Gebruik "Deactiveren" om ze niet langer beschikbaar te maken.'

function item(overrides: Partial<LookupItem> = {}): LookupItem {
  return {
    id: 'cat-1', code: 'KMO', name: 'KMO', description: null, isActive: true, sortOrder: 2,
    createdAt: '2026-01-01T00:00:00Z', updatedAt: '2026-01-01T00:00:00Z', ...overrides,
  }
}

beforeEach(() => {
  vi.clearAllMocks()
  paged.items = [item()]
})

describe('LookupManager — delete refused because history references the item', () => {
  it('shows the server message and deactivates on request', async () => {
    api.remove.mockRejectedValue(
      new ApiError(REFERENCED, 409, {
        message: REFERENCED,
        code: 'common.record_referenced',
        references: [{ key: 'customers', count: 3, label: '3 klanten' }],
        canDeactivate: true,
      }),
    )
    api.update.mockResolvedValue(item({ isActive: false }))
    render(
      <MemoryRouter>
        <LookupManager config={CATEGORIES} />
      </MemoryRouter>,
    )

    await userEvent.click(screen.getByRole('button', { name: 'Verwijderen' }))
    await userEvent.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Verwijderen' }))

    const dialog = await screen.findByRole('dialog', { name: /kan niet worden verwijderd/ })
    expect(dialog).toHaveTextContent(REFERENCED)
    expect(toast.showError).not.toHaveBeenCalled()

    await userEvent.click(within(dialog).getByRole('button', { name: 'Deactiveren' }))
    await waitFor(() =>
      expect(api.update).toHaveBeenCalledWith('cat-1', { code: 'KMO', name: 'KMO', description: null, isActive: false, sortOrder: 2 }),
    )
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(paged.reload).toHaveBeenCalled()
  })
})

describe('LookupFormDialog — server rules and unsaved input', () => {
  const dialogApi = () => ({ update: api.update, create: api.create }) as unknown as LookupApi

  it('shows a 400 field error on the code field', async () => {
    api.update.mockRejectedValue(
      new ApiError('Validatie mislukt', 400, { errors: { Code: ['De code van een gebruikte eenheid kan niet worden gewijzigd.'] } }),
    )
    render(
      <MemoryRouter>
        <LookupFormDialog config={CATEGORIES} api={dialogApi()} item={item()} onSaved={vi.fn()} onClose={vi.fn()} />
      </MemoryRouter>,
    )
    const code = screen.getByLabelText(/^Code/)
    await userEvent.clear(code)
    await userEvent.type(code, 'KMO2')
    await userEvent.click(screen.getByRole('button', { name: 'Opslaan' }))

    expect(await screen.findByText('De code van een gebruikte eenheid kan niet worden gewijzigd.')).toBeInTheDocument()
    expect(code).toHaveAttribute('aria-invalid', 'true')
  })

  it('asks before Annuleren discards typed input', async () => {
    const onClose = vi.fn()
    render(
      <MemoryRouter>
        <LookupFormDialog config={CATEGORIES} api={dialogApi()} item={item()} onSaved={vi.fn()} onClose={onClose} />
      </MemoryRouter>,
    )
    await userEvent.type(screen.getByLabelText(/^Naam/), ' extra')
    await userEvent.click(screen.getByRole('button', { name: 'Annuleren' }))
    expect(onClose).not.toHaveBeenCalled()
    expect(await screen.findAllByRole('dialog')).toHaveLength(2)
  })
})
