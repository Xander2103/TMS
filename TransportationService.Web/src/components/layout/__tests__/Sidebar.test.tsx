import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
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
      <Sidebar onNavigate={vi.fn()} />
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

  it('hides modules the user has no permission for, keeps ungated Communicatie', () => {
    renderSidebar()
    expect(screen.queryByRole('button', { name: /Beheer/ })).toBeNull()
    expect(screen.getByRole('button', { name: /Communicatie/ })).toBeInTheDocument()
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
        <Sidebar onNavigate={onNavigate} />
      </MemoryRouter>,
    )
    await userEvent.click(screen.getByRole('link', { name: 'Voertuigen' }))
    expect(onNavigate).toHaveBeenCalled()
  })
})
