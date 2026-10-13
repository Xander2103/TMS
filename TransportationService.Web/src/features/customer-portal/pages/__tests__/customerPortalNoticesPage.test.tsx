import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { CustomerPortalNoticesPage } from '../CustomerPortalNoticesPage'
import type { PortalFeedMessage } from '../../api/customerPortalApi'

const feed = vi.hoisted(() => ({ value: [] as PortalFeedMessage[] }))
const readSpy = vi.hoisted(() => vi.fn())
const acknowledgeSpy = vi.hoisted(() => vi.fn())

vi.mock('../../api/customerPortalApi', () => ({
  listPortalFeedMessages: () => Promise.resolve(feed.value),
  markPortalFeedMessageRead: readSpy,
  acknowledgePortalFeedMessage: acknowledgeSpy,
}))

function feedMessage(overrides: Partial<PortalFeedMessage>): PortalFeedMessage {
  return {
    id: 'n1',
    title: 'Mededeling',
    body: 'Inhoud van de mededeling.',
    language: 'nl',
    priority: 'Normal',
    displayMode: 'Notification',
    requiresAcknowledgement: false,
    relatedEntityType: null,
    relatedEntityId: null,
    publishedAt: '2026-08-01T08:00:00Z',
    expiresAt: null,
    readAt: null,
    acknowledgedAt: null,
    ...overrides,
  }
}

function renderPage() {
  return render(
    <MemoryRouter initialEntries={['/klantportaal/mededelingen']}>
      <CustomerPortalNoticesPage />
    </MemoryRouter>,
  )
}

describe('CustomerPortalNoticesPage', () => {
  beforeEach(() => {
    feed.value = []
    readSpy.mockReset().mockResolvedValue(undefined)
    acknowledgeSpy.mockReset().mockResolvedValue(undefined)
  })

  it('renders the feed with priority badges', async () => {
    feed.value = [
      feedMessage({ id: 'n1', title: 'Feestdagregeling', priority: 'Urgent' }),
      feedMessage({ id: 'n2', title: 'Nieuwe chauffeurspas', readAt: '2026-08-01T09:00:00Z' }),
    ]
    renderPage()

    expect(await screen.findByText('Feestdagregeling')).toBeInTheDocument()
    expect(screen.getByText('Nieuwe chauffeurspas')).toBeInTheDocument()
    expect(screen.getByText('Dringend')).toBeInTheDocument()
  })

  it('marks an unread message as read when opened', async () => {
    feed.value = [feedMessage({ id: 'n1', title: 'Feestdagregeling', readAt: null })]
    const user = userEvent.setup()
    renderPage()

    await user.click(await screen.findByText('Feestdagregeling'))

    await waitFor(() => expect(readSpy).toHaveBeenCalledWith('n1'))
    expect(screen.getByText('Inhoud van de mededeling.')).toBeInTheDocument()
  })

  it('does not call read again for an already read message', async () => {
    feed.value = [feedMessage({ id: 'n2', title: 'Oud bericht', readAt: '2026-08-01T09:00:00Z' })]
    const user = userEvent.setup()
    renderPage()

    await user.click(await screen.findByText('Oud bericht'))

    expect(readSpy).not.toHaveBeenCalled()
  })

  it('acknowledges a message that requires acknowledgement', async () => {
    feed.value = [feedMessage({ id: 'n3', title: 'Bevestig dit', requiresAcknowledgement: true })]
    const user = userEvent.setup()
    renderPage()

    await user.click(await screen.findByText('Bevestig dit'))
    await user.click(await screen.findByRole('button', { name: 'Bevestigen' }))

    await waitFor(() => expect(acknowledgeSpy).toHaveBeenCalledWith('n3'))
    expect(await screen.findByText(/Bevestigd op/)).toBeInTheDocument()
  })
})
