import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { AppLayout } from '../AppLayout'

vi.mock('../../../features/auth/authContextValue', () => ({
  useAuth: () => ({
    status: 'authenticated' as const,
    user: { id: 'u1', firstName: 'Ada', lastName: 'Byron', tenantName: 'Acme', employeeId: null, preferredLanguage: 'nl' },
    login: vi.fn(),
    logout: vi.fn(),
    hasPermission: () => false,
    hasAnyPermission: () => false,
  }),
}))
vi.mock('../DisplayPreferencesProvider', () => ({
  DisplayPreferencesProvider: ({ children }: { children: React.ReactNode }) => children,
}))
vi.mock('../displayPreferences', () => ({ useDisplayPreferences: () => ({ preferences: null }) }))
vi.mock('../../../hooks/useActionQueueSync', () => ({ useActionQueueSync: () => ({ unsyncedCount: 0 }) }))
vi.mock('../../../features/notifications/api/notificationsApi', () => ({
  getUnreadCount: vi.fn().mockResolvedValue({ count: 0 }),
  getNotifications: vi.fn().mockResolvedValue({ items: [], totalCount: 0 }),
}))
vi.mock('../../../features/legal-entities/api/legalEntitiesApi', () => ({
  getLegalEntityOptions: vi.fn().mockResolvedValue([]),
  getActiveLegalEntity: vi.fn().mockResolvedValue({ legalEntityId: null }),
  setActiveLegalEntity: vi.fn(),
}))

function renderLayout() {
  return render(
    <MemoryRouter initialEntries={['/help']}>
      <Routes>
        <Route element={<AppLayout />}>
          <Route path="/help" element={<p>help center</p>} />
        </Route>
      </Routes>
    </MemoryRouter>,
  )
}

describe('AppLayout — shortcuts help', () => {
  beforeEach(() => window.localStorage.clear())

  it('opens the same dialog from the sidebar as from the ? key, and does not leave the page', async () => {
    renderLayout()
    expect(screen.queryByRole('dialog')).toBeNull()

    await userEvent.click(screen.getByRole('button', { name: 'Sneltoetsen' }))
    const fromSidebar = within(screen.getByRole('dialog', { name: 'Sneltoetsen' }))
    const listed = fromSidebar.getAllByRole('listitem').map((li) => li.textContent)
    expect(listed.length).toBeGreaterThan(0)
    expect(screen.getByText('help center')).toBeInTheDocument()

    await userEvent.click(fromSidebar.getByRole('button', { name: 'Sluiten' }))
    expect(screen.queryByRole('dialog')).toBeNull()

    fireEvent.keyDown(window, { key: '?' })
    const fromKey = within(screen.getByRole('dialog', { name: 'Sneltoetsen' }))
    expect(fromKey.getAllByRole('listitem').map((li) => li.textContent)).toEqual(listed)

    // The '?' toggle shares the state the sidebar row sets.
    fireEvent.keyDown(window, { key: '?' })
    expect(screen.queryByRole('dialog')).toBeNull()
  })

  it('closes the mobile drawer when the shortcuts row is used', async () => {
    const { container } = renderLayout()
    await userEvent.click(screen.getByRole('button', { name: 'Menu openen' }))
    expect(container.querySelector('.sidebar-open')).not.toBeNull()

    await userEvent.click(screen.getByRole('button', { name: 'Sneltoetsen' }))
    expect(container.querySelector('.sidebar-open')).toBeNull()
    expect(screen.getByRole('dialog', { name: 'Sneltoetsen' })).toBeInTheDocument()
  })
})
