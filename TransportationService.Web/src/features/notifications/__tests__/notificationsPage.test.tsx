import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import * as api from '../api/notificationsApi'
import type { Notification } from '../api/notificationsApi'
import { NotificationsPage } from '../pages/NotificationsPage'

vi.mock('../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }),
}))

function makeNotification(overrides: Partial<Notification> = {}): Notification {
  return {
    id: 'n1',
    type: 'General',
    category: 'General',
    severity: 'Info',
    title: 'Gewone melding',
    message: 'Inhoud van de melding.',
    linkPath: null,
    isRead: false,
    isArchived: false,
    createdAt: '2026-07-31T09:30:00Z',
    requiresAcknowledgement: false,
    acknowledgedAt: null,
    resolvedAt: null,
    expiresAt: null,
    ...overrides,
  }
}

function renderPage() {
  return render(
    <MemoryRouter>
      <NotificationsPage />
    </MemoryRouter>,
  )
}

beforeEach(() => {
  vi.restoreAllMocks()
  vi.spyOn(api, 'listNotifications').mockResolvedValue([])
  vi.spyOn(api, 'getNotificationPreferences').mockResolvedValue([])
  vi.spyOn(api, 'acknowledgeNotification').mockResolvedValue(undefined)
})

describe('NotificationsPage', () => {
  it('shows "Bevestigen" only for unacknowledged acknowledgement notifications and calls the API', async () => {
    vi.spyOn(api, 'listNotifications').mockResolvedValue([
      makeNotification({ id: 'ack-1', title: 'Bevestiging nodig', requiresAcknowledgement: true }),
      makeNotification({
        id: 'ack-2',
        title: 'Al bevestigd',
        requiresAcknowledgement: true,
        acknowledgedAt: '2026-07-31T10:00:00Z',
      }),
      makeNotification({ id: 'plain', title: 'Zonder bevestiging' }),
    ])
    renderPage()
    expect(await screen.findByText('Bevestiging nodig')).toBeInTheDocument()

    const ackButtons = screen.getAllByRole('button', { name: /bevestigen$/i })
    expect(ackButtons).toHaveLength(1)
    expect(ackButtons[0]).toHaveAccessibleName('Melding "Bevestiging nodig" bevestigen')
    expect(screen.getByText(/Bevestigd op 2026-07-31 10:00/)).toBeInTheDocument()

    await userEvent.click(ackButtons[0])
    expect(api.acknowledgeNotification).toHaveBeenCalledWith('ack-1')
    // De lijst wordt na bevestigen opnieuw geladen.
    expect(vi.mocked(api.listNotifications).mock.calls.length).toBeGreaterThan(1)
  })

  it('hides resolved notifications by default and shows them when the toggle is unchecked', async () => {
    vi.spyOn(api, 'listNotifications').mockResolvedValue([
      makeNotification({ id: 'open', title: 'Actieve melding' }),
      makeNotification({ id: 'done', title: 'Opgeloste melding', resolvedAt: '2026-07-30T12:00:00Z', isRead: true }),
    ])
    renderPage()
    expect(await screen.findByText('Actieve melding')).toBeInTheDocument()
    expect(screen.queryByText('Opgeloste melding')).not.toBeInTheDocument()

    const toggle = screen.getByLabelText(/Opgeloste verbergen/)
    expect(toggle).toBeChecked()
    await userEvent.click(toggle)
    expect(await screen.findByText('Opgeloste melding')).toBeInTheDocument()
    expect(screen.getByText('Opgelost')).toBeInTheDocument()
  })

  it('offers the new categories in the category filter', async () => {
    renderPage()
    expect(await screen.findByText('Geen meldingen.')).toBeInTheDocument()
    const filter = screen.getByLabelText('Categorie')
    for (const label of ['Voorraad', 'Taken', 'Klantportaal', 'Wagenpark', 'Documenten', 'Goedkeuringen']) {
      expect(within(filter).getByRole('option', { name: label })).toBeInTheDocument()
    }
  })
})
