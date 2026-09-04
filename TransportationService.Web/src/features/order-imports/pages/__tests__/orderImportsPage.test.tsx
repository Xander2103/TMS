import { describe, expect, it, beforeEach, vi } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { OrderImportsPage } from '../OrderImportsPage'
import type { OrderImportAnalysis, OrderImportBatch, OrderImportProfile } from '../../api/orderImportsApi'

const auth = vi.hoisted(() => ({ permissions: new Set<string>() }))
vi.mock('../../../auth/authContextValue', () => ({
  useAuth: () => ({ hasPermission: (code: string) => auth.permissions.has(code) }),
}))

vi.mock('../../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }),
}))

const api = vi.hoisted(() => ({
  batches: [] as OrderImportBatch[],
  profiles: [] as OrderImportProfile[],
  analysis: { columns: [], profileMatches: [] } as OrderImportAnalysis,
  upload: vi.fn(),
  analyze: vi.fn(),
  createProfile: vi.fn(),
  updateProfile: vi.fn(),
  deleteProfile: vi.fn(),
}))

vi.mock('../../api/orderImportsApi', () => ({
  listOrderImportProfiles: vi.fn(() => Promise.resolve(api.profiles)),
  listOrderImportFields: vi.fn(() =>
    Promise.resolve([
      { key: 'customerReference', group: 'dossier' },
      { key: 'quantity', group: 'goods' },
      { key: 'unloadingPostalCode', group: 'unloading' },
      { key: 'unloadingCity', group: 'unloading' },
    ]),
  ),
  listOrderImportBatches: vi.fn(() =>
    Promise.resolve({ items: api.batches, totalCount: api.batches.length, page: 1, pageSize: 25 }),
  ),
  getOrderImportBatch: vi.fn(),
  uploadOrderImport: api.upload,
  analyzeOrderImportFile: api.analyze,
  createOrderImportProfile: api.createProfile,
  updateOrderImportProfile: api.updateProfile,
  deleteOrderImportProfile: api.deleteProfile,
}))

vi.mock('../../../customers/api/customersApi', () => ({
  searchCustomers: vi.fn(() =>
    Promise.resolve({ items: [{ id: 'c1', name: 'Atlas Copco', customerNumber: 'KL-1' }], totalCount: 1 }),
  ),
}))

function profile(overrides: Partial<OrderImportProfile> = {}): OrderImportProfile {
  return {
    id: 'p1',
    name: 'Generiek v1',
    description: null,
    mappingJson: '{}',
    isActive: true,
    customerId: null,
    customerName: null,
    mapping: { unloadingCity: 'M' },
    sourceHeaders: null,
    mappedFieldCount: 1,
    updatedAt: '2026-09-01T10:00:00Z',
    ...overrides,
  }
}

function batch(overrides: Partial<OrderImportBatch>): OrderImportBatch {
  return {
    id: 'b1',
    profileId: 'p1',
    profileName: 'Generiek v1',
    customerId: 'c1',
    customerName: 'Haven BV',
    fileName: 'orders.xlsx',
    status: 'Processed',
    rowCount: 3,
    successCount: 2,
    failureCount: 1,
    dryRun: false,
    createdAt: '2026-08-12T10:00:00Z',
    ...overrides,
  }
}

function renderPage(entry = '/order-imports') {
  return render(
    <MemoryRouter initialEntries={[entry]}>
      <OrderImportsPage />
    </MemoryRouter>,
  )
}

async function pickCustomer(user: ReturnType<typeof userEvent.setup>) {
  await user.click(screen.getByRole('combobox', { name: 'Klant' }))
  await user.click(await screen.findByText('Atlas Copco'))
}

describe('OrderImportsPage', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    auth.permissions = new Set()
    api.batches = []
    api.profiles = [profile()]
    api.analysis = { columns: [], profileMatches: [] }
    api.analyze.mockImplementation(() => Promise.resolve(api.analysis))
    api.upload.mockResolvedValue({
      batch: batch({ dryRun: true, status: 'Validated', failureCount: 0 }),
      rows: [],
    })
  })

  it('shows a permission placeholder without order permissions', () => {
    renderPage()
    expect(screen.getByText('Je hebt geen rechten om opdrachtimporten te bekijken.')).toBeInTheDocument()
  })

  it('renders local tabs with Importeren active by default; history lives under Importhistoriek', async () => {
    auth.permissions = new Set(['orders.view', 'orders.create'])
    api.batches = [
      batch({ id: 'b1', fileName: 'orders.xlsx', status: 'Processed' }),
      batch({ id: 'b2', fileName: 'proef.xlsx', status: 'Validated', dryRun: true, failureCount: 0 }),
    ]
    const user = userEvent.setup()
    renderPage()

    // Tab bar + default tab: the upload form, no history table.
    expect(await screen.findByRole('tab', { name: 'Importeren' })).toHaveAttribute('aria-selected', 'true')
    expect(screen.getByLabelText('Enkel valideren (proefdraaien)')).toBeInTheDocument()
    expect(screen.queryByText('orders.xlsx')).not.toBeInTheDocument()

    await user.click(screen.getByRole('tab', { name: 'Importhistoriek' }))
    expect(await screen.findByText('orders.xlsx')).toBeInTheDocument()
    expect(screen.getByText('Verwerkt')).toBeInTheDocument()
    expect(screen.getByText('Gevalideerd (proef)')).toBeInTheDocument()
    expect(screen.getByText('2 ok / 1 fout / 3 totaal')).toBeInTheDocument()
  })

  it('deep-links straight into the history tab via ?tab=', async () => {
    auth.permissions = new Set(['orders.view'])
    api.batches = [batch({ id: 'b1', fileName: 'orders.xlsx' })]
    renderPage('/order-imports?tab=historiek')

    expect(await screen.findByText('orders.xlsx')).toBeInTheDocument()
    expect(screen.getByRole('tab', { name: 'Importhistoriek' })).toHaveAttribute('aria-selected', 'true')
  })

  it('keeps the existing import flow working: profile + customer + file + dry-run submit', async () => {
    auth.permissions = new Set(['orders.view', 'orders.create'])
    const user = userEvent.setup()
    renderPage()

    await screen.findByRole('tab', { name: 'Importeren' })
    await pickCustomer(user)
    // Generiek v1 is auto-selected as the first profile.
    expect(screen.getByRole('combobox', { name: 'Importprofiel' })).toHaveValue('Generiek v1')
    const fileInput = screen.getByLabelText(/Bestand/)
    await user.upload(fileInput, new File(['x'], 'orders.xlsx', { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' }))
    await user.click(screen.getByRole('button', { name: 'Valideren' }))

    await waitFor(() => expect(api.upload).toHaveBeenCalledTimes(1))
    expect(api.upload.mock.calls[0][0]).toEqual(
      expect.objectContaining({ profileId: 'p1', customerId: 'c1', dryRun: true }),
    )
  })

  it('recognizes a saved profile from the file headers and preselects it at high confidence', async () => {
    auth.permissions = new Set(['orders.view', 'orders.create'])
    api.profiles = [profile(), profile({ id: 'p2', name: 'Atlas Copco Orders', customerId: 'c1', customerName: 'Atlas Copco' })]
    api.analysis = {
      columns: [],
      profileMatches: [{ profileId: 'p2', name: 'Atlas Copco Orders', customerId: 'c1', matchPercent: 96 }],
    }
    const user = userEvent.setup()
    renderPage()

    await screen.findByRole('tab', { name: 'Importeren' })
    await pickCustomer(user)
    await user.upload(screen.getByLabelText(/Bestand/), new File(['x'], 'atlas.xlsx'))

    expect(await screen.findByText(/Importprofiel herkend: Atlas Copco Orders \(96%/)).toBeInTheDocument()
    // ≥90%: automatically selected.
    expect(screen.getByRole('combobox', { name: 'Importprofiel' })).toHaveValue('Atlas Copco Orders')
  })

  it('a medium-confidence match is only OFFERED, never silently applied', async () => {
    auth.permissions = new Set(['orders.view', 'orders.create'])
    api.profiles = [profile(), profile({ id: 'p2', name: 'Atlas Copco Orders', customerId: null })]
    api.analysis = {
      columns: [],
      profileMatches: [{ profileId: 'p2', name: 'Atlas Copco Orders', customerId: null, matchPercent: 72 }],
    }
    const user = userEvent.setup()
    renderPage()

    await screen.findByRole('tab', { name: 'Importeren' })
    await pickCustomer(user)
    await user.upload(screen.getByLabelText(/Bestand/), new File(['x'], 'atlas.xlsx'))

    expect(await screen.findByText(/Importprofiel herkend: Atlas Copco Orders \(72%/)).toBeInTheDocument()
    expect(screen.getByRole('combobox', { name: 'Importprofiel' })).toHaveValue('Generiek v1')
    await user.click(screen.getByRole('button', { name: 'Profiel gebruiken' }))
    expect(screen.getByRole('combobox', { name: 'Importprofiel' })).toHaveValue('Atlas Copco Orders')
  })

  it('hides profiles bound to ANOTHER customer from the profile picker', async () => {
    auth.permissions = new Set(['orders.view', 'orders.create'])
    api.profiles = [profile(), profile({ id: 'p2', name: 'Andermans profiel', customerId: 'c-other', customerName: 'Bevo' })]
    const user = userEvent.setup()
    renderPage()

    await screen.findByRole('tab', { name: 'Importeren' })
    await pickCustomer(user)
    await user.click(screen.getByRole('combobox', { name: 'Importprofiel' }))
    expect(screen.queryByRole('option', { name: /Andermans profiel/ })).not.toBeInTheDocument()
  })
})

describe('OrderImportsPage — Importprofielen tab', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    auth.permissions = new Set(['orders.view', 'orders.create', 'order_imports.manage_profiles'])
    api.batches = []
    api.profiles = [
      profile({
        id: 'p2', name: 'Atlas Copco Orders', customerId: 'c1', customerName: 'Atlas Copco',
        mappedFieldCount: 5, sourceHeaders: ['Reference', 'Destination ZIP'],
        mapping: { customerReference: 'A', unloadingPostalCode: 'B' },
      }),
    ]
    api.analysis = { columns: [], profileMatches: [] }
    api.analyze.mockImplementation(() => Promise.resolve(api.analysis))
    api.createProfile.mockResolvedValue(profile({ id: 'p9' }))
    api.updateProfile.mockResolvedValue(profile({ id: 'p2' }))
  })

  it('shows the overview with customer, mapping count and status', async () => {
    const user = userEvent.setup()
    renderPage()
    await user.click(await screen.findByRole('tab', { name: 'Importprofielen' }))

    expect(await screen.findByText('Atlas Copco Orders')).toBeInTheDocument()
    expect(screen.getByText('Atlas Copco')).toBeInTheDocument()
    expect(screen.getByText('5 velden')).toBeInTheDocument()
    expect(screen.getByText('Transportopdrachten')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: '+ Importprofiel maken' })).toBeInTheDocument()
  })

  it('creates a profile from a sample file: analysis → statuses → searchable field picker → save', async () => {
    api.analysis = {
      columns: [
        { columnIndex: 1, header: 'Reference', sampleValues: ['AC-1001', 'AC-1002'], suggestedField: 'customerReference', confidence: 95 },
        { columnIndex: 2, header: 'PAL', sampleValues: ['4'], suggestedField: 'quantity', confidence: 70 },
        { columnIndex: 3, header: 'Delivery city', sampleValues: ['Gent'], suggestedField: 'unloadingCity', confidence: 95 },
        { columnIndex: 4, header: 'Internal', sampleValues: ['x'], suggestedField: null, confidence: null },
      ],
      profileMatches: [],
    }
    const user = userEvent.setup()
    renderPage()
    await user.click(await screen.findByRole('tab', { name: 'Importprofielen' }))
    await user.click(await screen.findByRole('button', { name: '+ Importprofiel maken' }))

    await user.type(screen.getByLabelText(/^Naam/), 'Atlas nieuw')
    await user.upload(screen.getByLabelText(/Voorbeeldbestand/), new File(['x'], 'sample.xlsx'))

    // Confidence-driven, text-based statuses (never colour-only).
    expect(await screen.findAllByText('Herkend')).not.toHaveLength(0)
    expect(screen.getByText('Controleren')).toBeInTheDocument()
    expect(screen.getByText('Niet gekoppeld')).toBeInTheDocument()
    expect(screen.getByText('AC-1001 · AC-1002')).toBeInTheDocument()

    // The unknown column is deliberately ignored via the searchable picker.
    await user.click(screen.getByRole('combobox', { name: 'TMS-veld voor kolom Internal' }))
    await user.click(await screen.findByRole('option', { name: /Niet importeren/ }))
    expect(screen.getByText('Genegeerd')).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Importprofiel opslaan' }))
    await waitFor(() => expect(api.createProfile).toHaveBeenCalledTimes(1))
    const input = api.createProfile.mock.calls[0][0]
    expect(input).toEqual(
      expect.objectContaining({
        name: 'Atlas nieuw',
        headerRows: 1,
        mapping: { customerReference: '1', quantity: '2', unloadingCity: '3' },
        sourceHeaders: ['Reference', 'PAL', 'Delivery city', 'Internal'],
      }),
    )
  })

  it('opens an existing profile from its stored headers without a new upload', async () => {
    const user = userEvent.setup()
    renderPage()
    await user.click(await screen.findByRole('tab', { name: 'Importprofielen' }))
    const row = (await screen.findByText('Atlas Copco Orders')).closest('tr')!
    await user.click(within(row).getByRole('button', { name: 'Bewerken' }))

    expect(await screen.findByLabelText(/^Naam/)).toHaveValue('Atlas Copco Orders')
    // Stored headers render as mapping rows with the saved targets.
    expect(screen.getByText('Reference')).toBeInTheDocument()
    expect(screen.getByRole('combobox', { name: 'TMS-veld voor kolom Reference' })).toHaveValue('Dossier · Klantreferentie')

    await user.click(screen.getByRole('button', { name: 'Importprofiel opslaan' }))
    await waitFor(() => expect(api.updateProfile).toHaveBeenCalledWith('p2', expect.objectContaining({
      mapping: { customerReference: '1', unloadingPostalCode: '2' },
    })))
  })

  it('blocks saving when two columns target the same TMS field', async () => {
    api.analysis = {
      columns: [
        { columnIndex: 1, header: 'Ref A', sampleValues: [], suggestedField: 'customerReference', confidence: 95 },
        { columnIndex: 2, header: 'Ref B', sampleValues: [], suggestedField: 'customerReference', confidence: 95 },
        { columnIndex: 3, header: 'City', sampleValues: [], suggestedField: 'unloadingCity', confidence: 95 },
      ],
      profileMatches: [],
    }
    const user = userEvent.setup()
    renderPage()
    await user.click(await screen.findByRole('tab', { name: 'Importprofielen' }))
    await user.click(await screen.findByRole('button', { name: '+ Importprofiel maken' }))
    await user.type(screen.getByLabelText(/^Naam/), 'Dubbel')
    await user.upload(screen.getByLabelText(/Voorbeeldbestand/), new File(['x'], 'sample.xlsx'))

    expect(await screen.findAllByText('Dubbel')).not.toHaveLength(0)
    await user.click(screen.getByRole('button', { name: 'Importprofiel opslaan' }))

    expect(await screen.findByRole('alert')).toHaveTextContent(/hetzelfde TMS-veld/)
    expect(api.createProfile).not.toHaveBeenCalled()
  })

  it('is read-only without the manage permission', async () => {
    auth.permissions = new Set(['orders.view', 'orders.create'])
    const user = userEvent.setup()
    renderPage()
    await user.click(await screen.findByRole('tab', { name: 'Importprofielen' }))

    expect(await screen.findByText('Atlas Copco Orders')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: '+ Importprofiel maken' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Bewerken' })).not.toBeInTheDocument()
  })
})
