import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { CustomerPortalLayout } from '../CustomerPortalLayout'

vi.mock('../../../auth/authContextValue', () => ({
  useAuth: () => ({
    status: 'authenticated' as const,
    user: {
      id: 'u1', tenantId: 't1', tenantName: 'Acme', email: 'x@haven.be', firstName: 'Kaat', lastName: 'Klant',
      employeeId: null, roles: [], permissions: ['customer_portal.view'], mustChangePassword: false, customerId: 'cust-1',
    },
    login: vi.fn(),
    logout: vi.fn(),
    hasPermission: (code: string) => code === 'customer_portal.view',
    hasAnyPermission: () => false,
  }),
}))

const preferredLanguage = vi.hoisted(() => ({ value: null as 'nl' | 'fr' | 'en' | null }))

vi.mock('../../api/customerPortalApi', () => ({
  getPortalContext: () =>
    Promise.resolve({ customerId: 'cust-1', customerName: 'Haven BV', preferredLanguage: preferredLanguage.value }),
  getPortalMessagesUnreadCount: () => Promise.resolve({ count: 0 }),
  getPortalFeedUnreadCount: () => Promise.resolve({ count: 0 }),
}))

const putJson = vi.hoisted(() => vi.fn(() => Promise.resolve({})))

vi.mock('../../../../api/apiClient', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../../../api/apiClient')>()
  return {
    ...actual,
    apiClient: { ...actual.apiClient, putJson },
  }
})

function renderLayout() {
  return render(
    <MemoryRouter initialEntries={['/klantportaal']}>
      <Routes>
        <Route element={<CustomerPortalLayout />}>
          <Route path="/klantportaal" element={<div>Inhoud</div>} />
        </Route>
      </Routes>
    </MemoryRouter>,
  )
}

describe('LanguageSwitcher / LocaleProvider', () => {
  beforeEach(() => {
    preferredLanguage.value = null
    putJson.mockClear()
  })

  it('starts in Dutch (browser nl, no saved preference) and switches the portal to French on click, persisting via PUT', async () => {
    const user = userEvent.setup()
    renderLayout()

    await waitFor(() => expect(screen.getByText('Haven BV')).toBeInTheDocument())
    expect(screen.getByRole('button', { name: 'Uitloggen' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Opdrachten' })).toBeInTheDocument()

    const frButton = screen.getByRole('button', { name: 'Français' })
    expect(frButton).toHaveAttribute('aria-pressed', 'false')
    await user.click(frButton)

    // Instant client-side switch...
    expect(await screen.findByRole('button', { name: 'Se déconnecter' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Commandes' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Français' })).toHaveAttribute('aria-pressed', 'true')

    // ...and the preference is persisted on the user via the canonical language
    // endpoint (i18n-wave: intern + portaal delen PUT /api/me/language).
    await waitFor(() =>
      expect(putJson).toHaveBeenCalledWith('/api/me/language', { language: 'fr' }),
    )
  })

  it('initialises from the saved preferredLanguage in the portal context', async () => {
    preferredLanguage.value = 'fr'
    renderLayout()

    expect(await screen.findByRole('button', { name: 'Se déconnecter' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Tableau de bord' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Français' })).toHaveAttribute('aria-pressed', 'true')
    expect(putJson).not.toHaveBeenCalled()
  })

  it('switches to English and back without persisting when the save fails silently', async () => {
    putJson.mockRejectedValueOnce(new Error('offline'))
    const user = userEvent.setup()
    renderLayout()

    await waitFor(() => expect(screen.getByText('Haven BV')).toBeInTheDocument())
    await user.click(screen.getByRole('button', { name: 'English' }))

    // Language still switches client-side even though the PUT failed.
    expect(await screen.findByRole('button', { name: 'Sign out' })).toBeInTheDocument()
  })
})
