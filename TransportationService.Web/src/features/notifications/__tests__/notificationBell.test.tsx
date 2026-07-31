import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import * as api from '../api/notificationsApi'
import type { Notification } from '../api/notificationsApi'
import { NotificationBell } from '../components/NotificationBell'
import { NotificationsProvider } from '../notificationsContext'

function makeNotification(overrides: Partial<Notification> = {}): Notification {
  return {
    id: 'n1',
    type: 'OrderCreated',
    category: 'Orders',
    severity: 'Info',
    title: 'Nieuwe opdracht',
    message: 'Er is een nieuwe opdracht aangemaakt.',
    linkPath: '/transport-orders/1',
    isRead: false,
    isArchived: false,
    createdAt: new Date(Date.now() - 5 * 60_000).toISOString(),
    requiresAcknowledgement: false,
    acknowledgedAt: null,
    resolvedAt: null,
    expiresAt: null,
    ...overrides,
  }
}

function renderBell() {
  return render(
    <MemoryRouter>
      <NotificationsProvider>
        <NotificationBell />
      </NotificationsProvider>
    </MemoryRouter>,
  )
}

beforeEach(() => {
  vi.restoreAllMocks()
  vi.spyOn(api, 'getUnreadCount').mockResolvedValue({ count: 3 })
  vi.spyOn(api, 'listNotifications').mockResolvedValue([])
  vi.spyOn(api, 'markNotificationRead').mockResolvedValue(undefined)
  vi.spyOn(api, 'markAllNotificationsRead').mockResolvedValue(undefined)
})

describe('NotificationBell', () => {
  it('shows the unread count from the shared provider as a badge', async () => {
    renderBell()
    expect(await screen.findByText('3')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Meldingen' })).toHaveAttribute('aria-expanded', 'false')
  })

  it('caps the badge at 99+', async () => {
    vi.spyOn(api, 'getUnreadCount').mockResolvedValue({ count: 150 })
    renderBell()
    expect(await screen.findByText('99+')).toBeInTheDocument()
  })

  it('opens the dropdown with the most recent notifications (take: 8)', async () => {
    vi.spyOn(api, 'listNotifications').mockResolvedValue([
      makeNotification({ id: 'n1', title: 'Nieuwe opdracht' }),
      makeNotification({ id: 'n2', title: 'Rit vertraagd', isRead: true }),
    ])
    renderBell()
    await userEvent.click(screen.getByRole('button', { name: 'Meldingen' }))
    expect(await screen.findByText('Nieuwe opdracht')).toBeInTheDocument()
    expect(screen.getByText('Rit vertraagd')).toBeInTheDocument()
    expect(screen.getAllByText('5 min geleden')).toHaveLength(2)
    expect(api.listNotifications).toHaveBeenCalledWith({ take: 8 })
    expect(screen.getByRole('button', { name: 'Meldingen' })).toHaveAttribute('aria-expanded', 'true')
  })

  it('closes on Escape and returns focus to the bell', async () => {
    vi.spyOn(api, 'listNotifications').mockResolvedValue([makeNotification()])
    renderBell()
    const bell = screen.getByRole('button', { name: 'Meldingen' })
    await userEvent.click(bell)
    expect(await screen.findByText('Nieuwe opdracht')).toBeInTheDocument()
    await userEvent.keyboard('{Escape}')
    expect(screen.queryByText('Nieuwe opdracht')).not.toBeInTheDocument()
    expect(bell).toHaveFocus()
    expect(bell).toHaveAttribute('aria-expanded', 'false')
  })

  it('marks everything read from the footer and refreshes the count', async () => {
    renderBell()
    await userEvent.click(screen.getByRole('button', { name: 'Meldingen' }))
    expect(await screen.findByText('Geen meldingen.')).toBeInTheDocument()
    const callsBefore = vi.mocked(api.getUnreadCount).mock.calls.length
    await userEvent.click(screen.getByRole('button', { name: 'Alles gelezen' }))
    expect(api.markAllNotificationsRead).toHaveBeenCalledTimes(1)
    expect(vi.mocked(api.getUnreadCount).mock.calls.length).toBeGreaterThan(callsBefore)
  })

  it('marks a clicked item read and closes the dropdown', async () => {
    vi.spyOn(api, 'listNotifications').mockResolvedValue([makeNotification({ id: 'n7' })])
    renderBell()
    await userEvent.click(screen.getByRole('button', { name: 'Meldingen' }))
    await userEvent.click(await screen.findByText('Nieuwe opdracht'))
    expect(api.markNotificationRead).toHaveBeenCalledWith('n7')
    expect(screen.queryByText('Nieuwe opdracht')).not.toBeInTheDocument()
  })
})
