import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes, useParams } from 'react-router-dom'
import * as api from '../api/notificationsApi'
import type { Notification } from '../api/notificationsApi'
import { NotificationsPage } from '../pages/NotificationsPage'

vi.mock('../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }),
}))

const HOUR = 60 * 60 * 1000
const DAY = 24 * HOUR

function ago(ms: number): string {
  return new Date(Date.now() - ms).toISOString()
}

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

/** Today (unread order), this week (read warning), earlier (unread invoice). */
function threeGroups(): Notification[] {
  return [
    makeNotification({
      id: 'today',
      type: 'order_created',
      category: 'Orders',
      title: 'Opdracht aangemaakt',
      message: 'ORD-0014 (Klant BV) is aangemaakt.',
      linkPath: '/orders/order-1',
      createdAt: ago(1 * HOUR),
    }),
    makeNotification({
      id: 'week',
      category: 'Inventory',
      severity: 'Warning',
      title: 'Lage voorraad',
      message: 'Artikel X zit onder het minimum.',
      isRead: true,
      createdAt: ago(2 * DAY),
    }),
    makeNotification({
      id: 'earlier',
      type: 'invoice_draft_ready',
      title: 'Conceptfactuur klaar',
      message: 'Factuur FAC-1 is klaar.',
      linkPath: '/invoices/inv-1',
      createdAt: ago(30 * DAY),
    }),
  ]
}

function OrderStub() {
  const { id } = useParams()
  return <p>Order page {id}</p>
}

function renderPage(initialPath = '/notifications') {
  return render(
    <MemoryRouter initialEntries={[initialPath]}>
      <Routes>
        <Route path="/notifications" element={<NotificationsPage />} />
        <Route path="/transport-orders/:id" element={<OrderStub />} />
        <Route path="/planning" element={<p>Planbord pagina</p>} />
      </Routes>
    </MemoryRouter>,
  )
}

/** Row buttons live in the list; scoping avoids matching detail actions whose aria-label repeats the title. */
function rowButton(title: string): HTMLElement {
  return within(screen.getByLabelText('Meldingenlijst')).getByRole('button', { name: new RegExp(title) })
}

beforeEach(() => {
  vi.restoreAllMocks()
  vi.spyOn(api, 'listNotifications').mockResolvedValue([])
  vi.spyOn(api, 'getNotificationPreferences').mockResolvedValue([])
  vi.spyOn(api, 'acknowledgeNotification').mockResolvedValue(undefined)
  vi.spyOn(api, 'archiveNotification').mockResolvedValue(undefined)
  vi.spyOn(api, 'markNotificationRead').mockResolvedValue(undefined)
  vi.spyOn(api, 'markAllNotificationsRead').mockResolvedValue(undefined)
})

describe('NotificationsPage — list and grouping', () => {
  it('renders compact rows grouped as Vandaag / Deze week / Eerder with counts', async () => {
    vi.spyOn(api, 'listNotifications').mockResolvedValue(threeGroups())
    renderPage()
    expect(await screen.findByText('Opdracht aangemaakt')).toBeInTheDocument()

    expect(screen.getByRole('button', { name: 'Groep Vandaag in- of uitklappen' })).toHaveTextContent('Vandaag (1)')
    expect(screen.getByRole('button', { name: 'Groep Deze week in- of uitklappen' })).toHaveTextContent('Deze week (1)')
    expect(screen.getByRole('button', { name: 'Groep Eerder in- of uitklappen' })).toHaveTextContent('Eerder (1)')
    expect(screen.getByText('3 meldingen')).toBeInTheDocument()

    // Row shows title, message, category and a time.
    const row = rowButton('Opdracht aangemaakt')
    expect(within(row).getByText('ORD-0014 (Klant BV) is aangemaakt.')).toBeInTheDocument()
    expect(within(row).getByText('Opdrachten')).toBeInTheDocument()
    expect(row.querySelector('time')).not.toBeNull()
  })

  it('collapses and expands a group', async () => {
    vi.spyOn(api, 'listNotifications').mockResolvedValue(threeGroups())
    renderPage()
    await screen.findByText('Opdracht aangemaakt')

    const toggle = screen.getByRole('button', { name: 'Groep Vandaag in- of uitklappen' })
    expect(toggle).toHaveAttribute('aria-expanded', 'true')
    await userEvent.click(toggle)
    expect(toggle).toHaveAttribute('aria-expanded', 'false')
    expect(screen.queryByRole('button', { name: /Opdracht aangemaakt/ })).not.toBeInTheDocument()
    await userEvent.click(toggle)
    expect(screen.getByRole('button', { name: /Opdracht aangemaakt/ })).toBeInTheDocument()
  })

  it('marks unread rows visually and read rows plainly', async () => {
    vi.spyOn(api, 'listNotifications').mockResolvedValue(threeGroups())
    renderPage()
    await screen.findByText('Opdracht aangemaakt')

    expect(rowButton('Opdracht aangemaakt')).toHaveClass('is-unread')
    expect(within(rowButton('Opdracht aangemaakt')).getByText('Ongelezen')).toBeInTheDocument()
    expect(rowButton('Lage voorraad')).not.toHaveClass('is-unread')
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

  it('has no duplicate element ids and wires group toggles to their lists', async () => {
    vi.spyOn(api, 'listNotifications').mockResolvedValue(threeGroups())
    const { container } = renderPage()
    await screen.findByText('Opdracht aangemaakt')
    await userEvent.click(rowButton('Opdracht aangemaakt'))

    const ids = Array.from(container.querySelectorAll('[id]')).map((el) => el.id)
    expect(new Set(ids).size).toBe(ids.length)
    for (const toggle of screen.getAllByRole('button', { name: /in- of uitklappen/ })) {
      const controls = toggle.getAttribute('aria-controls')
      expect(controls).toBeTruthy()
      expect(container.querySelector(`#${controls}`)).not.toBeNull()
    }
  })

  it('moves focus between rows with the arrow keys', async () => {
    vi.spyOn(api, 'listNotifications').mockResolvedValue(threeGroups())
    renderPage()
    await screen.findByText('Opdracht aangemaakt')

    rowButton('Opdracht aangemaakt').focus()
    await userEvent.keyboard('{ArrowDown}')
    expect(rowButton('Lage voorraad')).toHaveFocus()
    await userEvent.keyboard('{ArrowDown}')
    expect(rowButton('Conceptfactuur klaar')).toHaveFocus()
    await userEvent.keyboard('{ArrowUp}')
    expect(rowButton('Lage voorraad')).toHaveFocus()
  })
})

describe('NotificationsPage — selection and detail panel', () => {
  it('shows an empty detail state until a row is selected, then the full notification', async () => {
    vi.spyOn(api, 'listNotifications').mockResolvedValue(threeGroups())
    renderPage()
    await screen.findByText('Opdracht aangemaakt')
    const detail = screen.getByRole('region', { name: 'Meldingdetail' })
    expect(within(detail).getByText('Geen melding geselecteerd')).toBeInTheDocument()

    await userEvent.click(rowButton('Opdracht aangemaakt'))
    expect(rowButton('Opdracht aangemaakt')).toHaveAttribute('aria-pressed', 'true')
    expect(rowButton('Opdracht aangemaakt')).toHaveClass('is-selected')
    expect(rowButton('Lage voorraad')).toHaveAttribute('aria-pressed', 'false')

    const panel = screen.getByRole('region', { name: 'Meldingdetail' })
    expect(within(panel).getByRole('heading', { level: 2, name: 'Opdracht aangemaakt' })).toBeInTheDocument()
    expect(within(panel).getByText('ORD-0014 (Klant BV) is aangemaakt.')).toBeInTheDocument()
    expect(within(panel).getByText(/Vandaag om \d{2}:\d{2}/)).toBeInTheDocument()
    expect(within(panel).getByText('order_created')).toBeInTheDocument()
    expect(within(panel).getByText('/transport-orders/order-1')).toBeInTheDocument()
    expect(within(panel).getByRole('button', { name: 'Open opdracht' })).toBeInTheDocument()
    expect(within(panel).getByRole('button', { name: 'Markeer als gelezen' })).toBeInTheDocument()
    expect(within(panel).getByRole('button', { name: 'Melding "Opdracht aangemaakt" archiveren' })).toBeInTheDocument()
    expect(within(panel).getByText('Vervolgactie')).toBeInTheDocument()

    await userEvent.click(within(panel).getByRole('button', { name: 'Detail sluiten' }))
    expect(screen.getByText('Geen melding geselecteerd')).toBeInTheDocument()
  })

  it('restores the selection from the ?id= query parameter (deep link)', async () => {
    vi.spyOn(api, 'listNotifications').mockResolvedValue(threeGroups())
    renderPage('/notifications?id=earlier')
    await screen.findAllByText('Conceptfactuur klaar')
    const panel = screen.getByRole('region', { name: 'Meldingdetail' })
    expect(within(panel).getByRole('heading', { level: 2, name: 'Conceptfactuur klaar' })).toBeInTheDocument()
    expect(within(panel).getByRole('button', { name: 'Open factuur' })).toBeInTheDocument()
    expect(within(panel).queryByText('Vervolgactie')).not.toBeInTheDocument()
  })

  it('marks the selected notification as read without leaving the page', async () => {
    vi.spyOn(api, 'listNotifications').mockResolvedValue(threeGroups())
    renderPage()
    await screen.findByText('Opdracht aangemaakt')
    await userEvent.click(rowButton('Opdracht aangemaakt'))

    await userEvent.click(screen.getByRole('button', { name: 'Markeer als gelezen' }))
    expect(api.markNotificationRead).toHaveBeenCalledWith('today')
    await waitFor(() => expect(rowButton('Opdracht aangemaakt')).not.toHaveClass('is-unread'))
    expect(screen.queryByRole('button', { name: 'Markeer als gelezen' })).not.toBeInTheDocument()
    // Still on the notifications page with the same selection.
    expect(screen.getByRole('heading', { level: 2, name: 'Opdracht aangemaakt' })).toBeInTheDocument()
  })

  it('opens the related object via the primary action (legacy /orders path rewritten) and marks it read', async () => {
    vi.spyOn(api, 'listNotifications').mockResolvedValue(threeGroups())
    renderPage()
    await screen.findByText('Opdracht aangemaakt')
    await userEvent.click(rowButton('Opdracht aangemaakt'))
    await userEvent.click(screen.getByRole('button', { name: 'Open opdracht' }))

    expect(api.markNotificationRead).toHaveBeenCalledWith('today')
    expect(await screen.findByText('Order page order-1')).toBeInTheDocument()
  })

  it('offers "Naar planbord" as follow-up for order notifications', async () => {
    vi.spyOn(api, 'listNotifications').mockResolvedValue(threeGroups())
    renderPage()
    await screen.findByText('Opdracht aangemaakt')
    await userEvent.click(rowButton('Opdracht aangemaakt'))
    await userEvent.click(screen.getByRole('button', { name: 'Naar planbord' }))
    expect(await screen.findByText('Planbord pagina')).toBeInTheDocument()
  })

  it('archives from the detail panel, reloads and clears the selection', async () => {
    vi.spyOn(api, 'listNotifications').mockResolvedValue(threeGroups())
    renderPage()
    await screen.findByText('Opdracht aangemaakt')
    await userEvent.click(rowButton('Opdracht aangemaakt'))
    const callsBefore = vi.mocked(api.listNotifications).mock.calls.length

    await userEvent.click(screen.getByRole('button', { name: 'Melding "Opdracht aangemaakt" archiveren' }))
    expect(api.archiveNotification).toHaveBeenCalledWith('today')
    await waitFor(() => expect(vi.mocked(api.listNotifications).mock.calls.length).toBeGreaterThan(callsBefore))
    expect(await screen.findByText('Geen melding geselecteerd')).toBeInTheDocument()
  })

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
    expect(within(rowButton('Bevestiging nodig')).getByText('Te bevestigen')).toBeInTheDocument()
    expect(within(rowButton('Al bevestigd')).queryByText('Te bevestigen')).not.toBeInTheDocument()

    await userEvent.click(rowButton('Al bevestigd'))
    expect(screen.queryByRole('button', { name: /bevestigen$/i })).not.toBeInTheDocument()
    // Tenant default dd/MM/yyyy HH:mm; assert shape rather than the exact hour (runner timezone).
    expect(screen.getByText(/Bevestigd op \d{2}\/\d{2}\/\d{4} \d{2}:\d{2}/)).toBeInTheDocument()

    await userEvent.click(rowButton('Bevestiging nodig'))
    const ackButton = screen.getByRole('button', { name: 'Melding "Bevestiging nodig" bevestigen' })
    await userEvent.click(ackButton)
    expect(api.acknowledgeNotification).toHaveBeenCalledWith('ack-1')
    // De lijst wordt na bevestigen opnieuw geladen.
    await waitFor(() => expect(vi.mocked(api.listNotifications).mock.calls.length).toBeGreaterThan(1))
  })
})

describe('NotificationsPage — header, filters and states', () => {
  it('shows summary counters and marks everything read from the header', async () => {
    vi.spyOn(api, 'listNotifications').mockResolvedValue(threeGroups())
    renderPage()
    await screen.findByText('Opdracht aangemaakt')

    const summary = screen.getByRole('group', { name: 'Samenvatting' })
    expect(within(summary).getByRole('button', { name: /Open/ })).toHaveTextContent('3')
    expect(within(summary).getByRole('button', { name: /Ongelezen/ })).toHaveTextContent('2')
    expect(within(summary).getByRole('button', { name: /Waarschuwingen/ })).toHaveTextContent('1')

    await userEvent.click(screen.getByRole('button', { name: 'Alles gelezen' }))
    expect(api.markAllNotificationsRead).toHaveBeenCalled()
  })

  it('filters by search, status and warnings; category and archive go to the API', async () => {
    vi.spyOn(api, 'listNotifications').mockResolvedValue(threeGroups())
    renderPage()
    await screen.findByText('Opdracht aangemaakt')

    await userEvent.type(screen.getByRole('searchbox', { name: 'Zoeken in meldingen' }), 'voorraad')
    expect(screen.getByText('1 melding')).toBeInTheDocument()
    expect(screen.queryByText('Opdracht aangemaakt')).not.toBeInTheDocument()
    await userEvent.clear(screen.getByRole('searchbox', { name: 'Zoeken in meldingen' }))

    await userEvent.selectOptions(screen.getByLabelText('Status'), 'unread')
    expect(screen.getByText('2 meldingen')).toBeInTheDocument()
    expect(screen.queryByText('Lage voorraad')).not.toBeInTheDocument()
    await userEvent.selectOptions(screen.getByLabelText('Status'), 'all')

    await userEvent.click(within(screen.getByRole('group', { name: 'Samenvatting' })).getByRole('button', { name: /Waarschuwingen/ }))
    expect(screen.getByText('1 melding')).toBeInTheDocument()
    expect(screen.getByText('Lage voorraad')).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Filters wissen' }))
    expect(screen.getByText('3 meldingen')).toBeInTheDocument()

    await userEvent.selectOptions(screen.getByLabelText('Categorie'), 'Orders')
    await waitFor(() =>
      expect(api.listNotifications).toHaveBeenLastCalledWith(expect.objectContaining({ category: 'Orders', includeArchived: false })),
    )
    await userEvent.click(screen.getByLabelText(/Archief tonen/))
    await waitFor(() => expect(api.listNotifications).toHaveBeenLastCalledWith(expect.objectContaining({ includeArchived: true })))
  })

  it('offers the new categories in the category filter', async () => {
    renderPage()
    expect(await screen.findByText('Geen meldingen.')).toBeInTheDocument()
    const filter = screen.getByLabelText('Categorie')
    for (const label of ['Voorraad', 'Taken', 'Klantportaal', 'Wagenpark', 'Documenten', 'Goedkeuringen']) {
      expect(within(filter).getByRole('option', { name: label })).toBeInTheDocument()
    }
  })

  it('shows a distinct empty state when filters exclude everything', async () => {
    vi.spyOn(api, 'listNotifications').mockResolvedValue(threeGroups())
    renderPage()
    await screen.findByText('Opdracht aangemaakt')
    await userEvent.type(screen.getByRole('searchbox', { name: 'Zoeken in meldingen' }), 'bestaat niet')
    expect(screen.getByText('Geen meldingen voor deze filters.')).toBeInTheDocument()
    // Both the filter bar and the empty state offer a reset; either works.
    await userEvent.click(screen.getAllByRole('button', { name: 'Filters wissen' })[1])
    expect(screen.getByText('Opdracht aangemaakt')).toBeInTheDocument()
  })

  it('shows an error state with retry when loading fails', async () => {
    vi.spyOn(api, 'listNotifications').mockRejectedValueOnce(new Error('boom')).mockResolvedValue(threeGroups())
    renderPage()
    expect(await screen.findByRole('alert')).toHaveTextContent('Meldingen konden niet worden geladen.')
    await userEvent.click(screen.getByRole('button', { name: 'Opnieuw proberen' }))
    expect(await screen.findByText('Opdracht aangemaakt')).toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })
})
