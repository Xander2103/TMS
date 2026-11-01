import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { NotificationsProvider } from '../../../features/notifications/notificationsContext'
import { Sidebar } from '../Sidebar'

const auth = vi.hoisted(() => ({
  permissions: [] as string[],
  employeeId: null as string | null,
  userId: 'u1' as string | null,
}))

vi.mock('../../../features/auth/authContextValue', () => ({
  useAuth: () => ({
    status: 'authenticated' as const,
    user: auth.userId
      ? { id: auth.userId, firstName: 'Ada', lastName: 'Byron', tenantName: 'Acme', employeeId: auth.employeeId }
      : null,
    login: vi.fn(),
    logout: vi.fn(),
    hasPermission: (code: string) => auth.permissions.includes(code),
    hasAnyPermission: (codes: string[]) => codes.some((c) => auth.permissions.includes(c)),
  }),
}))

vi.mock('../../../features/notifications/api/notificationsApi', () => ({
  getUnreadCount: vi.fn().mockResolvedValue({ count: 0 }),
}))

vi.mock('../../../features/legal-entities/api/legalEntitiesApi', () => ({
  getLegalEntityOptions: vi.fn().mockResolvedValue([]),
  getActiveLegalEntity: vi.fn().mockResolvedValue({ legalEntityId: null }),
  setActiveLegalEntity: vi.fn(),
}))

function renderSidebar(path = '/dashboard') {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <NotificationsProvider>
        <Sidebar onNavigate={vi.fn()} onAction={vi.fn()} />
      </NotificationsProvider>
    </MemoryRouter>,
  )
}

describe('Sidebar', () => {
  beforeEach(() => {
    window.localStorage.clear()
    auth.permissions = []
    auth.employeeId = null
    auth.userId = 'u1'
  })

  it('hides modules the user has no permission for, keeps Vandaag (ungated Berichten/Meldingen)', () => {
    renderSidebar()
    expect(screen.queryByRole('button', { name: /Parameters/ })).toBeNull()
    expect(screen.getByRole('button', { name: /Vandaag/ })).toBeInTheDocument()
  })

  it('shows a permitted module and auto-expands the active one', () => {
    auth.permissions = ['vehicles.view']
    renderSidebar('/vehicles')
    const vloot = screen.getByRole('button', { name: /Vloot/ })
    expect(vloot).toHaveAttribute('aria-expanded', 'true')
    expect(screen.getByRole('link', { name: 'Voertuigen' })).toBeInTheDocument()
  })

  it('filters the menu and drops non-matching modules', async () => {
    auth.permissions = ['invoices.view', 'vehicles.view']
    renderSidebar()
    await userEvent.type(screen.getByRole('searchbox', { name: /menu/i }), 'facturen')
    expect(screen.getByRole('link', { name: 'Facturen' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Vloot/ })).toBeNull()
  })

  it('shows the portal module only when the user has an employee link', () => {
    auth.employeeId = 'emp-1'
    renderSidebar('/portal')
    expect(screen.getByRole('button', { name: /Mijn portaal/ })).toBeInTheDocument()
  })

  it('calls onNavigate when a link is clicked (drawer close)', async () => {
    auth.permissions = ['vehicles.view']
    const onNavigate = vi.fn()
    render(
      <MemoryRouter initialEntries={['/vehicles']}>
        <NotificationsProvider>
          <Sidebar onNavigate={onNavigate} onAction={vi.fn()} />
        </NotificationsProvider>
      </MemoryRouter>,
    )
    await userEvent.click(screen.getByRole('link', { name: 'Voertuigen' }))
    expect(onNavigate).toHaveBeenCalled()
  })

  describe('Help', () => {
    const helpHeader = () => screen.getByRole('button', { name: 'Help' })

    it('comes right after Parameters as the last module, collapsed by default', () => {
      auth.permissions = ['company_settings.view']
      renderSidebar()
      const modules = screen.getAllByRole('button', { expanded: false }).map((b) => b.textContent)
      expect(modules.slice(-2)).toEqual(['Parameters', 'Help'])
      expect(helpHeader()).toHaveAttribute('aria-expanded', 'false')
      expect(screen.getByRole('region', { name: 'Help', hidden: true })).toHaveAttribute('data-expanded', 'false')
    })

    it.each(['/help', '/help/planbord'])('expands and highlights Handleidingen on %s', (path) => {
      renderSidebar(path)
      expect(helpHeader()).toHaveAttribute('aria-expanded', 'true')
      expect(helpHeader().closest('li')).toHaveClass('nav-module-active')
      const link = screen.getByRole('link', { name: 'Handleidingen' })
      expect(link).toHaveAttribute('href', '/help')
      expect(link).toHaveClass('active')
    })

    it('collapses and expands like any other module', async () => {
      renderSidebar()
      await userEvent.click(helpHeader())
      expect(helpHeader()).toHaveAttribute('aria-expanded', 'true')
      await userEvent.click(helpHeader())
      expect(helpHeader()).toHaveAttribute('aria-expanded', 'false')
    })
  })
})
