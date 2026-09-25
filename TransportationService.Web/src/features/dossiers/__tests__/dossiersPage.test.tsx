import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { DossiersPage } from '../pages/DossiersPage'
import { weekRangeOf } from '../pages/dossierSearchParams'
import type { DossierConfirmationEvaluation, DossierListItem } from '../types'
import { dossierDetail, dossierListItem } from './fixtures'

const auth = vi.hoisted(() => ({ permissions: new Set<string>(['dossiers.view', 'dossiers.manage']) }))
vi.mock('../../auth/authContextValue', () => ({
  useAuth: () => ({ hasPermission: (code: string) => auth.permissions.has(code) }),
}))

const toast = vi.hoisted(() => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }))
vi.mock('../../../components/ui/toastContext', () => ({ useToast: () => toast }))

const api = vi.hoisted(() => ({
  searchDossiers: vi.fn(),
  getDossierConfirmation: vi.fn(),
  confirmDossier: vi.fn(),
  cancelDossier: vi.fn(),
}))
vi.mock('../api/dossiersApi', () => api)

const lookups = vi.hoisted(() => ({
  searchCustomers: vi.fn(),
  listActivityTypes: vi.fn(),
  searchDrivers: vi.fn(),
  getVehicleOptions: vi.fn(),
  getTrailerOptions: vi.fn(),
}))
vi.mock('../../customers/api/customersApi', () => ({ searchCustomers: lookups.searchCustomers }))
vi.mock('../api/activityTypesApi', () => ({ listActivityTypes: lookups.listActivityTypes }))
vi.mock('../../drivers/api/driversApi', () => ({ searchDrivers: lookups.searchDrivers }))
vi.mock('../../vehicles/api/vehiclesApi', () => ({ getVehicleOptions: lookups.getVehicleOptions }))
vi.mock('../../trailers/api/trailersApi', () => ({ getTrailerOptions: lookups.getTrailerOptions }))

function paged(items: DossierListItem[], totalCount = items.length) {
  return { items, totalCount, page: 1, pageSize: 25 }
}

function evaluation(overrides: Partial<DossierConfirmationEvaluation> = {}): DossierConfirmationEvaluation {
  return {
    dossierId: 'd-1',
    status: 'Open',
    canConfirmManually: true,
    canAutoConfirm: false,
    blockers: [],
    warnings: [],
    autoBlockers: [],
    relevantOrderIds: [],
    relevantActivityIds: [],
    summary: {
      ordersTotal: 1,
      ordersCompleted: 1,
      ordersCancelled: 0,
      ordersOpen: 0,
      deliveriesFailed: 0,
      podMissing: 0,
      activitiesExecutable: 1,
      activitiesExecuted: 1,
      billableUnits: 1,
      pricedUnits: 1,
      documents: 1,
      hasCmr: true,
      openIncidents: 0,
    },
    ...overrides,
  }
}

function renderPage(initialEntry = '/dossiers') {
  const router = createMemoryRouter(
    [
      { path: '/dossiers', element: <DossiersPage /> },
      { path: '/dossiers/:id', element: <p>detail-route</p> },
    ],
    { initialEntries: [initialEntry] },
  )
  render(<RouterProvider router={router} />)
  return router
}

/** Kopteksten in DOM-volgorde, zonder de sorteerindicatoren. */
function headerTexts(): string[] {
  const table = screen.getByRole('table')
  return within(table)
    .getAllByRole('columnheader')
    .map((cell) => (cell.textContent ?? '').replace(/[↕▲▼]/g, '').trim())
}

async function findRow(number = 'DOS-0001'): Promise<HTMLElement> {
  return (await screen.findByText(number)).closest('tr')!
}

describe('DossiersPage (lijst)', () => {
  beforeEach(() => {
    auth.permissions = new Set(['dossiers.view', 'dossiers.manage'])
    toast.showSuccess.mockReset()
    toast.showError.mockReset()
    api.searchDossiers.mockReset()
    api.searchDossiers.mockResolvedValue(paged([dossierListItem()]))
    api.getDossierConfirmation.mockReset()
    api.confirmDossier.mockReset()
    api.cancelDossier.mockReset()
    lookups.searchCustomers.mockResolvedValue({
      items: [
        { id: 'c-1', name: 'Nexans NV' },
        { id: 'c-2', name: 'Bekaert' },
      ],
      totalCount: 2,
      page: 1,
      pageSize: 200,
    })
    lookups.listActivityTypes.mockResolvedValue([{ id: 'at-1', code: 'DIRECT', name: 'Direct transport', isActive: true }])
    lookups.searchDrivers.mockResolvedValue({ items: [{ id: 'dr-1', fullName: 'Jan Peeters' }], totalCount: 1, page: 1, pageSize: 200 })
    lookups.getVehicleOptions.mockResolvedValue([{ id: 'v-1', internalNumber: 'V01', licensePlate: '1-ABC-123', brand: null, model: null }])
    lookups.getTrailerOptions.mockResolvedValue([])
  })

  it('vraagt de eerste pagina op zonder filters en toont de kolommen in volgorde', async () => {
    renderPage()
    await screen.findByRole('table')

    expect(api.searchDossiers).toHaveBeenLastCalledWith({ page: 1, pageSize: 25 })
    expect(headerTexts()).toEqual([
      'Dossiernr.',
      'Status',
      'Klant',
      'Referentie',
      'Datum',
      'Transport',
      'Chauffeur / kenteken',
      'Bevestigd',
      'Prijs',
      'Facturatie',
      '', // rijacties
    ])
    expect(screen.queryByRole('columnheader', { name: /Verantwoordelijke/ })).toBeNull()
    expect(screen.queryByRole('columnheader', { name: /Open incidenten/ })).toBeNull()
  })

  it('toont de samenvattingen: transport, chauffeur/kenteken, bevestigingsdatum en facturatiestatus', async () => {
    api.searchDossiers.mockResolvedValue(
      paged([
        dossierListItem({
          status: 'Closed',
          confirmedAt: '2026-09-20T10:00:00Z',
          confirmationSource: 'Manual',
          dossierDate: '2026-08-12',
          firstLoadingCity: 'Antwerpen',
          lastUnloadingCity: 'Gent',
          orderCount: 2,
          driverSummary: '2 chauffeurs',
          vehicleSummary: '1-ABC-123',
          invoiceStatus: 'Sent',
        }),
      ]),
    )
    renderPage()
    const row = await findRow()

    expect(within(row).getByText('Antwerpen → Gent')).toBeInTheDocument()
    expect(within(row).getByText('2 opdr.')).toBeInTheDocument()
    expect(within(row).getByText('2 chauffeurs')).toBeInTheDocument()
    expect(within(row).getByText('1-ABC-123')).toBeInTheDocument()
    expect(within(row).getByText('Bevestigd')).toBeInTheDocument()
    expect(within(row).getByText('20/09/2026')).toBeInTheDocument()
    expect(within(row).getByText('12/08/2026')).toBeInTheDocument()
    expect(within(row).getByText('Verzonden')).toBeInTheDocument()
    expect(within(row).getByText('Nexans NV')).toBeInTheDocument()
    expect(within(row).getByText('K-1001')).toBeInTheDocument()
  })

  it('toont — voor ontbrekende chauffeur, bevestiging en facturatie en een incidentbadge op het nummer', async () => {
    api.searchDossiers.mockResolvedValue(
      paged([
        dossierListItem({
          driverSummary: null,
          vehicleSummary: null,
          confirmedAt: null,
          invoiceStatus: 'NotInvoiced',
          openIncidentCount: 3,
          customerReference: null,
          agreedPriceTotal: null,
        }),
      ]),
    )
    renderPage()
    const row = await findRow()

    const cells = within(row).getAllByRole('cell').map((cell) => cell.textContent?.trim())
    // Kolommen: nummer, status, klant, referentie, datum, transport, chauffeur, bevestigd, prijs, facturatie, acties
    expect(cells[3]).toBe('—')
    expect(cells[6]).toBe('—')
    expect(cells[7]).toBe('—')
    expect(cells[8]).toBe('—')
    expect(cells[9]).toBe('—')
    expect(within(row).getByTitle('3 open incidenten')).toHaveTextContent('3')
    expect(within(row).queryByText(/€\s0,00/)).toBeNull()
  })

  it('markeert een gedeeltelijk geprijsd dossier en een echte € 0,00 met ⚠', async () => {
    api.searchDossiers.mockResolvedValue(
      paged([
        dossierListItem({ id: 'd-1', dossierNumber: 'DOS-0001', agreedPriceTotal: 450, billableActivityCount: 2, pricedActivityCount: 1 }),
        dossierListItem({ id: 'd-2', dossierNumber: 'DOS-0002', agreedPriceTotal: 0, billableActivityCount: 1, pricedActivityCount: 1, zeroPricedActivityCount: 1 }),
      ]),
    )
    renderPage()
    const partial = await findRow('DOS-0001')
    const zero = await findRow('DOS-0002')

    expect(within(partial).getByText(/€\s450,00/)).toBeInTheDocument()
    expect(within(partial).getByText('1/2 geprijsd')).toHaveAttribute(
      'title',
      'Slechts 1 van 2 activiteiten hebben een prijs; het bedrag is geen dossiertotaal.',
    )
    expect(within(zero).getByText(/€\s0,00/)).toBeInTheDocument()
    expect(within(zero).getByRole('img', { name: 'Verkoopprijs is € 0,00. Controleer of dit bewust is.' })).toHaveTextContent('⚠')
  })

  it('zoekt server-side: de zoekterm gaat na de debounce naar searchDossiers', async () => {
    const user = userEvent.setup()
    renderPage()
    await screen.findByRole('table')

    const input = screen.getByPlaceholderText('Zoek op dossiernr., opdrachtnr., referentie, klant, chauffeur, kenteken of plaats…')
    await user.type(input, 'K-1001')

    await waitFor(() => {
      expect(api.searchDossiers).toHaveBeenLastCalledWith({ search: 'K-1001', page: 1, pageSize: 25 })
    })
  })

  it('statuschip "Bevestigd" stuurt status=Closed mee en schrijft het in de URL', async () => {
    const user = userEvent.setup()
    const router = renderPage()
    await screen.findByRole('table')

    await user.click(within(screen.getByRole('group', { name: 'Status' })).getByRole('button', { name: 'Bevestigd' }))

    await waitFor(() => {
      expect(api.searchDossiers).toHaveBeenLastCalledWith({ status: 'Closed', page: 1, pageSize: 25 })
    })
    expect(router.state.location.search).toBe('?status=Closed')
    expect(within(screen.getByRole('group', { name: 'Status' })).getByRole('button', { name: 'Bevestigd' })).toHaveAttribute('aria-pressed', 'true')
  })

  it('klantkeuze stuurt customerId mee', async () => {
    const user = userEvent.setup()
    renderPage()
    await screen.findByRole('table')

    await user.click(screen.getByRole('combobox', { name: 'Klant' }))
    await user.click(await screen.findByRole('option', { name: 'Bekaert' }))

    await waitFor(() => {
      expect(api.searchDossiers).toHaveBeenLastCalledWith({ customerId: 'c-2', page: 1, pageSize: 25 })
    })
  })

  it('"Deze week" zet een maandag-t/m-zondagbereik op de dossierdatum', async () => {
    const user = userEvent.setup()
    renderPage()
    await screen.findByRole('table')

    await user.click(screen.getByRole('button', { name: 'Deze week' }))

    const { dateFrom, dateTo } = weekRangeOf(new Date())
    await waitFor(() => {
      expect(api.searchDossiers).toHaveBeenLastCalledWith({ dateFrom, dateTo, page: 1, pageSize: 25 })
    })
    expect(screen.getByLabelText('Datum van')).toHaveValue(dateFrom)
    expect(screen.getByLabelText('Datum tot')).toHaveValue(dateTo)
  })

  it('"Meer filters" toont het uitgebreide paneel en een tekstfilter wordt meegestuurd', async () => {
    const user = userEvent.setup()
    renderPage()
    await screen.findByRole('table')

    const toggle = screen.getByRole('button', { name: 'Meer filters' })
    expect(toggle).toHaveAttribute('aria-expanded', 'false')
    expect(screen.queryByLabelText('Opdrachtnr.')).toBeNull()

    await user.click(toggle)
    expect(screen.getByRole('button', { name: 'Minder filters' })).toHaveAttribute('aria-expanded', 'true')
    expect(screen.getByText('Identificatie')).toBeInTheDocument()
    expect(screen.getByText('Commercieel')).toBeInTheDocument()

    await user.type(screen.getByLabelText('Opdrachtnr.'), 'ORD-7')
    await waitFor(() => {
      expect(api.searchDossiers).toHaveBeenLastCalledWith({ orderNumber: 'ORD-7', page: 1, pageSize: 25 })
    })
  })

  it('toont actieve filterchips en ✕ wist er precies één', async () => {
    const user = userEvent.setup()
    const router = renderPage('/dossiers?status=Open&postalCode=2000')
    await screen.findByRole('table')

    const chips = screen.getByRole('list', { name: 'Actieve filters' })
    expect(within(chips).getByText('Status: Open')).toBeInTheDocument()
    expect(within(chips).getByText('Postcode: 2000')).toBeInTheDocument()

    await user.click(within(chips).getByRole('button', { name: 'Filter Postcode verwijderen' }))

    expect(router.state.location.search).toBe('?status=Open')
    await waitFor(() => {
      expect(api.searchDossiers).toHaveBeenLastCalledWith({ status: 'Open', page: 1, pageSize: 25 })
    })
    expect(within(chips).queryByText('Postcode: 2000')).toBeNull()
  })

  it('"Filters wissen" wist alle filters en de URL', async () => {
    const user = userEvent.setup()
    const router = renderPage('/dossiers?status=Open&search=nex&customerId=c-2&page=3')
    await screen.findByRole('table')

    await user.click(screen.getByRole('button', { name: 'Filters wissen' }))

    expect(router.state.location.search).toBe('')
    await waitFor(() => {
      expect(api.searchDossiers).toHaveBeenLastCalledWith({ page: 1, pageSize: 25 })
    })
    expect(screen.queryByRole('button', { name: 'Filters wissen' })).toBeNull()
  })

  it('paginering behoudt de filters in de URL', async () => {
    const user = userEvent.setup()
    api.searchDossiers.mockResolvedValue(paged([dossierListItem()], 60))
    const router = renderPage('/dossiers?status=Closed')
    await screen.findByRole('table')

    await user.click(screen.getByRole('button', { name: 'Volgende' }))

    expect(router.state.location.search).toBe('?status=Closed&page=2')
    await waitFor(() => {
      expect(api.searchDossiers).toHaveBeenLastCalledWith({ status: 'Closed', page: 2, pageSize: 25 })
    })
  })

  it('leest de begintoestand uit de URL (?status=Closed&page=2)', async () => {
    api.searchDossiers.mockResolvedValue(paged([dossierListItem()], 60))
    renderPage('/dossiers?status=Closed&page=2')
    await screen.findByRole('table')

    expect(api.searchDossiers).toHaveBeenLastCalledWith({ status: 'Closed', page: 2, pageSize: 25 })
  })

  it('klik op een sorteerbare kop stuurt sort/dir mee en wisselt de richting', async () => {
    const user = userEvent.setup()
    const router = renderPage()
    await screen.findByRole('table')

    await user.click(screen.getByRole('button', { name: /Dossiernr\./ }))
    await waitFor(() => {
      expect(api.searchDossiers).toHaveBeenLastCalledWith({ sort: 'number', dir: 'asc', page: 1, pageSize: 25 })
    })
    expect(router.state.location.search).toBe('?sort=number&dir=asc')

    await user.click(await screen.findByRole('button', { name: /Dossiernr\./ }))
    await waitFor(() => {
      expect(api.searchDossiers).toHaveBeenLastCalledWith({ sort: 'number', dir: 'desc', page: 1, pageSize: 25 })
    })
  })

  it('rijmenu "Bevestigen" opent de bevestigingsdialoog, bevestigt en herlaadt de lijst', async () => {
    const user = userEvent.setup()
    api.getDossierConfirmation.mockResolvedValue(evaluation())
    api.confirmDossier.mockResolvedValue(dossierDetail({ status: 'Closed' }))
    renderPage()
    const row = await findRow()
    const callsBefore = api.searchDossiers.mock.calls.length

    await user.click(within(row).getByRole('button', { name: 'Acties DOS-0001' }))
    await user.click(screen.getByRole('menuitem', { name: 'Bevestigen' }))

    const dialog = await screen.findByRole('dialog', { name: 'Dossier bevestigen — DOS-0001' })
    expect(api.getDossierConfirmation).toHaveBeenCalledWith('d-1')
    await within(dialog).findByText('Alle operationele onderdelen zijn afgerond.')
    await user.click(within(dialog).getByRole('button', { name: 'Bevestigen' }))

    await waitFor(() => {
      expect(api.confirmDossier).toHaveBeenCalledWith('d-1', { reason: null, acknowledgeWarnings: false, version: undefined })
    })
    expect(toast.showSuccess).toHaveBeenCalledWith('Dossier bevestigd.')
    await waitFor(() => {
      expect(api.searchDossiers.mock.calls.length).toBeGreaterThan(callsBefore)
    })
    expect(screen.queryByRole('dialog')).toBeNull()
  })

  it('rijmenu "Annuleren" vraagt een reden en annuleert het dossier', async () => {
    const user = userEvent.setup()
    api.cancelDossier.mockResolvedValue(dossierDetail({ status: 'Cancelled' }))
    renderPage()
    const row = await findRow()

    await user.click(within(row).getByRole('button', { name: 'Acties DOS-0001' }))
    await user.click(screen.getByRole('menuitem', { name: 'Annuleren' }))

    const dialog = await screen.findByRole('dialog', { name: 'Dossier annuleren — DOS-0001' })
    await user.click(within(dialog).getByRole('button', { name: 'Dossier annuleren' }))
    expect(within(dialog).getByText('Geef een reden op.')).toBeInTheDocument()
    expect(api.cancelDossier).not.toHaveBeenCalled()

    await user.type(within(dialog).getByLabelText('Reden voor annulering'), 'Klant zegt af')
    await user.click(within(dialog).getByRole('button', { name: 'Dossier annuleren' }))

    await waitFor(() => {
      expect(api.cancelDossier).toHaveBeenCalledWith('d-1', 'Klant zegt af')
    })
    expect(toast.showSuccess).toHaveBeenCalledWith('Dossier geannuleerd.')
  })

  it('rijmenu toont "Bevestigen"/"Annuleren" niet voor een bevestigd dossier', async () => {
    const user = userEvent.setup()
    api.searchDossiers.mockResolvedValue(paged([dossierListItem({ status: 'Closed' })]))
    renderPage()
    const row = await findRow()

    await user.click(within(row).getByRole('button', { name: 'Acties DOS-0001' }))

    expect(screen.getByRole('menuitem', { name: 'Openen' })).toBeInTheDocument()
    expect(screen.queryByRole('menuitem', { name: 'Bevestigen' })).toBeNull()
    expect(screen.queryByRole('menuitem', { name: 'Annuleren' })).toBeNull()
  })

  it('verbergt het rijmenu zonder dossiers.manage', async () => {
    auth.permissions = new Set(['dossiers.view'])
    renderPage()
    const row = await findRow()

    expect(within(row).queryByRole('button', { name: 'Acties DOS-0001' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Nieuw dossier' })).toBeNull()
  })

  it('toont de lege toestand zonder resultaten', async () => {
    api.searchDossiers.mockResolvedValue(paged([]))
    renderPage()

    expect(await screen.findByText('Geen dossiers gevonden. Pas de filters aan of maak een nieuw dossier.')).toBeInTheDocument()
  })

  it('navigeert naar het dossier bij klik op een rij, maar niet bij klik op het rijmenu', async () => {
    const user = userEvent.setup()
    api.searchDossiers.mockResolvedValue(paged([dossierListItem({ id: 'd-42' })]))
    const router = renderPage()
    const row = await findRow()

    await user.click(within(row).getByRole('button', { name: 'Acties DOS-0001' }))
    expect(router.state.location.pathname).toBe('/dossiers')

    await user.click(within(row).getByText('DOS-0001'))

    expect(router.state.location.pathname).toBe('/dossiers/d-42')
    expect(await screen.findByText('detail-route')).toBeInTheDocument()
  })
})
