import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { DossiersPage } from '../pages/DossiersPage'
import { dossierListItem } from './fixtures'

const auth = vi.hoisted(() => ({ permissions: new Set<string>(['dossiers.view', 'dossiers.manage']) }))
vi.mock('../../auth/authContextValue', () => ({
  useAuth: () => ({ hasPermission: (code: string) => auth.permissions.has(code) }),
}))

const api = vi.hoisted(() => ({
  listDossiers: vi.fn(),
}))
vi.mock('../api/dossiersApi', () => api)

function renderPage() {
  const router = createMemoryRouter(
    [
      { path: '/dossiers', element: <DossiersPage /> },
      { path: '/dossiers/:id', element: <p>detail-route</p> },
    ],
    { initialEntries: ['/dossiers'] },
  )
  render(<RouterProvider router={router} />)
  return router
}

/** Kopteksten in DOM-volgorde. */
function headerTexts(): string[] {
  const table = screen.getByRole('table')
  return within(table)
    .getAllByRole('columnheader')
    .map((cell) => cell.textContent?.trim() ?? '')
}

describe('DossiersPage (lijst)', () => {
  beforeEach(() => {
    api.listDossiers.mockReset()
    api.listDossiers.mockResolvedValue([dossierListItem()])
  })

  it('toont kolommen in de vereiste volgorde en geen Titel-kolom', async () => {
    renderPage()
    await screen.findByRole('table')

    expect(headerTexts()).toEqual([
      'Dossiernr.',
      'Referentie',
      'Klant',
      'Klantnr.',
      'Prijs',
      'Verantwoordelijke',
      'Opdrachten',
      'Open incidenten',
      'Status',
    ])
    expect(screen.queryByRole('columnheader', { name: 'Titel' })).toBeNull()
    // De gegenereerde titel wordt niet meer als cel getoond.
    expect(screen.queryByText('Nexans NV — 12-08-2026')).toBeNull()
  })

  it('toont referentie, klantnummer en prijs voor een geprijsd dossier', async () => {
    api.listDossiers.mockResolvedValue([
      dossierListItem({ customerReference: 'ABC-458', customerNumber: 'K-1001', agreedPriceTotal: 485, pricedOrderCount: 1 }),
    ])
    renderPage()
    const row = (await screen.findByText('ABC-458')).closest('tr')!

    expect(within(row).getByText('K-1001')).toBeInTheDocument()
    expect(within(row).getByText(/€\s485,00/)).toBeInTheDocument()
    expect(within(row).getByText('DOS-0001')).toBeInTheDocument()
  })

  it('toont — voor ontbrekende referentie, klantnummer en prijs en nooit € 0,00 als "geen prijs"', async () => {
    api.listDossiers.mockResolvedValue([
      dossierListItem({ customerReference: null, customerNumber: null, agreedPriceTotal: null, pricedOrderCount: 0 }),
    ])
    renderPage()
    const row = (await screen.findByText('DOS-0001')).closest('tr')!

    const cells = within(row).getAllByRole('cell').map((cell) => cell.textContent?.trim())
    // Kolommen: nummer, referentie, klant, klantnr, prijs, verantwoordelijke, opdrachten, incidenten, status
    expect(cells[1]).toBe('—')
    expect(cells[3]).toBe('—')
    expect(cells[4]).toBe('—')
    expect(within(row).queryByText(/€\s0,00/)).toBeNull()
  })

  it('markeert een gedeeltelijk geprijsd dossier: het bedrag is geen dossiertotaal', async () => {
    api.listDossiers.mockResolvedValue([
      dossierListItem({ agreedPriceTotal: 450, orderCount: 2, pricedOrderCount: 1, billableActivityCount: 2, pricedActivityCount: 1 }),
    ])
    renderPage()
    const row = (await screen.findByText('DOS-0001')).closest('tr')!

    expect(within(row).getByText(/€\s450,00/)).toBeInTheDocument()
    expect(within(row).getByText('1/2 geprijsd')).toHaveAttribute(
      'title',
      'Slechts 1 van 2 activiteiten hebben een prijs; het bedrag is geen dossiertotaal.',
    )
  })

  it('toont geen markering wanneer elke factureerbare activiteit geprijsd is', async () => {
    api.listDossiers.mockResolvedValue([
      dossierListItem({ agreedPriceTotal: 900, orderCount: 2, pricedOrderCount: 2, billableActivityCount: 2, pricedActivityCount: 2 }),
    ])
    renderPage()
    const row = (await screen.findByText('DOS-0001')).closest('tr')!

    expect(within(row).getByText(/€\s900,00/)).toBeInTheDocument()
    expect(within(row).queryByText(/geprijsd/)).toBeNull()
    expect(within(row).queryByRole('img')).toBeNull()
  })

  it('toont een echte € 0,00 plus ⚠ (bewust?) wanneer het dossier op nul geprijsd is', async () => {
    api.listDossiers.mockResolvedValue([
      dossierListItem({ agreedPriceTotal: 0, pricedOrderCount: 1, billableActivityCount: 1, pricedActivityCount: 1, zeroPricedActivityCount: 1 }),
    ])
    renderPage()
    const row = (await screen.findByText('DOS-0001')).closest('tr')!

    // The amount is never replaced by the icon: both are there, the icon carries the question.
    expect(within(row).getByText(/€\s0,00/)).toBeInTheDocument()
    const icon = within(row).getByRole('img', { name: 'Verkoopprijs is € 0,00. Controleer of dit bewust is.' })
    expect(icon).toHaveTextContent('⚠')
    expect(icon).toHaveAttribute('title', 'Verkoopprijs is € 0,00. Controleer of dit bewust is.')
    expect(within(row).queryByText(/geprijsd/)).toBeNull()
  })

  it('€ 100 + € 0: toont € 100,00 zonder deelmarkering (2/2) maar mét de ⚠ voor de nul-eenheid', async () => {
    api.listDossiers.mockResolvedValue([
      dossierListItem({ agreedPriceTotal: 100, orderCount: 1, pricedOrderCount: 1, billableActivityCount: 2, pricedActivityCount: 2, zeroPricedActivityCount: 1 }),
    ])
    renderPage()
    const row = (await screen.findByText('DOS-0001')).closest('tr')!

    expect(within(row).getByText(/€\s100,00/)).toBeInTheDocument()
    expect(within(row).queryByText(/geprijsd/)).toBeNull()
    expect(within(row).getByRole('img', { name: 'Verkoopprijs is € 0,00. Controleer of dit bewust is.' })).toBeInTheDocument()
  })

  it('meervoud: twee nul-eenheden noemen het aantal in de tooltip', async () => {
    api.listDossiers.mockResolvedValue([
      dossierListItem({ agreedPriceTotal: 0, billableActivityCount: 2, pricedActivityCount: 2, zeroPricedActivityCount: 2 }),
    ])
    renderPage()
    const row = (await screen.findByText('DOS-0001')).closest('tr')!

    expect(within(row).getByRole('img', { name: 'Verkoopprijs is € 0,00 voor 2 activiteiten. Controleer of dit bewust is.' })).toBeInTheDocument()
  })

  it('zoekt server-side: de zoekterm gaat na de debounce naar listDossiers', async () => {
    const user = userEvent.setup()
    renderPage()
    await screen.findByRole('table')
    expect(api.listDossiers).toHaveBeenLastCalledWith({ search: undefined, status: undefined })

    const input = screen.getByPlaceholderText('Zoek op dossiernr., referentie, klant of klantnr…')
    await user.type(input, 'K-1001')

    await waitFor(() => {
      expect(api.listDossiers).toHaveBeenLastCalledWith({ search: 'K-1001', status: undefined })
    })
  })

  it('stuurt de statusfilter server-side mee', async () => {
    const user = userEvent.setup()
    renderPage()
    await screen.findByRole('table')

    await user.selectOptions(screen.getByLabelText('Status'), 'Closed')

    await waitFor(() => {
      expect(api.listDossiers).toHaveBeenLastCalledWith({ search: undefined, status: 'Closed' })
    })
  })

  it('navigeert naar het dossier bij klik op een rij', async () => {
    const user = userEvent.setup()
    api.listDossiers.mockResolvedValue([dossierListItem({ id: 'd-42' })])
    const router = renderPage()
    const row = (await screen.findByText('DOS-0001')).closest('tr')!

    await user.click(row)

    expect(router.state.location.pathname).toBe('/dossiers/d-42')
    expect(await screen.findByText('detail-route')).toBeInTheDocument()
  })
})
