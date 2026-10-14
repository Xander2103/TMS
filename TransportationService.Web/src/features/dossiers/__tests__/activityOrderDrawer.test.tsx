import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { ActivityDrawer } from '../components/ActivityDrawer'
import { dossierActivity, dossierDetail, orderDetail } from './fixtures'

const getOrder = vi.hoisted(() => vi.fn())
vi.mock('../../transport-orders/api/transportOrdersApi', () => ({ getTransportOrder: getOrder }))
vi.mock('../../auth/authContextValue', () => ({ useAuth: () => ({ hasPermission: () => false, user: { id: 'u-1' } }) }))
vi.mock('../../master-data/hooks/useLookupOptions', () => ({ useLookupOptions: () => ({ options: [] }) }))
vi.mock('../notes/DossierNotesPanel', () => ({ DossierNotesPanel: () => <p>notes</p> }))
vi.mock('../components/ActivityPlanningBlock', () => ({ ActivityPlanningBlock: () => <p>planning-block</p> }))

/** B12: "Openen" on a transport activity reads its order inside the dossier. */
describe('ActivityDrawer — order in context (B12)', () => {
  function renderDrawer(onOpenTab = vi.fn()) {
    getOrder.mockResolvedValue(orderDetail({ id: 'o-1', orderNumber: 'ORD-0023', customerReference: 'PO-9' }))
    const activity = dossierActivity({ id: 'a-1', hasStops: true, supportsGoods: true, linkedTransportOrderId: 'o-1', linkedOrderNumber: 'ORD-0023', linkedOrderStatus: 'Confirmed', activityTypeName: 'Direct transport' })
    const dossier = dossierDetail({ dossierNumber: 'DOS-0022', activities: [activity] })
    const router = createMemoryRouter([{
      path: '/dossiers/:id',
      element: (
        <ActivityDrawer dossier={dossier} activity={activity} canManage onClose={vi.fn()} onUpdated={vi.fn()} onConflict={() => false} onOpenTab={onOpenTab} />
      ),
    }], { initialEntries: ['/dossiers/d-1'] })
    render(<RouterProvider router={router} />)
    return { onOpenTab, activity }
  }

  it('names the parent dossier, the order and offers the full order page as a secondary link', async () => {
    renderDrawer()
    const dialog = screen.getByRole('dialog')
    expect(dialog).toHaveTextContent('Dossier')
    expect(dialog).toHaveTextContent('DOS-0022')
    expect(screen.getByRole('heading', { level: 3 })).toHaveTextContent('ORD-0023 · Direct transport')
    expect(screen.getByRole('link', { name: 'Volledige opdracht openen →' })).toHaveAttribute('href', '/transport-orders/o-1')
    expect(await screen.findByText('PO-9')).toBeInTheDocument()
  })

  it('shows the order slices with section jumps; editing lands on the dossier tab for this activity', async () => {
    const user = userEvent.setup()
    const { onOpenTab, activity } = renderDrawer()
    const nav = screen.getByRole('navigation', { name: 'Onderdelen' })
    for (const name of ['Overzicht', 'Stops', 'Lading', 'Prijs', 'Planning', 'Documenten', 'Notities']) {
      expect(nav).toHaveTextContent(name)
    }
    await user.click(screen.getByRole('button', { name: 'Prijs bepalen' }))
    expect(onOpenTab).toHaveBeenCalledWith('prijs', activity)
  })
})
