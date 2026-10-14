import { beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent, { type UserEvent } from '@testing-library/user-event'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { ApiError } from '../../../api/apiClient'
import { DossiersPage } from '../pages/DossiersPage'
import { monthRangeOf } from '../pages/dossierSearchParams'
import type { DossierConfirmationEvaluation, DossierListItem } from '../types'
import { dossierDetail, dossierListItem } from './fixtures'

const auth = vi.hoisted(() => ({ permissions: new Set<string>() }))
vi.mock('../../auth/authContextValue', () => ({
  useAuth: () => ({ hasPermission: (code: string) => auth.permissions.has(code), user: { id: 'u-1' } }),
}))

const toast = vi.hoisted(() => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }))
vi.mock('../../../components/ui/toastContext', () => ({ useToast: () => toast }))

const api = vi.hoisted(() => ({
  searchDossiers: vi.fn(),
  getDossierConfirmation: vi.fn(),
  confirmDossier: vi.fn(),
  cancelDossier: vi.fn(),
  reopenDossier: vi.fn(),
}))
vi.mock('../api/dossiersApi', () => api)

const lookups = vi.hoisted(() => ({
  searchCustomers: vi.fn(),
  listActivityTypes: vi.fn(),
  searchDrivers: vi.fn(),
  getVehicleOptions: vi.fn(),
  getTrailerOptions: vi.fn(),
  getCountryOptions: vi.fn(),
}))
vi.mock('../../customers/api/customersApi', () => ({ searchCustomers: lookups.searchCustomers }))
vi.mock('../api/activityTypesApi', () => ({ listActivityTypes: lookups.listActivityTypes }))
vi.mock('../../drivers/api/driversApi', () => ({ searchDrivers: lookups.searchDrivers }))
vi.mock('../../vehicles/api/vehiclesApi', () => ({ getVehicleOptions: lookups.getVehicleOptions }))
vi.mock('../../trailers/api/trailersApi', () => ({ getTrailerOptions: lookups.getTrailerOptions }))
vi.mock('../../reference/api/countriesApi', () => ({ getCountryOptions: lookups.getCountryOptions }))

const SEARCH_PLACEHOLDER = 'Zoek op dossiernr., opdrachtnr., referentie, klant, chauffeur, kenteken of plaats…'
const BASE = { page: 1, pageSize: 25 }

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

const filtersButton = () => screen.getByRole('button', { name: /^Filters( \(\d+\))?$/ })
const queryPanel = () => screen.queryByRole('region', { name: 'Uitgebreide filters' })
const chipList = () => screen.getByRole('list', { name: 'Actieve filters' })
const chipTexts = () =>
  within(chipList())
    .getAllByRole('listitem')
    .map((item) => item.querySelector('span')?.textContent)

async function openPanel(user: UserEvent): Promise<HTMLElement> {
  await user.click(filtersButton())
  return screen.getByRole('region', { name: 'Uitgebreide filters' })
}

async function pickOption(user: UserEvent, panel: HTMLElement, label: string, option: string | RegExp) {
  await user.click(within(panel).getByRole('combobox', { name: label }))
  await user.click(await screen.findByRole('option', { name: option }))
}

async function openRowMenu(user: UserEvent, number = 'DOS-0001') {
  const row = await findRow(number)
  await user.click(within(row).getByRole('button', { name: `Acties ${number}` }))
}

const menuItems = () => screen.getAllByRole('menuitem').map((item) => item.textContent)

async function expectLastSearch(params: Record<string, unknown>) {
  await waitFor(() => {
    expect(api.searchDossiers).toHaveBeenLastCalledWith({ ...params, ...BASE })
  })
}

describe('DossiersPage (lijst)', () => {
  beforeEach(() => {
    window.localStorage.clear()
    auth.permissions = new Set(['dossiers.view', 'dossiers.manage', 'dossiers.reopen'])
    toast.showSuccess.mockReset()
    toast.showError.mockReset()
    api.searchDossiers.mockReset()
    api.searchDossiers.mockResolvedValue(paged([dossierListItem()]))
    api.getDossierConfirmation.mockReset()
    api.confirmDossier.mockReset()
    api.cancelDossier.mockReset()
    api.reopenDossier.mockReset()
    lookups.searchCustomers.mockResolvedValue({
      items: [
        { id: 'c-1', name: 'Nexans NV' },
        { id: 'c-2', name: 'Bekaert' },
        { id: 'c-3', name: 'Van Caudenberg BV' },
      ],
      totalCount: 3,
      page: 1,
      pageSize: 200,
    })
    lookups.listActivityTypes.mockResolvedValue([
      { id: 'at-1', code: 'KRAANTRANSPORT', name: 'Kraantransport', isActive: true, hasStops: true },
      { id: 'at-2', code: 'KRAANWERK', name: 'Kraanwerk', isActive: true, hasStops: false },
    ])
    lookups.searchDrivers.mockResolvedValue({ items: [{ id: 'dr-1', fullName: 'Jan Peeters' }], totalCount: 1, page: 1, pageSize: 200 })
    lookups.getVehicleOptions.mockResolvedValue([{ id: 'v-1', internalNumber: 'V01', licensePlate: '1-ABC-123', brand: null, model: null }])
    lookups.getTrailerOptions.mockResolvedValue([{ id: 'tr-1', internalNumber: 'OPL-3', licensePlate: 'Q-XYZ-987' }])
    lookups.getCountryOptions.mockResolvedValue([
      { code: 'BE', alpha3: 'BEL', name: 'België', isEuMember: true },
      { code: 'FR', alpha3: 'FRA', name: 'Frankrijk', isEuMember: true },
    ])
  })

  // ---- bovenbalk --------------------------------------------------------------------------------

  describe('bovenbalk', () => {
    it('toont [ zoeken ] [ status ] [ Filters ] en vraagt de eerste pagina op zonder filters', async () => {
      renderPage()
      await screen.findByRole('table')

      const search = screen.getByPlaceholderText(SEARCH_PLACEHOLDER)
      const status = screen.getByRole('combobox', { name: 'Status' })
      const toggle = screen.getByRole('button', { name: 'Filters' })
      // Document order = visual order on desktop.
      expect(search.compareDocumentPosition(status) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
      expect(status.compareDocumentPosition(toggle) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
      expect(within(status).getAllByRole('option').map((o) => o.textContent)).toEqual(['Alle statussen', 'Open', 'Bevestigd', 'Geannuleerd'])
      expect(status).toHaveValue('')

      expect(api.searchDossiers).toHaveBeenLastCalledWith(BASE)
      expect(screen.queryByRole('list', { name: 'Actieve filters' })).toBeNull()
      expect(screen.queryByRole('button', { name: 'Filters wissen' })).toBeNull()
    })

    it('zoekt server-side: de zoekterm gaat na de debounce naar searchDossiers', async () => {
      const user = userEvent.setup()
      const router = renderPage()
      await screen.findByRole('table')

      await user.type(screen.getByPlaceholderText(SEARCH_PLACEHOLDER), 'K-1001')

      await expectLastSearch({ search: 'K-1001' })
      expect(router.state.location.search).toBe('?search=K-1001')
    })

    it.each(['DOS-0001', 'Nexans', 'K-1001', 'ABC-458'])('geeft de zoekterm "%s" ongewijzigd door, samen met de actieve filters', async (term) => {
      const user = userEvent.setup()
      renderPage('/dossiers?status=Open&postalCode=2000')
      await screen.findByRole('table')

      await user.type(screen.getByPlaceholderText(SEARCH_PLACEHOLDER), term)

      await expectLastSearch({ search: term, status: 'Open', postalCode: '2000' })
    })

    it.each([
      ['Open', 'Open'],
      ['Bevestigd', 'Closed'],
      ['Geannuleerd', 'Cancelled'],
    ])('status "%s" stuurt status=%s mee en schrijft het in de URL', async (label, status) => {
      const user = userEvent.setup()
      const router = renderPage()
      await screen.findByRole('table')

      await user.selectOptions(screen.getByRole('combobox', { name: 'Status' }), label)

      await expectLastSearch({ status })
      expect(router.state.location.search).toBe(`?status=${status}`)
      expect(chipTexts()).toEqual([label])
      expect(filtersButton()).toHaveTextContent('Filters (1)')
    })
  })

  // ---- filterpaneel -----------------------------------------------------------------------------

  describe('filterpaneel', () => {
    it('is standaard dicht — ook wanneer de URL al uitgebreide filters bevat', async () => {
      renderPage()
      await screen.findByRole('table')
      expect(filtersButton()).toHaveAttribute('aria-expanded', 'false')
      expect(queryPanel()).toBeNull()
      cleanup()

      renderPage('/dossiers?postalCode=2000&driverId=dr-1')
      await screen.findByRole('table')
      expect(filtersButton()).toHaveAttribute('aria-expanded', 'false')
      expect(queryPanel()).toBeNull()
      expect(filtersButton()).toHaveTextContent('Filters (2)')
    })

    it('opent en sluit met de knop "Filters" en toont de gegroepeerde velden', async () => {
      const user = userEvent.setup()
      renderPage()
      await screen.findByRole('table')

      const panel = await openPanel(user)
      expect(filtersButton()).toHaveAttribute('aria-expanded', 'true')
      expect(
        within(panel)
          .getAllByRole('group')
          .map((group) => group.querySelector('legend')?.textContent),
      ).toEqual(['Identificatie', 'Klant', 'Lifecycle', 'Datum', 'Transport', 'Planning', 'Locatie', 'Commercieel', 'Documenten'])
      for (const label of ['Dossiernummer', 'Opdrachtnummer', 'Klantreferentie', 'Klantnummer', 'Kenteken', 'Laadplaats', 'Losplaats', 'Postcode']) {
        expect(within(panel).getByLabelText(label)).toBeInTheDocument()
      }
      for (const label of ['Klant', 'Transporttype', 'Activiteitstype', 'Chauffeur', 'Voertuig', 'Oplegger', 'Land']) {
        // Doorzoekbare keuzelijsten, geen native <select>.
        expect(within(panel).getByRole('combobox', { name: label }).tagName).toBe('INPUT')
      }

      await user.click(filtersButton())
      expect(queryPanel()).toBeNull()
      expect(filtersButton()).toHaveAttribute('aria-expanded', 'false')
    })

    it('filters blijven actief na het sluiten van het paneel', async () => {
      const user = userEvent.setup()
      const router = renderPage()
      await screen.findByRole('table')

      const panel = await openPanel(user)
      await user.type(within(panel).getByLabelText('Postcode'), '2000')
      await expectLastSearch({ postalCode: '2000' })
      const calls = api.searchDossiers.mock.calls.length

      await user.click(filtersButton())

      expect(queryPanel()).toBeNull()
      expect(router.state.location.search).toBe('?postalCode=2000')
      expect(filtersButton()).toHaveTextContent('Filters (1)')
      expect(chipTexts()).toEqual(['Postcode: 2000'])
      expect(api.searchDossiers).toHaveBeenLastCalledWith({ postalCode: '2000', ...BASE })
      // Closing is not a filter change: no new request.
      expect(api.searchDossiers.mock.calls.length).toBe(calls)

      const reopened = await openPanel(user)
      expect(within(reopened).getByLabelText('Postcode')).toHaveValue('2000')
    })

    it('"Filters (N)" telt de actieve filters — een datumbereik telt als één — en de chips tonen ze', async () => {
      renderPage('/dossiers?status=Closed&customerId=c-3&dateFrom=2026-09-01&dateTo=2026-09-30&transportTypeId=at-1')
      await screen.findByRole('table')

      expect(filtersButton()).toHaveTextContent('Filters (4)')
      expect(filtersButton()).toHaveAttribute('data-active', 'true')
      await within(chipList()).findByText('Van Caudenberg BV')
      expect(chipTexts()).toEqual(['Bevestigd', 'Van Caudenberg BV', 'September 2026', 'Kraantransport'])
      expect(queryPanel()).toBeNull()
    })

    it('de zoekterm krijgt een eigen chip maar telt niet mee in "Filters (N)"', async () => {
      const user = userEvent.setup()
      const router = renderPage('/dossiers?search=nex&status=Open')
      await screen.findByRole('table')

      expect(filtersButton()).toHaveTextContent('Filters (1)')
      expect(chipTexts()).toEqual(['Zoekterm: nex', 'Open'])

      await user.click(within(chipList()).getByRole('button', { name: 'Zoekterm wissen' }))

      expect(router.state.location.search).toBe('?status=Open')
      expect(screen.getByPlaceholderText(SEARCH_PLACEHOLDER)).toHaveValue('')
    })

    it('✕ op een chip wist precies dat ene filter; een bereikchip wist beide grenzen', async () => {
      const user = userEvent.setup()
      const router = renderPage('/dossiers?status=Open&planningFrom=2026-09-05&planningTo=2026-09-06&postalCode=2000')
      await screen.findByRole('table')
      expect(chipTexts()).toEqual(['Open', 'Planningdatum: 05/09/2026 – 06/09/2026', 'Postcode: 2000'])

      await user.click(within(chipList()).getByRole('button', { name: 'Filter Postcode: 2000 verwijderen' }))
      expect(router.state.location.search).toBe('?status=Open&planningFrom=2026-09-05&planningTo=2026-09-06')
      await expectLastSearch({ status: 'Open', planningFrom: '2026-09-05', planningTo: '2026-09-06' })

      await user.click(within(chipList()).getByRole('button', { name: /Filter Planningdatum/ }))
      expect(router.state.location.search).toBe('?status=Open')
      await expectLastSearch({ status: 'Open' })
      expect(chipTexts()).toEqual(['Open'])
      expect(filtersButton()).toHaveTextContent('Filters (1)')
    })

    it('"Filters wissen" zet alles terug naar de standaard: filters, zoekterm en pagina', async () => {
      const user = userEvent.setup()
      const router = renderPage('/dossiers?status=Open&search=nex&customerId=c-2&hasCmr=false&page=3')
      await screen.findByRole('table')

      await user.click(screen.getByRole('button', { name: 'Filters wissen' }))

      expect(router.state.location.search).toBe('')
      await expectLastSearch({})
      expect(screen.queryByRole('button', { name: 'Filters wissen' })).toBeNull()
      expect(screen.queryByRole('list', { name: 'Actieve filters' })).toBeNull()
      expect(filtersButton()).toHaveTextContent(/^Filters$/)
      expect(screen.getByPlaceholderText(SEARCH_PLACEHOLDER)).toHaveValue('')
      expect(screen.getByRole('combobox', { name: 'Status' })).toHaveValue('')
    })

    it('een open paneel toont na "Filters wissen" lege velden', async () => {
      const user = userEvent.setup()
      renderPage('/dossiers?postalCode=2000&priceStatus=Partial')
      await screen.findByRole('table')
      const panel = await openPanel(user)
      expect(within(panel).getByLabelText('Prijsstatus')).toHaveValue('Partial')

      await user.click(screen.getByRole('button', { name: 'Filters wissen' }))

      expect(within(panel).getByLabelText('Postcode')).toHaveValue('')
      expect(within(panel).getByLabelText('Prijsstatus')).toHaveValue('')
    })
  })

  // ---- elk filter gaat server-side mee ----------------------------------------------------------

  describe('filters → GET /api/dossiers/search', () => {
    it.each([
      ['Dossiernummer', 'dossierNumber', 'DOS-7'],
      ['Opdrachtnummer', 'orderNumber', 'ORD-7'],
      ['Klantreferentie', 'customerReference', 'PO-4711'],
      ['Klantnummer', 'customerNumber', 'KL-100'],
      ['Kenteken', 'licensePlate', '1-ABC-123'],
      ['Laadplaats', 'loadingCity', 'Antwerpen'],
      ['Losplaats', 'unloadingCity', 'Gent'],
      ['Postcode', 'postalCode', '9000'],
    ])('tekstfilter %s → %s', async (label, param, value) => {
      const user = userEvent.setup()
      const router = renderPage()
      await screen.findByRole('table')
      const panel = await openPanel(user)

      await user.type(within(panel).getByLabelText(label), value)

      await expectLastSearch({ [param]: value })
      expect(router.state.location.search).toBe(`?${new URLSearchParams({ [param]: value }).toString()}`)
    })

    it.each([
      ['Klant', 'Bekaert', 'customerId', 'c-2'],
      ['Chauffeur', 'Jan Peeters', 'driverId', 'dr-1'],
      ['Voertuig', /V01/, 'vehicleId', 'v-1'],
      ['Oplegger', /OPL-3/, 'trailerId', 'tr-1'],
      ['Transporttype', 'Kraantransport', 'transportTypeId', 'at-1'],
      ['Activiteitstype', 'Kraanwerk', 'activityTypeId', 'at-2'],
      ['Land', /Frankrijk/, 'countryCode', 'FR'],
    ])('doorzoekbare keuze %s → %s', async (label, option, param, value) => {
      const user = userEvent.setup()
      renderPage()
      await screen.findByRole('table')
      const panel = await openPanel(user)

      await pickOption(user, panel, label, option)

      await expectLastSearch({ [param]: value })
    })

    it('transporttype biedt alleen types met stops aan; activiteitstype alle types', async () => {
      const user = userEvent.setup()
      renderPage()
      await screen.findByRole('table')
      const panel = await openPanel(user)

      await user.click(within(panel).getByRole('combobox', { name: 'Transporttype' }))
      await screen.findByRole('option', { name: 'Kraantransport' })
      expect(screen.queryByRole('option', { name: 'Kraanwerk' })).toBeNull()
    })

    it.each([
      ['Status', 'Bevestigd', { status: 'Closed' }],
      ['Bevestigingswijze', 'Handmatig', { confirmationSource: 'Manual' }],
      ['Bevestigingswijze', 'Automatisch', { confirmationSource: 'Automatic' }],
      ['Prijsstatus', 'Geprijsd', { priceStatus: 'Priced' }],
      ['Prijsstatus', 'Gedeeltelijk geprijsd', { priceStatus: 'Partial' }],
      ['Prijsstatus', 'Niet geprijsd', { priceStatus: 'Unpriced' }],
      ['Facturatiestatus', 'Niet gefactureerd', { invoiceStatus: 'NotInvoiced' }],
      ['Facturatiestatus', 'Verzonden', { invoiceStatus: 'Sent' }],
      ['CMR', 'Aanwezig', { hasCmr: true }],
      ['CMR', 'Ontbreekt', { hasCmr: false }],
    ])('keuzefilter %s = %s', async (label, option, expected) => {
      const user = userEvent.setup()
      renderPage()
      await screen.findByRole('table')
      const panel = await openPanel(user)

      await user.selectOptions(within(panel).getByLabelText(label), option)

      await expectLastSearch(expected)
    })

    it.each([
      ['Dossierdatum', 'dateFrom', 'dateTo'],
      ['Planningdatum', 'planningFrom', 'planningTo'],
      ['Bevestigd op', 'confirmedFrom', 'confirmedTo'],
      ['Aanmaakdatum', 'createdFrom', 'createdTo'],
    ])('datumbereik %s → %s/%s', async (label, fromParam, toParam) => {
      const user = userEvent.setup()
      renderPage()
      await screen.findByRole('table')
      const panel = await openPanel(user)

      fireEvent.change(within(panel).getByLabelText(`${label} van`), { target: { value: '2026-09-01' } })
      fireEvent.change(within(panel).getByLabelText(`${label} tot`), { target: { value: '2026-09-30' } })

      await expectLastSearch({ [fromParam]: '2026-09-01', [toParam]: '2026-09-30' })
      // Each bound limits the other, so an inverted range cannot be picked.
      expect(within(panel).getByLabelText(`${label} van`)).toHaveAttribute('max', '2026-09-30')
      expect(within(panel).getByLabelText(`${label} tot`)).toHaveAttribute('min', '2026-09-01')
      expect(filtersButton()).toHaveTextContent('Filters (1)')
    })

    it('"Deze maand" zet de dossierdatum op de lopende maand', async () => {
      const user = userEvent.setup()
      renderPage()
      await screen.findByRole('table')
      const panel = await openPanel(user)

      await user.click(within(panel).getByRole('button', { name: 'Deze maand' }))

      const { dateFrom, dateTo } = monthRangeOf(new Date())
      await expectLastSearch({ dateFrom, dateTo })
      expect(within(panel).getByLabelText('Dossierdatum van')).toHaveValue(dateFrom)
      expect(within(panel).getByRole('button', { name: 'Deze maand' })).toHaveAttribute('aria-pressed', 'true')
    })

    it('combineert alle filters in één server-side verzoek', async () => {
      renderPage(
        '/dossiers?search=kraan&status=Closed&customerId=c-3&dateFrom=2026-09-01&dateTo=2026-09-30' +
          '&driverId=dr-1&transportTypeId=at-1&invoiceStatus=NotInvoiced&hasCmr=true',
      )
      await screen.findByRole('table')

      expect(api.searchDossiers).toHaveBeenLastCalledWith({
        search: 'kraan',
        status: 'Closed',
        customerId: 'c-3',
        dateFrom: '2026-09-01',
        dateTo: '2026-09-30',
        driverId: 'dr-1',
        transportTypeId: 'at-1',
        invoiceStatus: 'NotInvoiced',
        hasCmr: true,
        ...BASE,
      })
      // One request for the whole combination — never a page that is filtered again client-side.
      expect(api.searchDossiers).toHaveBeenCalledTimes(1)
      expect(filtersButton()).toHaveTextContent('Filters (7)')
    })

    it('een filterwijziging gaat terug naar pagina 1', async () => {
      const user = userEvent.setup()
      api.searchDossiers.mockResolvedValue(paged([dossierListItem()], 80))
      const router = renderPage('/dossiers?status=Closed&page=3')
      await screen.findByRole('table')
      expect(api.searchDossiers).toHaveBeenLastCalledWith({ status: 'Closed', page: 3, pageSize: 25 })

      const panel = await openPanel(user)
      await user.selectOptions(within(panel).getByLabelText('CMR'), 'Aanwezig')

      await expectLastSearch({ status: 'Closed', hasCmr: true })
      expect(router.state.location.search).toBe('?status=Closed&hasCmr=true')
    })
  })

  // ---- URL-state, paginering, sortering ---------------------------------------------------------

  describe('URL-state', () => {
    it('paginering behoudt de filters in de URL en in het verzoek', async () => {
      const user = userEvent.setup()
      api.searchDossiers.mockResolvedValue(paged([dossierListItem()], 60))
      const router = renderPage('/dossiers?status=Closed&postalCode=2000')
      await screen.findByRole('table')

      await user.click(screen.getByRole('button', { name: 'Volgende' }))

      expect(router.state.location.search).toBe('?status=Closed&postalCode=2000&page=2')
      await waitFor(() => {
        expect(api.searchDossiers).toHaveBeenLastCalledWith({ status: 'Closed', postalCode: '2000', page: 2, pageSize: 25 })
      })
      expect(filtersButton()).toHaveTextContent('Filters (2)')
    })

    it('leest de begintoestand uit de URL (?status=Closed&page=2)', async () => {
      api.searchDossiers.mockResolvedValue(paged([dossierListItem()], 60))
      renderPage('/dossiers?status=Closed&page=2')
      await screen.findByRole('table')

      expect(api.searchDossiers).toHaveBeenLastCalledWith({ status: 'Closed', page: 2, pageSize: 25 })
      expect(screen.getByRole('combobox', { name: 'Status' })).toHaveValue('Closed')
    })

    it('een ongeldige of te hoge pagina in de URL breekt de lijst niet', async () => {
      renderPage('/dossiers?page=-4')
      await screen.findByRole('table')
      expect(api.searchDossiers).toHaveBeenLastCalledWith(BASE)
      cleanup()

      api.searchDossiers.mockResolvedValue(paged([], 0))
      renderPage('/dossiers?status=Open&page=99')
      expect(await screen.findByText('Geen dossiers gevonden. Pas de filters aan of maak een nieuw dossier.')).toBeInTheDocument()
      expect(api.searchDossiers).toHaveBeenLastCalledWith({ status: 'Open', page: 99, pageSize: 25 })
    })

    it('een refresh (opnieuw laden met dezelfde URL) behoudt filters, sortering en pagina', async () => {
      const user = userEvent.setup()
      api.searchDossiers.mockResolvedValue(paged([dossierListItem()], 60))
      const router = renderPage()
      await screen.findByRole('table')
      await user.selectOptions(screen.getByRole('combobox', { name: 'Status' }), 'Bevestigd')
      const panel = await openPanel(user)
      await pickOption(user, panel, 'Klant', 'Van Caudenberg BV')
      await pickOption(user, panel, 'Transporttype', 'Kraantransport')
      await expectLastSearch({ status: 'Closed', customerId: 'c-3', transportTypeId: 'at-1' })
      const url = router.state.location.pathname + router.state.location.search
      expect(url).toBe('/dossiers?status=Closed&customerId=c-3&transportTypeId=at-1')

      cleanup()
      api.searchDossiers.mockClear()
      renderPage(url)
      await screen.findByRole('table')

      expect(api.searchDossiers).toHaveBeenLastCalledWith({ status: 'Closed', customerId: 'c-3', transportTypeId: 'at-1', ...BASE })
      expect(queryPanel()).toBeNull()
      expect(filtersButton()).toHaveTextContent('Filters (3)')
      await within(chipList()).findByText('Van Caudenberg BV')
      expect(chipTexts()).toEqual(['Bevestigd', 'Van Caudenberg BV', 'Kraantransport'])
    })

    it('browser Terug maakt een filterkeuze ongedaan', async () => {
      const user = userEvent.setup()
      const router = renderPage()
      await screen.findByRole('table')
      await user.selectOptions(screen.getByRole('combobox', { name: 'Status' }), 'Open')
      await expectLastSearch({ status: 'Open' })

      await router.navigate(-1)

      await expectLastSearch({})
      expect(screen.getByRole('combobox', { name: 'Status' })).toHaveValue('')
    })

    it('klik op een sorteerbare kop stuurt sort/dir mee, wisselt de richting en behoudt de filters', async () => {
      const user = userEvent.setup()
      const router = renderPage('/dossiers?status=Closed&postalCode=2000')
      await screen.findByRole('table')

      await user.click(screen.getByRole('button', { name: /Dossiernr\./ }))
      await expectLastSearch({ status: 'Closed', postalCode: '2000', sort: 'number', dir: 'asc' })
      expect(router.state.location.search).toBe('?status=Closed&postalCode=2000&sort=number&dir=asc')

      await user.click(await screen.findByRole('button', { name: /Dossiernr\./ }))
      await expectLastSearch({ status: 'Closed', postalCode: '2000', sort: 'number', dir: 'desc' })
      expect(filtersButton()).toHaveTextContent('Filters (2)')
    })

    it.each([
      ['Bevestigd op', 'confirmedAt'],
      ['Planningdatum', 'planningDate'],
      ['Klant', 'customer'],
      ['Status', 'status'],
      ['Dossiernummer', 'number'],
    ])('"Sorteren op" %s stuurt sort=%s mee en behoudt de filters', async (label, key) => {
      const user = userEvent.setup()
      renderPage('/dossiers?status=Closed')
      await screen.findByRole('table')

      await user.selectOptions(screen.getByRole('combobox', { name: 'Sorteren op' }), label)

      await expectLastSearch({ status: 'Closed', sort: key })
    })

    it('"Filters wissen" laat de gekozen sortering staan', async () => {
      const user = userEvent.setup()
      const router = renderPage('/dossiers?status=Open&sort=customer&dir=asc')
      await screen.findByRole('table')

      await user.click(screen.getByRole('button', { name: 'Filters wissen' }))

      expect(router.state.location.search).toBe('?sort=customer&dir=asc')
    })
  })

  // ---- tabel ------------------------------------------------------------------------------------

  describe('tabel', () => {
    it('toont de kolommen in volgorde, met een resultaattelling', async () => {
      api.searchDossiers.mockResolvedValue(paged([dossierListItem()], 42))
      renderPage()
      await screen.findByRole('table')

      expect(headerTexts()).toEqual([
        'Dossiernr.',
        'Status',
        'Klant',
        'Referentie',
        'Datum',
        'Transport',
        'Planning',
        'Prijs',
        'Facturatie',
        '', // rijacties
      ])
      expect(screen.getByText('42 dossiers')).toBeInTheDocument()
    })

    it('toont de samenvattingen: dossierdatum, transport, planning en facturatiestatus', async () => {
      api.searchDossiers.mockResolvedValue(
        paged([
          dossierListItem({
            dossierDate: '2026-08-12',
            firstLoadingCity: 'Antwerpen',
            lastUnloadingCity: 'Gent',
            orderCount: 2,
            planningDate: '2026-08-14',
            driverSummary: 'Jan Peeters',
            vehicleSummary: '1-ABC-123',
            invoiceStatus: 'Sent',
          }),
        ]),
      )
      renderPage()
      const row = await findRow()

      expect(within(row).getByText('12/08/2026')).toBeInTheDocument()
      expect(within(row).getByText('Antwerpen → Gent')).toBeInTheDocument()
      expect(within(row).getByText('2 opdr.')).toBeInTheDocument()
      expect(within(row).getByText('14/08/2026')).toBeInTheDocument()
      expect(within(row).getByText('Jan Peeters · 1-ABC-123')).toBeInTheDocument()
      expect(within(row).getByText('Verzonden')).toBeInTheDocument()
      expect(within(row).getByText('Nexans NV')).toBeInTheDocument()
      expect(within(row).getByText('K-1001')).toBeInTheDocument()
    })

    it('meerdere chauffeurs/voertuigen: toont het aantal, nooit één willekeurige naam', async () => {
      api.searchDossiers.mockResolvedValue(
        paged([dossierListItem({ planningDate: '2026-08-14', driverSummary: '2 chauffeurs', vehicleSummary: '2 voertuigen' })]),
      )
      renderPage()
      const row = await findRow()

      expect(within(row).getByText('2 chauffeurs · 2 voertuigen')).toBeInTheDocument()
    })

    it('toont — voor ontbrekende referentie, planning en facturatie en een incidentbadge op het nummer', async () => {
      api.searchDossiers.mockResolvedValue(
        paged([
          dossierListItem({
            driverSummary: null,
            vehicleSummary: null,
            planningDate: null,
            invoiceStatus: 'NotInvoiced',
            openIncidentCount: 3,
            customerReference: null,
          }),
        ]),
      )
      renderPage()
      const row = await findRow()

      const cells = within(row).getAllByRole('cell').map((cell) => cell.textContent?.trim())
      // Kolommen: nummer, status, klant, referentie, datum, transport, planning, prijs, facturatie, acties
      expect(cells[3]).toBe('—')
      expect(cells[6]).toBe('—')
      expect(cells[8]).toBe('—')
      expect(within(row).getByTitle('3 open incidenten')).toHaveTextContent('3')
    })

    it('één dossier = één regel: nummer breekt nooit, klant/transport/planning/prijs zijn samengestelde éénregelige cellen', async () => {
      api.searchDossiers.mockResolvedValue(
        paged([
          dossierListItem({
            customerName: 'Van Caudenberg Internationale Transporten NV',
            customerNumber: 'KL-0042',
            customerReference: 'Zeer lange klantreferentie voor het project in Antwerpen 2026',
            planningDate: '2026-08-14',
            driverSummary: 'Jan Peeters',
            vehicleSummary: '1-ABC-123',
            agreedPriceTotal: 100,
            billableActivityCount: 2,
            pricedActivityCount: 1,
            priceStatus: 'PartiallyPriced',
            openIncidentCount: 2,
          }),
        ]),
      )
      renderPage()
      const row = await findRow()
      const cells = within(row).getAllByRole('cell')

      // Dossiernummer op één regel; de incidentteller (B33) staat als ⚠-badge naast de status, nooit afgekapt.
      const number = cells[0].querySelector('.dossier-list-number')!
      expect(within(number as HTMLElement).getByText('DOS-0001').tagName).toBe('CODE')
      const status = cells[1].querySelector('.dossier-list-status')!
      expect(within(status as HTMLElement).getByTitle('2 open incidenten')).toHaveTextContent('⚠ 2')
      expect(row.querySelector('.dossier-list-stack')).toBeNull()

      // Klant: naam · nummer in één afkapbare regel; het nummer is het secundaire deel.
      const customer = cells[2].querySelector('.ui-truncated-text')!
      expect(within(customer as HTMLElement).getByText('Van Caudenberg Internationale Transporten NV')).toBeInTheDocument()
      expect(within(customer as HTMLElement).getByText('KL-0042')).toHaveClass('dossier-list-muted', 'dossier-list-sep')
      // Referentie, transport en planning: dezelfde éénregelige cel.
      expect(cells[3].querySelector('.ui-truncated-text')).toHaveTextContent('Zeer lange klantreferentie voor het project in Antwerpen 2026')
      const transport = cells[5].querySelector('.ui-truncated-text')!
      expect(within(transport as HTMLElement).getByText('Antwerpen → Gent')).toBeInTheDocument()
      expect(within(transport as HTMLElement).getByText('1 opdr.')).toHaveClass('dossier-list-sep')
      const planning = cells[6].querySelector('.ui-truncated-text')!
      expect(within(planning as HTMLElement).getByText('14/08/2026')).toBeInTheDocument()
      expect(within(planning as HTMLElement).getByText('Jan Peeters · 1-ABC-123')).toHaveClass('dossier-list-sep')
      // Prijs: bedrag en status naast elkaar in één nowrap-cel.
      const price = cells[7].querySelector('.dossier-list-price')!
      expect(price).toHaveTextContent(/€\s100,00/)
      expect(within(price as HTMLElement).getByText('Gedeeltelijk 1/2')).toHaveClass('dossier-list-sep')
      // Rijacties blijven aanwezig in de laatste cel.
      expect(within(cells[9]).getByRole('button', { name: 'Acties DOS-0001' })).toBeInTheDocument()
    })

    it('afgekapte waarden tonen hun volledige inhoud als tooltip; volledig zichtbare waarden niet', async () => {
      // jsdom heeft geen layout: de overloop wordt via de prototype-getters afgedwongen.
      const widths = { scroll: 0, client: 0 }
      Object.defineProperty(HTMLElement.prototype, 'scrollWidth', { configurable: true, get: () => widths.scroll })
      Object.defineProperty(HTMLElement.prototype, 'clientWidth', { configurable: true, get: () => widths.client })
      try {
        widths.scroll = 400
        widths.client = 180
        api.searchDossiers.mockResolvedValue(
          paged([
            dossierListItem({
              customerName: 'Van Caudenberg Internationale Transporten NV',
              customerNumber: 'KL-0042',
              customerReference: 'Zeer lange klantreferentie voor het project in Antwerpen 2026',
              planningDate: '2026-08-14',
              driverSummary: 'Jan Peeters',
              vehicleSummary: '1-ABC-123',
            }),
          ]),
        )
        renderPage()
        const cells = within(await findRow()).getAllByRole('cell')
        expect(cells[2].querySelector('.ui-truncated-text')).toHaveAttribute('title', 'Van Caudenberg Internationale Transporten NV · Klantnummer KL-0042')
        expect(cells[3].querySelector('.ui-truncated-text')).toHaveAttribute('title', 'Zeer lange klantreferentie voor het project in Antwerpen 2026')
        expect(cells[5].querySelector('.ui-truncated-text')).toHaveAttribute('title', 'Antwerpen → Gent · 1 opdr.')
        expect(cells[6].querySelector('.ui-truncated-text')).toHaveAttribute('title', '14/08/2026 · Jan Peeters · 1-ABC-123')
        cleanup()

        // Ruimte genoeg: geen tooltip die de zichtbare tekst herhaalt.
        widths.scroll = 100
        widths.client = 180
        renderPage()
        const wide = within(await findRow()).getAllByRole('cell')
        for (const index of [2, 3, 5, 6]) {
          expect(wide[index].querySelector('.ui-truncated-text')).not.toHaveAttribute('title')
        }
      } finally {
        Object.defineProperty(HTMLElement.prototype, 'scrollWidth', { configurable: true, get: () => 0 })
        Object.defineProperty(HTMLElement.prototype, 'clientWidth', { configurable: true, get: () => 0 })
      }
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

    it('toont de lege toestand zonder resultaten', async () => {
      api.searchDossiers.mockResolvedValue(paged([]))
      renderPage()

      expect(await screen.findByText('Geen dossiers gevonden. Pas de filters aan of maak een nieuw dossier.')).toBeInTheDocument()
      expect(screen.getByText('0 dossiers')).toBeInTheDocument()
    })
  })

  // ---- statusbenaming + bevestigingsinfo --------------------------------------------------------

  describe('status en bevestiging', () => {
    it('een Closed-dossier heet "Bevestigd" — nergens "Gesloten" of "Closed"', async () => {
      const user = userEvent.setup()
      api.searchDossiers.mockResolvedValue(
        paged([dossierListItem({ status: 'Closed', confirmedAt: '2026-09-20T10:00:00Z', confirmationSource: 'Manual' })]),
      )
      renderPage('/dossiers?status=Closed')
      const row = await findRow()
      await openPanel(user)

      expect(within(row).getByText('Bevestigd')).toBeInTheDocument()
      expect(document.body).not.toHaveTextContent(/Gesloten/)
      expect(document.body).not.toHaveTextContent(/Closed/)
    })

    it.each([
      ['Manual', 'Handmatig'],
      ['Automatic', 'Automatisch'],
    ] as const)('toont de bevestigingsdatum naast de badge op één regel; de wijze (%s) zit in de tooltip', async (source, label) => {
      api.searchDossiers.mockResolvedValue(
        paged([dossierListItem({ status: 'Closed', confirmedAt: '2026-09-20T10:00:00Z', confirmationSource: source })]),
      )
      renderPage()
      const row = await findRow()

      const cell = within(row).getAllByRole('cell')[1]
      expect(within(cell).getByText('Bevestigd')).toBeInTheDocument()
      // Eén regel: badge en datum zitten samen in de nowrap-statuscel; geen gestapelde tweede regel.
      const status = cell.querySelector('.dossier-list-status')!
      expect(within(status as HTMLElement).getByText('20/09/2026')).toHaveClass('dossier-list-sep')
      expect(cell.querySelector('.dossier-list-stack')).toBeNull()
      expect(cell).not.toHaveTextContent(label)
      expect(status).toHaveAttribute('title', expect.stringMatching(new RegExp(`^Bevestigd op 20\\/09\\/2026 .+ · ${label}$`)))
    })

    it('historisch bevestigd zonder bron: "Bron onbekend (historisch)", geen verzonnen Handmatig/Automatisch', async () => {
      api.searchDossiers.mockResolvedValue(
        paged([
          dossierListItem({ id: 'd-1', dossierNumber: 'DOS-0001', status: 'Closed', confirmedAt: '2026-03-02T08:00:00Z', confirmationSource: null }),
          dossierListItem({ id: 'd-2', dossierNumber: 'DOS-0002', status: 'Closed', confirmedAt: null, confirmationSource: null }),
        ]),
      )
      renderPage()
      const dated = within(await findRow('DOS-0001')).getAllByRole('cell')[1]
      const undated = within(await findRow('DOS-0002')).getAllByRole('cell')[1]

      expect(within(dated).getByText('02/03/2026')).toBeInTheDocument()
      expect(dated.querySelector('.dossier-list-status')).toHaveAttribute('title', expect.stringMatching(/· Bron onbekend \(historisch\)$/))
      expect(within(undated).getByText('Bron onbekend (historisch)')).toBeInTheDocument()
      for (const cell of [dated, undated]) {
        expect(cell).not.toHaveTextContent(/Handmatig|Automatisch/)
      }
    })

    it('een open of geannuleerd dossier toont geen bevestigingsregel', async () => {
      api.searchDossiers.mockResolvedValue(
        paged([
          dossierListItem({ id: 'd-1', dossierNumber: 'DOS-0001', status: 'Open' }),
          dossierListItem({ id: 'd-2', dossierNumber: 'DOS-0002', status: 'Cancelled' }),
        ]),
      )
      renderPage()

      expect(within(await findRow('DOS-0001')).getAllByRole('cell')[1]).toHaveTextContent(/^Open$/)
      expect(within(await findRow('DOS-0002')).getAllByRole('cell')[1]).toHaveTextContent(/^Geannuleerd$/)
    })
  })

  // ---- prijsweergave ----------------------------------------------------------------------------

  describe('prijs', () => {
    const priceCell = async (number: string) => within(await findRow(number)).getAllByRole('cell')[7]

    it('toont bedrag + één consistente status: Geprijsd, Gedeeltelijk, Niet geprijsd, Gratis', async () => {
      api.searchDossiers.mockResolvedValue(
        paged([
          dossierListItem({ id: 'd-1', dossierNumber: 'DOS-0001', agreedPriceTotal: 485, billableActivityCount: 1, pricedActivityCount: 1, priceStatus: 'Priced' }),
          dossierListItem({ id: 'd-2', dossierNumber: 'DOS-0002', agreedPriceTotal: 100, billableActivityCount: 2, pricedActivityCount: 1, priceStatus: 'PartiallyPriced' }),
          dossierListItem({ id: 'd-3', dossierNumber: 'DOS-0003', agreedPriceTotal: null, billableActivityCount: 1, pricedActivityCount: 0, priceStatus: 'NotPriced' }),
          dossierListItem({ id: 'd-4', dossierNumber: 'DOS-0004', agreedPriceTotal: 0, billableActivityCount: 1, pricedActivityCount: 1, priceStatus: 'Free' }),
          dossierListItem({ id: 'd-5', dossierNumber: 'DOS-0005', agreedPriceTotal: null, billableActivityCount: 0, pricedActivityCount: 0, priceStatus: null }),
        ]),
      )
      renderPage()

      const priced = await priceCell('DOS-0001')
      expect(priced).toHaveTextContent(/€\s485,00/)
      expect(within(priced).getByText('Geprijsd')).toBeInTheDocument()

      const partial = await priceCell('DOS-0002')
      expect(partial).toHaveTextContent(/€\s100,00/)
      expect(within(partial).getByText('Gedeeltelijk 1/2')).toHaveAttribute(
        'title',
        'Slechts 1 van 2 activiteiten hebben een prijs; het bedrag is geen dossiertotaal.',
      )

      // Geen prijs is nooit € 0,00.
      const unpriced = await priceCell('DOS-0003')
      expect(unpriced).toHaveTextContent(/^Niet geprijsd$/)
      expect(unpriced).not.toHaveTextContent('€')

      const free = await priceCell('DOS-0004')
      expect(free).toHaveTextContent(/€\s0,00/)
      expect(within(free).getByText('Gratis')).toBeInTheDocument()

      // Niets factureerbaar: geen bedrag en geen status.
      expect(await priceCell('DOS-0005')).toHaveTextContent(/^—$/)
    })

    it('markeert een echte € 0,00 die nog niet als gratis bevestigd is met ⚠', async () => {
      api.searchDossiers.mockResolvedValue(
        paged([dossierListItem({ agreedPriceTotal: 0, billableActivityCount: 1, pricedActivityCount: 1, zeroPricedActivityCount: 1, priceStatus: 'Priced' })]),
      )
      renderPage()
      const cell = await priceCell('DOS-0001')

      expect(cell).toHaveTextContent(/€\s0,00/)
      expect(within(cell).getByRole('img', { name: 'Verkoopprijs is € 0,00. Controleer of dit bewust is.' })).toHaveTextContent('⚠')
      expect(within(cell).getByText('Geprijsd')).toBeInTheDocument()
      expect(within(cell).queryByText('Gratis')).toBeNull()
    })

    it('zonder serverstatus (oudere payload) volgt de status uit de tellingen — nooit "Gratis"', async () => {
      api.searchDossiers.mockResolvedValue(
        paged([
          dossierListItem({ id: 'd-1', dossierNumber: 'DOS-0001', agreedPriceTotal: 0, billableActivityCount: 1, pricedActivityCount: 1 }),
          dossierListItem({ id: 'd-2', dossierNumber: 'DOS-0002', agreedPriceTotal: null, billableActivityCount: 2, pricedActivityCount: 0 }),
        ]),
      )
      renderPage()

      expect(within(await priceCell('DOS-0001')).getByText('Geprijsd')).toBeInTheDocument()
      expect(await priceCell('DOS-0002')).toHaveTextContent(/^Niet geprijsd$/)
    })
  })

  // ---- rijacties --------------------------------------------------------------------------------

  describe('rijacties', () => {
    it('Open dossier: Openen, Bevestigen, Annuleren', async () => {
      const user = userEvent.setup()
      renderPage()
      await openRowMenu(user)

      expect(menuItems()).toEqual(['Openen', 'Bevestigen', 'Annuleren'])
    })

    it.each(['Closed', 'Cancelled'] as const)('%s dossier: Openen, Heropenen', async (status) => {
      const user = userEvent.setup()
      api.searchDossiers.mockResolvedValue(paged([dossierListItem({ status })]))
      renderPage()
      await openRowMenu(user)

      expect(menuItems()).toEqual(['Openen', 'Heropenen'])
    })

    it('Escape sluit het rijmenu en zet de focus terug op de knop, zonder het dossier te openen', async () => {
      const user = userEvent.setup()
      const router = renderPage()
      await openRowMenu(user)
      expect(screen.getByRole('menu')).toBeInTheDocument()

      await user.keyboard('{Escape}')

      expect(screen.queryByRole('menu')).toBeNull()
      const trigger = screen.getByRole('button', { name: 'Acties DOS-0001' })
      expect(trigger).toHaveFocus()
      expect(trigger).toHaveAttribute('aria-expanded', 'false')
      expect(router.state.location.pathname).toBe('/dossiers')
    })

    it('"Openen" navigeert naar het dossier', async () => {
      const user = userEvent.setup()
      const router = renderPage()
      await openRowMenu(user)

      await user.click(screen.getByRole('menuitem', { name: 'Openen' }))

      expect(router.state.location.pathname).toBe('/dossiers/d-1')
    })

    it('"Bevestigen" gebruikt de bestaande bevestigingsdialoog: gereedheid ophalen, bevestigen met de rijversie, lijst herladen', async () => {
      const user = userEvent.setup()
      api.searchDossiers.mockResolvedValue(paged([dossierListItem({ version: 'v-7' })]))
      api.getDossierConfirmation.mockResolvedValue(evaluation())
      api.confirmDossier.mockResolvedValue(dossierDetail({ status: 'Closed' }))
      renderPage()
      await openRowMenu(user)
      const callsBefore = api.searchDossiers.mock.calls.length

      await user.click(screen.getByRole('menuitem', { name: 'Bevestigen' }))

      const dialog = await screen.findByRole('dialog', { name: 'Dossier bevestigen — DOS-0001' })
      expect(api.getDossierConfirmation).toHaveBeenCalledWith('d-1')
      await within(dialog).findByText('Alle operationele onderdelen zijn afgerond.')
      await user.click(within(dialog).getByRole('button', { name: 'Bevestigen' }))

      await waitFor(() => {
        expect(api.confirmDossier).toHaveBeenCalledWith('d-1', { reason: null, acknowledgeWarnings: false, version: 'v-7' })
      })
      expect(toast.showSuccess).toHaveBeenCalledWith('Dossier bevestigd.')
      await waitFor(() => {
        expect(api.searchDossiers.mock.calls.length).toBeGreaterThan(callsBefore)
      })
      expect(screen.queryByRole('dialog')).toBeNull()
    })

    it('"Bevestigen" met aandachtspunten vraagt dezelfde bewuste bevestiging als op de detailpagina', async () => {
      const user = userEvent.setup()
      api.getDossierConfirmation.mockResolvedValue(
        evaluation({
          warnings: [{ code: 'pod.missing', severity: 'Warning', message: 'Eén stop heeft geen afleverbewijs.', transportOrderId: null, activityId: null }],
        }),
      )
      api.confirmDossier.mockResolvedValue(dossierDetail({ status: 'Closed' }))
      renderPage()
      await openRowMenu(user)
      await user.click(screen.getByRole('menuitem', { name: 'Bevestigen' }))

      const dialog = await screen.findByRole('dialog', { name: 'Dossier bevestigen — DOS-0001' })
      await within(dialog).findByText('Eén stop heeft geen afleverbewijs.')
      const confirm = within(dialog).getByRole('button', { name: 'Bevestigen' })
      expect(confirm).toBeDisabled()

      await user.click(within(dialog).getByRole('checkbox', { name: /Ik heb de aandachtspunten bekeken/ }))
      await user.click(confirm)

      await waitFor(() => {
        expect(api.confirmDossier).toHaveBeenCalledWith('d-1', { reason: null, acknowledgeWarnings: true, version: undefined })
      })
    })

    it('"Bevestigen" met blokkades kan niet bevestigd worden', async () => {
      const user = userEvent.setup()
      api.getDossierConfirmation.mockResolvedValue(
        evaluation({
          canConfirmManually: false,
          blockers: [{ code: 'orders.open', severity: 'Blocking', message: 'Opdracht ORD-1 is nog niet voltooid.', transportOrderId: null, activityId: null }],
        }),
      )
      renderPage()
      await openRowMenu(user)
      await user.click(screen.getByRole('menuitem', { name: 'Bevestigen' }))

      const dialog = await screen.findByRole('dialog', { name: 'Dossier bevestigen — DOS-0001' })
      await within(dialog).findByText('Opdracht ORD-1 is nog niet voltooid.')
      expect(within(dialog).getByText('Bevestigen is niet mogelijk')).toBeInTheDocument()
      expect(within(dialog).getByRole('button', { name: 'Bevestigen' })).toBeDisabled()
      expect(api.confirmDossier).not.toHaveBeenCalled()
    })

    it('"Annuleren" gebruikt de bestaande redendialoog: reden verplicht, annuleren met de rijversie, lijst herladen', async () => {
      const user = userEvent.setup()
      api.searchDossiers.mockResolvedValue(paged([dossierListItem({ version: 'v-7' })]))
      api.cancelDossier.mockResolvedValue(dossierDetail({ status: 'Cancelled' }))
      renderPage()
      await openRowMenu(user)
      const callsBefore = api.searchDossiers.mock.calls.length

      await user.click(screen.getByRole('menuitem', { name: 'Annuleren' }))

      const dialog = await screen.findByRole('dialog', { name: 'Dossier annuleren — DOS-0001' })
      expect(within(dialog).getByText(/Alleen voor werk dat nooit is uitgevoerd/)).toBeInTheDocument()
      await user.click(within(dialog).getByRole('button', { name: 'Dossier annuleren' }))
      expect(within(dialog).getByText('Geef een reden op.')).toBeInTheDocument()
      expect(api.cancelDossier).not.toHaveBeenCalled()

      await user.type(within(dialog).getByLabelText(/Reden voor annulering/), 'Klant zegt af')
      await user.click(within(dialog).getByRole('button', { name: 'Dossier annuleren' }))

      await waitFor(() => {
        expect(api.cancelDossier).toHaveBeenCalledWith('d-1', 'Klant zegt af', 'v-7')
      })
      expect(toast.showSuccess).toHaveBeenCalledWith('Dossier geannuleerd.')
      await waitFor(() => {
        expect(api.searchDossiers.mock.calls.length).toBeGreaterThan(callsBefore)
      })
      expect(screen.queryByRole('dialog')).toBeNull()
    })

    it('"Annuleren" toont de uitleg van de server in de dialoog en behoudt de reden', async () => {
      const user = userEvent.setup()
      api.cancelDossier.mockRejectedValue(new ApiError('Opdracht ORD-1 is al gepland; annuleer eerst de rit.', 400))
      renderPage()
      await openRowMenu(user)
      await user.click(screen.getByRole('menuitem', { name: 'Annuleren' }))
      const dialog = await screen.findByRole('dialog', { name: 'Dossier annuleren — DOS-0001' })
      await user.type(within(dialog).getByLabelText(/Reden voor annulering/), 'Klant zegt af')

      await user.click(within(dialog).getByRole('button', { name: 'Dossier annuleren' }))

      expect(await within(dialog).findByRole('alert')).toHaveTextContent('Opdracht ORD-1 is al gepland; annuleer eerst de rit.')
      expect(within(dialog).getByLabelText(/Reden voor annulering/)).toHaveValue('Klant zegt af')
      expect(toast.showSuccess).not.toHaveBeenCalled()
    })

    it('een versieconflict (dossier intussen gewijzigd) sluit de dialoog, meldt het en herlaadt de lijst', async () => {
      const user = userEvent.setup()
      api.searchDossiers.mockResolvedValue(paged([dossierListItem({ version: 'v-7' })]))
      api.cancelDossier.mockRejectedValue(new ApiError('Conflict', 409, dossierDetail({ status: 'Closed' })))
      renderPage()
      await openRowMenu(user)
      await user.click(screen.getByRole('menuitem', { name: 'Annuleren' }))
      const dialog = await screen.findByRole('dialog', { name: 'Dossier annuleren — DOS-0001' })
      await user.type(within(dialog).getByLabelText(/Reden voor annulering/), 'Klant zegt af')
      const callsBefore = api.searchDossiers.mock.calls.length

      await user.click(within(dialog).getByRole('button', { name: 'Dossier annuleren' }))

      await waitFor(() => {
        expect(toast.showError).toHaveBeenCalledWith(
          'Het dossier is intussen gewijzigd. De lijst is vernieuwd; controleer het dossier en probeer opnieuw.',
        )
      })
      expect(screen.queryByRole('dialog')).toBeNull()
      expect(toast.showSuccess).not.toHaveBeenCalled()
      await waitFor(() => {
        expect(api.searchDossiers.mock.calls.length).toBeGreaterThan(callsBefore)
      })
    })

    it.each(['Closed', 'Cancelled'] as const)('"Heropenen" (%s) gebruikt de bestaande redendialoog en heropent met de rijversie', async (status) => {
      const user = userEvent.setup()
      api.searchDossiers.mockResolvedValue(paged([dossierListItem({ status, version: 'v-9' })]))
      api.reopenDossier.mockResolvedValue(dossierDetail({ status: 'Open' }))
      renderPage()
      await openRowMenu(user)
      const callsBefore = api.searchDossiers.mock.calls.length

      await user.click(screen.getByRole('menuitem', { name: 'Heropenen' }))

      const dialog = await screen.findByRole('dialog', { name: 'Dossier heropenen — DOS-0001' })
      expect(within(dialog).getByText(/De eerdere bevestiging blijft in de historiek staan/)).toBeInTheDocument()
      await user.click(within(dialog).getByRole('button', { name: 'Dossier heropenen' }))
      expect(within(dialog).getByText('Geef een reden op.')).toBeInTheDocument()
      expect(api.reopenDossier).not.toHaveBeenCalled()

      await user.type(within(dialog).getByLabelText(/Reden voor heropenen/), 'Correctie nodig')
      await user.click(within(dialog).getByRole('button', { name: 'Dossier heropenen' }))

      await waitFor(() => {
        expect(api.reopenDossier).toHaveBeenCalledWith('d-1', 'Correctie nodig', 'v-9')
      })
      expect(toast.showSuccess).toHaveBeenCalledWith('Dossier heropend.')
      await waitFor(() => {
        expect(api.searchDossiers.mock.calls.length).toBeGreaterThan(callsBefore)
      })
    })

    describe('bevoegdheden', () => {
      const rows = [
        dossierListItem({ id: 'd-1', dossierNumber: 'DOS-0001', status: 'Open' }),
        dossierListItem({ id: 'd-2', dossierNumber: 'DOS-0002', status: 'Closed' }),
        dossierListItem({ id: 'd-3', dossierNumber: 'DOS-0003', status: 'Cancelled' }),
      ]

      async function menuOf(user: UserEvent, number: string): Promise<(string | null)[]> {
        await openRowMenu(user, number)
        const items = menuItems()
        await user.keyboard('{Escape}')
        return items
      }

      it('alleen dossiers.view: enkel "Openen", geen "Nieuw dossier"', async () => {
        const user = userEvent.setup()
        auth.permissions = new Set(['dossiers.view'])
        api.searchDossiers.mockResolvedValue(paged(rows))
        renderPage()

        expect(await menuOf(user, 'DOS-0001')).toEqual(['Openen'])
        expect(await menuOf(user, 'DOS-0002')).toEqual(['Openen'])
        expect(await menuOf(user, 'DOS-0003')).toEqual(['Openen'])
        expect(screen.queryByRole('button', { name: 'Nieuw dossier' })).toBeNull()
      })

      it('dossiers.manage zonder dossiers.reopen: bevestigen/annuleren wel, heropenen niet', async () => {
        const user = userEvent.setup()
        auth.permissions = new Set(['dossiers.view', 'dossiers.manage'])
        api.searchDossiers.mockResolvedValue(paged(rows))
        renderPage()

        expect(await menuOf(user, 'DOS-0001')).toEqual(['Openen', 'Bevestigen', 'Annuleren'])
        expect(await menuOf(user, 'DOS-0002')).toEqual(['Openen'])
        expect(await menuOf(user, 'DOS-0003')).toEqual(['Openen'])
      })

      it('dossiers.reopen zonder dossiers.manage: alleen heropenen', async () => {
        const user = userEvent.setup()
        auth.permissions = new Set(['dossiers.view', 'dossiers.reopen'])
        api.searchDossiers.mockResolvedValue(paged(rows))
        renderPage()

        expect(await menuOf(user, 'DOS-0001')).toEqual(['Openen'])
        expect(await menuOf(user, 'DOS-0002')).toEqual(['Openen', 'Heropenen'])
        expect(await menuOf(user, 'DOS-0003')).toEqual(['Openen', 'Heropenen'])
      })
    })
  })

  // ---- B10/B11: weergaven, kolommen, dichtheid -------------------------------------------------

  describe('weergaven en kolommen (Wave 2)', () => {
    it('de presetchips zetten de bestaande serverfilters en tonen welke actief is', async () => {
      const user = userEvent.setup()
      api.searchDossiers.mockResolvedValue(paged([dossierListItem()]))
      const router = renderPage()
      await screen.findByRole('table')

      const views = screen.getByRole('group', { name: 'Weergave' })
      expect(within(views).getByRole('button', { name: 'Alle' })).toHaveAttribute('aria-pressed', 'true')

      await user.click(within(views).getByRole('button', { name: 'Te factureren' }))
      await waitFor(() => expect(router.state.location.search).toBe('?status=Closed&invoiceStatus=NotInvoiced'))
      expect(within(views).getByRole('button', { name: 'Te factureren' })).toHaveAttribute('aria-pressed', 'true')
      expect(chipTexts()).toEqual(['Bevestigd', 'Facturatiestatus: Niet gefactureerd'])
      await expectLastSearch({ ...BASE, status: 'Closed', invoiceStatus: 'NotInvoiced' })

      await user.click(within(views).getByRole('button', { name: 'Mijn open' }))
      await waitFor(() => expect(router.state.location.search).toBe('?status=Open&responsibleUserId=u-1'))
      expect(chipTexts()).toEqual(['Open', 'Alleen mijn dossiers'])

      await user.click(within(views).getByRole('button', { name: 'Met open incident' }))
      await expectLastSearch({ ...BASE, status: 'Open', hasOpenIncident: true })
    })

    it('een verfijnde preset is geen preset meer en kan als eigen weergave bewaard worden', async () => {
      const user = userEvent.setup()
      api.searchDossiers.mockResolvedValue(paged([dossierListItem()]))
      renderPage('/dossiers?status=Open&priceStatus=Unpriced&postalCode=2000')
      await screen.findByRole('table')

      const views = screen.getByRole('group', { name: 'Weergave' })
      expect(within(views).getByRole('button', { name: 'Niet geprijsd' })).toHaveAttribute('aria-pressed', 'false')
      await user.click(screen.getByRole('button', { name: '+ Opslaan als weergave' }))
      await user.type(screen.getByLabelText(/Naam/), 'Antwerpen te prijzen')
      await user.click(screen.getByRole('button', { name: 'Opslaan' }))

      expect(within(views).getByRole('button', { name: 'Antwerpen te prijzen' })).toHaveAttribute('aria-pressed', 'true')
      expect(JSON.parse(window.localStorage.getItem('ts.dossiers.views:u-1') ?? '[]')).toHaveLength(1)
      await user.click(within(views).getByRole('button', { name: 'Weergave Antwerpen te prijzen verwijderen' }))
      expect(within(views).queryByRole('button', { name: 'Antwerpen te prijzen' })).not.toBeInTheDocument()
    })

    it('"Kolommen" verbergt en toont optionele kolommen en onthoudt de keuze per gebruiker', async () => {
      const user = userEvent.setup()
      api.searchDossiers.mockResolvedValue(paged([dossierListItem({ driverSummary: 'Jan Peeters', vehicleSummary: '1-ABC-123', planningDate: '2026-08-14' })]))
      renderPage()
      await screen.findByRole('table')

      await user.click(screen.getByRole('button', { name: 'Kolommen' }))
      const panel = screen.getByRole('group', { name: 'Zichtbare kolommen' })
      await user.click(within(panel).getByRole('checkbox', { name: 'Referentie' }))
      await user.click(within(panel).getByRole('checkbox', { name: 'Chauffeur / voertuig' }))

      expect(headerTexts()).toEqual(['Dossiernr.', 'Status', 'Klant', 'Datum', 'Transport', 'Planning', 'Chauffeur / voertuig', 'Prijs', 'Facturatie', ''])
      // With its own Chauffeur column, Planning is the date alone.
      const row = await findRow()
      expect(within(row).getByText('Jan Peeters · 1-ABC-123')).toBeInTheDocument()
      expect(within(row).getByText('14/08/2026').closest('td')).toHaveClass('dossier-col-planning')
      expect(JSON.parse(window.localStorage.getItem('ts.dossiers.list:u-1') ?? '{}')).toEqual({
        optional: ['transport', 'planning', 'driver', 'price', 'invoice'],
        density: 'normal',
      })

      await user.click(within(panel).getByRole('button', { name: 'Standaard herstellen' }))
      expect(headerTexts()).toContain('Referentie')
    })

    it('de dichtheid Compact/Normaal wordt onthouden en op de tabel gezet', async () => {
      const user = userEvent.setup()
      api.searchDossiers.mockResolvedValue(paged([dossierListItem()]))
      renderPage()
      await screen.findByRole('table')

      await user.click(screen.getByRole('button', { name: 'Compact' }))
      expect(screen.getByRole('table')).toHaveClass('data-table-compact')
      expect(JSON.parse(window.localStorage.getItem('ts.dossiers.list:u-1') ?? '{}').density).toBe('compact')
    })
  })

})
