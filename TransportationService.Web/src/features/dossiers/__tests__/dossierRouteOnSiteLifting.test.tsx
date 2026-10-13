import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { DossierRouteEditor } from '../components/DossierRouteEditor'
import { DossierRouteSummary } from '../components/DossierRouteSummary'
import type { TransportOrderDetail } from '../../transport-orders/types'
import { dossierActivity, dossierDetail, orderDetail } from './fixtures'

const toast = vi.hoisted(() => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }))
vi.mock('../../../components/ui/toastContext', () => ({ useToast: () => toast }))
const orders = vi.hoisted(() => ({ get: vi.fn(), update: vi.fn() }))
vi.mock('../../transport-orders/api/transportOrdersApi', () => ({
  getTransportOrder: orders.get,
  updateTransportOrder: orders.update,
}))
vi.mock('../api/dossiersApi', () => ({ createOrderForActivity: vi.fn() }))
vi.mock('../../tarification/api/pricingApi', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../tarification/api/pricingApi')>()),
  listServiceOptions: () => Promise.resolve([]),
}))
vi.mock('../../locations/api/locationsApi', () => ({ getLocation: () => Promise.reject(new Error('n/a')) }))
vi.mock('../../locations/api/customerAddressesApi', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../locations/api/customerAddressesApi')>()),
  pickAddresses: () => Promise.resolve([]),
  checkAddressDuplicates: () => Promise.resolve({ hasExactMatch: false, candidates: [] }),
}))
vi.mock('../../locations/components/LocationSelect', () => ({
  LocationSelect: ({ id }: { id?: string }) => <input id={id} aria-label="locatie" />,
}))
vi.mock('../../reference/components/CountryCombobox', () => ({
  CountryCombobox: ({ id }: { id?: string }) => <input id={id} aria-label="Land" />,
}))

const baseStop = orderDetail().stops[0]

/** An on-site lifting job as the API returns it: one Site stop, a description, no goods. */
function onSiteOrder(overrides: Partial<TransportOrderDetail> = {}): TransportOrderDetail {
  return orderDetail({
    status: 'Draft',
    goodsDescription: null,
    cargoItems: [],
    craneJobKind: 'OnSiteLifting',
    workDescription: 'Airco-unit op dak plaatsen',
    liftLoadWeightKg: 850,
    activitySupportsOnSiteWork: true,
    activityDurationHours: 4,
    version: 'ov-1',
    stops: [
      {
        ...baseStop, id: 'site-1', stopType: 'Site', locationName: 'Werf Dupont', address: 'Avenue Sabin 1', postalCode: '1300',
        city: 'Waver', plannedFrom: '2026-09-22T06:00:00Z', plannedTo: '2026-09-22T10:00:00Z', plannedToIsManual: false,
      },
    ],
    ...overrides,
  })
}

function renderEditor(order: TransportOrderDetail, onOrderSaved = vi.fn()) {
  const element = (
    <DossierRouteEditor
      dossier={dossierDetail()}
      activity={dossierActivity({ linkedTransportOrderId: order.id, supportsOnSiteWork: order.activitySupportsOnSiteWork })}
      order={order}
      loading={false}
      canEdit
      canCreateLocations
      onOrderSaved={onOrderSaved}
      onDossierUpdated={vi.fn()}
      onConflict={() => false}
    />
  )
  const router = createMemoryRouter([{ path: '/', element }])
  render(<RouterProvider router={router} />)
  return { onOrderSaved }
}

const stopCards = () => Array.from(document.querySelectorAll<HTMLElement>('.tof-stops-grid > fieldset.tof-stop'))

beforeEach(() => {
  vi.clearAllMocks()
})

describe('Dossier route editor — hijswerk op locatie (D2)', () => {
  it('shows ONE werfadres with the stored snapshot, the description and the lift data — never a seeded laad/los stop', async () => {
    renderEditor(onSiteOrder())
    const cards = stopCards()
    expect(cards).toHaveLength(1)
    expect(within(cards[0]).getByText('Werfadres')).toBeInTheDocument()
    expect(within(cards[0]).getByLabelText('Naam locatie')).toHaveValue('Werf Dupont')
    expect(within(cards[0]).getByLabelText('Straat + nr')).toHaveValue('Avenue Sabin 1')
    expect(within(cards[0]).getByLabelText('Van')).toHaveValue('08:00')
    expect(within(cards[0]).getByLabelText('Tot')).toHaveValue('12:00')
    expect(screen.getByRole('radio', { name: 'Hijswerk op locatie' })).toBeChecked()
    expect(screen.getByLabelText(/Omschrijving werkzaamheden/)).toHaveValue('Airco-unit op dak plaatsen')
    expect(screen.getByLabelText('Gewicht last (kg)')).toHaveValue(850)
    expect(screen.getByText('Geplande duur: 4 u — aanpassen via de activiteit.')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: '+ Extra losstop' })).not.toBeInTheDocument()
  })

  it('saves the full crane-job set + the Site stop (id and version echoed); the end follows a moved start', async () => {
    const user = userEvent.setup()
    orders.update.mockResolvedValue(onSiteOrder({ version: 'ov-2' }))
    renderEditor(onSiteOrder())
    const site = stopCards()[0]

    await user.clear(within(site).getByLabelText('Van'))
    await user.type(within(site).getByLabelText('Van'), '22:00')
    await user.tab()
    expect(within(site).getByLabelText('Tot')).toHaveValue('02:00')

    await user.click(screen.getByRole('button', { name: 'Route opslaan' }))
    await waitFor(() => expect(orders.update).toHaveBeenCalledTimes(1))
    const [orderId, payload] = orders.update.mock.calls[0]
    expect(orderId).toBe('o-1')
    expect(payload).toMatchObject({
      version: 'ov-1', craneJobKind: 'OnSiteLifting', workDescription: 'Airco-unit op dak plaatsen', liftLoadWeightKg: 850,
      liftLoadDimensions: null, cargoItems: [],
    })
    expect(payload.stops).toHaveLength(1)
    expect(payload.stops[0]).toMatchObject({
      id: 'site-1', stopType: 'Site', plannedToIsManual: false,
      plannedFrom: '2026-09-22T20:00:00Z', plannedTo: '2026-09-23T00:00:00Z',
    })
    expect(toast.showSuccess).toHaveBeenCalledTimes(1)
  })

  it('a cleared description blocks the save inline — no request, no success toast', async () => {
    const user = userEvent.setup()
    renderEditor(onSiteOrder())
    await user.clear(screen.getByLabelText(/Omschrijving werkzaamheden/))
    await user.click(screen.getByRole('button', { name: 'Route opslaan' }))
    expect(await screen.findByText('Beschrijf de werkzaamheden op locatie.')).toBeInTheDocument()
    expect(orders.update).not.toHaveBeenCalled()
    expect(toast.showSuccess).not.toHaveBeenCalled()
  })

  it('a server refusal is shown honestly — on the stop it names, without a success toast', async () => {
    const user = userEvent.setup()
    const { ApiError } = await import('../../../api/apiClient')
    orders.update.mockRejectedValue(
      new ApiError('Bad Request', 400, {
        title: 'Validatie mislukt',
        errors: { 'Stops[0].SaveToAddressBook': ['Je hebt geen rechten om adressen aan te maken.'] },
      }),
    )
    renderEditor(onSiteOrder())
    const site = stopCards()[0]
    await user.clear(within(site).getByLabelText(/Plaats/))
    await user.type(within(site).getByLabelText(/Plaats/), 'Wavre')
    await user.click(within(site).getByLabelText('Adres ook bewaren in klantenbestand'))
    await user.click(screen.getByRole('button', { name: 'Route opslaan' }))

    expect(await within(site).findByText('Je hebt geen rechten om adressen aan te maken.')).toBeInTheDocument()
    expect(orders.update.mock.calls[0][1].stops[0]).toMatchObject({ saveToAddressBook: true })
    expect(toast.showSuccess).not.toHaveBeenCalled()
  })

  it('a classic order of a type WITHOUT the capability offers no choice and sends no crane-job fields', async () => {
    const user = userEvent.setup()
    const classic = orderDetail({ status: 'Draft', version: 'ov-1' })
    orders.update.mockResolvedValue(orderDetail({ status: 'Draft', version: 'ov-2' }))
    renderEditor(classic)
    expect(screen.queryByRole('radio', { name: 'Hijswerk op locatie' })).not.toBeInTheDocument()

    const unload = stopCards()[1]
    await user.type(within(unload).getByLabelText(/Plaats/), 'Gent')
    await user.click(screen.getByRole('button', { name: 'Route opslaan' }))
    await waitFor(() => expect(orders.update).toHaveBeenCalledTimes(1))
    expect(orders.update.mock.calls[0][1]).not.toHaveProperty('craneJobKind')
    expect(orders.update.mock.calls[0][1]).not.toHaveProperty('workDescription')
  })

  it('switching kind asks before dropping filled stops, then shows the work site instead of laden/lossen', async () => {
    const user = userEvent.setup()
    renderEditor(orderDetail({ status: 'Draft', version: 'ov-1', activitySupportsOnSiteWork: true, craneJobKind: 'TransportWithCrane' }))
    expect(screen.getByRole('radio', { name: 'Transport met kraan' })).toBeChecked()

    await user.click(screen.getByRole('radio', { name: 'Hijswerk op locatie' }))
    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByText('Soort kraanopdracht wijzigen?')).toBeInTheDocument()
    await user.click(within(dialog).getByRole('button', { name: 'Annuleren' }))
    expect(stopCards()).toHaveLength(2)

    await user.click(screen.getByRole('radio', { name: 'Hijswerk op locatie' }))
    await user.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Wijzigen' }))
    expect(stopCards()).toHaveLength(1)
    expect(within(stopCards()[0]).getByText('Werfadres')).toBeInTheDocument()
  })
})

describe('Dossier route summary — a Site stop reads "Werf"', () => {
  it('shows the work site and the work instead of an empty Laden/Lossen pair', () => {
    render(<DossierRouteSummary order={onSiteOrder()} loading={false} canEdit={false} onEdit={() => undefined} />)
    expect(screen.getByRole('heading', { name: 'Werf' })).toBeInTheDocument()
    expect(screen.getByText('Werf Dupont')).toBeInTheDocument()
    expect(screen.getByText('Airco-unit op dak plaatsen')).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'Laden' })).not.toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'Lossen' })).not.toBeInTheDocument()
  })

  it('a classic order keeps its Laden/Lossen columns', () => {
    render(<DossierRouteSummary order={orderDetail()} loading={false} canEdit={false} onEdit={() => undefined} />)
    expect(screen.getByRole('heading', { name: 'Laden' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Lossen' })).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'Werf' })).not.toBeInTheDocument()
  })
})
