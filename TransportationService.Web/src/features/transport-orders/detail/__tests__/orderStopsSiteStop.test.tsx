import { describe, expect, it } from 'vitest'
import { render, screen, within } from '@testing-library/react'
import { OrderStopsSection } from '../OrderStopsSection'
import { OrderDetailContext, type OrderDetailWorkspace } from '../orderDetailContext'
import { orderDetail } from '../../../dossiers/__tests__/fixtures'

function renderStops(order: ReturnType<typeof orderDetail>) {
  // The section only reads these members of the workspace.
  const workspace = { order, planEditable: false, busy: false, editing: false, setPlanStop: () => undefined } as unknown as OrderDetailWorkspace
  render(
    <OrderDetailContext.Provider value={workspace}>
      <OrderStopsSection />
    </OrderDetailContext.Provider>,
  )
}

describe('Order stops view — on-site lifting (D2)', () => {
  it('renders a Site stop as "Werf" with the work description, never as a loading/unloading stop', () => {
    const base = orderDetail().stops[0]
    renderStops(
      orderDetail({
        craneJobKind: 'OnSiteLifting',
        workDescription: 'Airco-unit op dak plaatsen',
        stops: [{ ...base, id: 'site-1', stopType: 'Site', locationName: 'Werf Dupont', city: 'Waver' }],
      }),
    )
    const row = screen.getByText('Werf Dupont').closest('tr')!
    expect(within(row).getByText('Werf')).toBeInTheDocument()
    expect(within(row).queryByText('Laden')).not.toBeInTheDocument()
    expect(within(row).queryByText('Lossen')).not.toBeInTheDocument()
    expect(screen.getByText('Airco-unit op dak plaatsen')).toBeInTheDocument()
  })

  it('a classic order is unchanged', () => {
    renderStops(orderDetail())
    expect(screen.getByText('Laden')).toBeInTheDocument()
    expect(screen.queryByText('Werf')).not.toBeInTheDocument()
    expect(screen.queryByText(/Omschrijving werkzaamheden/)).not.toBeInTheDocument()
  })
})
