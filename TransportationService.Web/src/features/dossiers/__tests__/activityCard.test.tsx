import { describe, expect, it, vi } from 'vitest'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ActivityCard } from '../components/ActivityCard'
import { ActivityList } from '../components/ActivityList'
import type { DossierActivity, DossierActivityAssignment } from '../types'
import { dossierActivity, dossierDetail } from './fixtures'

/**
 * Master sprint 2026-09-21 (spec §16) — the activity card shows what the DTO states and nothing
 * else: real driver / plate / price / CMR / note preview, and an explicit "missing" for whatever
 * is not there. A missing price is never € 0,00.
 */

function assignment(overrides: Partial<DossierActivityAssignment> = {}): DossierActivityAssignment {
  return {
    tripId: 'trip-1',
    tripNumber: 'RIT-0007',
    tripDate: '2026-09-22',
    tripStatus: 'Draft',
    driverId: 'd-jan',
    driverName: 'Jan Peeters',
    vehicleId: 'v-1',
    vehicleNumber: 'V-101',
    vehiclePlate: '1-ABC-123',
    vehicleSelectionSource: 'Manual',
    trailerId: null,
    trailerNumber: null,
    trailerPlate: null,
    tripCount: 1,
    otherOrderCount: 0,
    ...overrides,
  }
}

const order = { linkedTransportOrderId: 'o-6', linkedOrderNumber: '0006', linkedOrderStatus: 'Confirmed' }

function renderCard(activity: DossierActivity, handlers: Partial<Parameters<typeof ActivityCard>[0]> = {}) {
  const onOpen = vi.fn()
  render(
    <ul>
      <ActivityCard activity={activity} activities={[activity]} dossier={dossierDetail({ dossierNumber: 'DOS-0042' })} onOpen={onOpen} {...handlers} />
    </ul>,
  )
  return { onOpen, card: within(screen.getByRole('listitem')) }
}

/** The value cell next to a label of the info grid. */
function cell(card: ReturnType<typeof within>, label: string): HTMLElement {
  return card.getByText(label).parentElement!.querySelector('dd')!
}

describe('ActivityCard', () => {
  it('shows the real driver, plate, price, CMR number and note preview', async () => {
    const user = userEvent.setup()
    const onOpenDetail = vi.fn()
    const activity = dossierActivity({
      ...order,
      assignment: assignment(),
      agreedPrice: 450,
      isPriced: true,
      priceStatus: 'Priced',
      issuedDocuments: [{ id: 'doc-1', kind: 'Cmr', documentNumber: 'CMR-2026-00051' }],
      documentCount: 1,
      noteCount: 2,
      latestNotePreview: 'Klant belt een uur vooraf.',
    })
    const { card, onOpen } = renderCard(activity, { onOpenDetail })

    expect(cell(card, 'Dossier')).toHaveTextContent('DOS-0042')
    expect(cell(card, 'CMR / document')).toHaveTextContent('CMR-2026-00051')
    expect(cell(card, 'Chauffeur')).toHaveTextContent('Jan Peeters')
    expect(cell(card, 'Kenteken')).toHaveTextContent('1-ABC-123')
    expect(cell(card, 'Kenteken')).not.toHaveTextContent('voorgesteld')
    expect(cell(card, 'Verkoopprijs')).toHaveTextContent(/€\s450,00/)
    expect(cell(card, 'Prijsstatus')).toHaveTextContent('Geprijsd')
    expect(card.getByText('Klant belt een uur vooraf.')).toBeInTheDocument()
    expect(card.getByText('2 notities')).toBeInTheDocument()
    // A trip of its own: no shared-trip hint.
    expect(card.queryByText(/samen met/)).not.toBeInTheDocument()

    // Header: number + status left, [Openen] right — and it still opens the activity.
    expect(card.getByText('0006')).toBeInTheDocument()
    await user.click(card.getByRole('button', { name: 'Openen' }))
    expect(onOpen).toHaveBeenCalledWith(activity)

    // The note preview opens the activity on its notes, "Planning" on its planning block.
    await user.click(card.getByRole('button', { name: /Klant belt een uur vooraf/ }))
    expect(onOpenDetail).toHaveBeenLastCalledWith(activity, 'notes')
    await user.click(card.getByRole('button', { name: 'Planning' }))
    expect(onOpenDetail).toHaveBeenLastCalledWith(activity, 'planning')
  })

  it('shows every missing value as missing — and never € 0,00 for a missing price', () => {
    // An unpriced order still carries a 0 amount on some payloads: provenance decides, not the number.
    const { card } = renderCard(dossierActivity({ ...order, agreedPrice: 0, isPriced: false, priceStatus: 'NotPriced' }))

    expect(cell(card, 'Chauffeur')).toHaveTextContent('Niet toegewezen')
    expect(cell(card, 'Kenteken')).toHaveTextContent('—')
    expect(cell(card, 'CMR / document')).toHaveTextContent('Nog niet aangemaakt')
    expect(cell(card, 'Verkoopprijs')).toHaveTextContent('Nog niet geprijsd')
    expect(cell(card, 'Prijsstatus')).toHaveTextContent('Niet geprijsd')
    expect(screen.getByRole('listitem')).not.toHaveTextContent(/€\s?0,00/)
    // No notes → no preview row at all.
    expect(card.queryByText(/notitie/)).not.toBeInTheDocument()
  })

  it('falls back to the provenance flag on a payload without priceStatus', () => {
    const { card } = renderCard(dossierActivity({ ...order, agreedPrice: 0, isPriced: false }))
    expect(cell(card, 'Verkoopprijs')).toHaveTextContent('Nog niet geprijsd')
    expect(screen.getByRole('listitem')).not.toHaveTextContent(/€\s?0,00/)
  })

  it('shows "Gratis" for an activity that was confirmed as free', () => {
    const { card } = renderCard(
      dossierActivity({ hasStops: false, activityTypeName: 'Opslag', agreedPrice: 0, isPriced: true, priceStatus: 'Free', freeConfirmed: true }),
    )
    expect(cell(card, 'Verkoopprijs')).toHaveTextContent('Gratis')
    expect(cell(card, 'Prijsstatus')).toHaveTextContent('Gratis')
    expect(screen.getByRole('listitem')).not.toHaveTextContent(/€/)
  })

  it('marks a vehicle that was only proposed as "voorgesteld"', () => {
    const { card } = renderCard(dossierActivity({ ...order, assignment: assignment({ vehicleSelectionSource: 'Suggested' }) }))
    expect(cell(card, 'Kenteken')).toHaveTextContent('1-ABC-123')
    expect(cell(card, 'Kenteken')).toHaveTextContent('voorgesteld')
  })

  it('says when the trip is shared with other orders', () => {
    const first = renderCard(dossierActivity({ ...order, assignment: assignment({ otherOrderCount: 2 }) }))
    expect(first.card.getByText('Rit RIT-0007 · samen met 2 andere opdrachten')).toBeInTheDocument()
  })

  it('uses the singular for one other order', () => {
    const shared = renderCard(dossierActivity({ ...order, assignment: assignment({ otherOrderCount: 1 }) }))
    expect(shared.card.getByText('Rit RIT-0007 · samen met 1 andere opdracht')).toBeInTheDocument()
  })

  it('counts several issued documents and links to the Documenten tab of the order', async () => {
    const user = userEvent.setup()
    const onOpenDocuments = vi.fn()
    const activity = dossierActivity({
      ...order,
      issuedDocuments: [
        { id: 'doc-1', kind: 'Cmr', documentNumber: 'CMR-2026-00051' },
        { id: 'doc-2', kind: 'Cmr', documentNumber: 'CMR-2026-00052' },
      ],
    })
    const { card } = renderCard(activity, { onOpenDocuments })
    const status = within(cell(card, 'CMR / document')).getByRole('button')
    expect(status).toHaveTextContent('2')
    expect(status).not.toHaveTextContent('CMR-2026-00051')
    await user.click(status)
    expect(onOpenDocuments).toHaveBeenCalledWith(activity)
  })

  it('shows two activities with two different drivers independently', () => {
    const activities = [
      dossierActivity({ id: 'a-1', sequence: 1, linkedTransportOrderId: 'o-5', linkedOrderNumber: '0005', linkedOrderStatus: 'Confirmed', assignment: assignment() }),
      dossierActivity({
        id: 'a-2',
        sequence: 2,
        ...order,
        assignment: assignment({ tripId: 'trip-2', tripNumber: 'RIT-0008', driverId: 'd-els', driverName: 'Els Janssens', vehicleId: 'v-2', vehiclePlate: '2-XYZ-987' }),
      }),
      dossierActivity({ id: 'a-3', sequence: 3, linkedTransportOrderId: 'o-7', linkedOrderNumber: '0007', linkedOrderStatus: 'Draft' }),
    ]
    render(<ActivityList activities={activities} dossier={dossierDetail({ activities })} canManage={false} onOpen={vi.fn()} onAdd={vi.fn()} />)

    const cards = screen.getAllByRole('listitem')
    expect(cards).toHaveLength(3)
    expect(cell(within(cards[0]), 'Chauffeur')).toHaveTextContent('Jan Peeters')
    expect(cell(within(cards[0]), 'Kenteken')).toHaveTextContent('1-ABC-123')
    expect(cell(within(cards[1]), 'Chauffeur')).toHaveTextContent('Els Janssens')
    expect(cell(within(cards[1]), 'Kenteken')).toHaveTextContent('2-XYZ-987')
    // The third activity is not on a trip: it borrows nothing from its neighbours.
    expect(cell(within(cards[2]), 'Chauffeur')).toHaveTextContent('Niet toegewezen')
    expect(cell(within(cards[2]), 'Kenteken')).toHaveTextContent('—')
  })
})
