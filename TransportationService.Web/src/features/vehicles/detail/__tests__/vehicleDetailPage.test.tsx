import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { RouterProvider, createMemoryRouter, useLocation } from 'react-router-dom'
import { VehicleDetailPage } from '../../pages/VehicleDetailPage'
import type { VehicleDetail } from '../../types'

const auth = vi.hoisted(() => ({ granted: new Set<string>() }))
vi.mock('../../../auth/authContextValue', () => ({
  useAuth: () => ({ hasPermission: (code: string) => auth.granted.has(code) }),
}))
vi.mock('../../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }),
}))

const vehicle: VehicleDetail = {
  id: 'v1', internalNumber: 'VRT-0001', licensePlate: '1-ABC-101', vin: null, categoryId: null, categoryName: 'Trekker', brand: 'Volvo', model: 'FH',
  year: 2022, firstRegistrationDate: null, fuelType: 'Diesel', emissionClass: null, grossVehicleWeightKg: 40000, payloadKg: 24000, lengthMeters: null,
  widthMeters: null, heightMeters: null, volumeM3: null, volumeIsManual: false, odometerKm: 101234, consumptionLPer100Km: null, axleCount: 3,
  loadingMeters: 0, requiredLicenceCode: 'CE', hasCrane: false, hasRefrigeration: false, hasTailLift: false, adrSuitable: true, ownershipType: 'Owned',
  operationalStatus: 'Available', statusReason: null, isActive: true, fixedDriverId: 'drv-1', fixedDriverName: 'Karim El Amrani', currentDriverId: null,
  currentDriverName: null, notes: null,
}
vi.mock('../../api/vehiclesApi', () => ({
  getVehicle: () => Promise.resolve(vehicle),
  updateVehicle: vi.fn(),
  deleteVehicle: vi.fn(),
  setVehicleActive: vi.fn(),
  setVehicleDriver: vi.fn(),
}))
const listDocs = vi.hoisted(() => vi.fn())
vi.mock('../../../fleet-documents/api/fleetDocumentsApi', () => ({ listFleetDocuments: listDocs }))
vi.mock('../../../inspections/api/inspectionsApi', () => ({ listInspections: () => Promise.resolve([]) }))
vi.mock('../../../maintenance/api/maintenanceApi', () => ({ listMaintenance: () => Promise.resolve([]) }))
vi.mock('../../../damage/api/damageApi', () => ({
  listDamageReports: () => Promise.resolve([{ id: 'x', status: 'Reported', severity: 'Moderate' }]),
}))
vi.mock('../../../fleet-compliance/tachographApi', async () => ({
  ...(await vi.importActual<object>('../../../fleet-compliance/tachographApi')),
  listTachographCalibrations: () => Promise.resolve([]),
}))
const stub = vi.hoisted(() => (name: string) => () => `panel:${name}`)
vi.mock('../../../fleet-documents/components/FleetDocumentsPanel', () => ({ FleetDocumentsPanel: stub('documents') }))
vi.mock('../../../inspections/components/InspectionsPanel', () => ({ InspectionsPanel: stub('inspections') }))
vi.mock('../../../fleet-compliance/TachographPanel', () => ({ TachographPanel: stub('tachograph') }))
vi.mock('../../../fleet-compliance/LeasingPanel', () => ({ LeasingPanel: stub('leasing') }))
vi.mock('../../../maintenance/components/MaintenancePanel', () => ({ MaintenancePanel: stub('maintenance') }))
vi.mock('../../../maintenance-policies/components/MaintenancePolicySummary', () => ({ MaintenancePolicySummary: stub('policy') }))
vi.mock('../../../damage/components/DamagePanel', () => ({ DamagePanel: stub('damage') }))
vi.mock('../../../fuel/components/FuelPanel', () => ({ FuelPanel: stub('fuel') }))
vi.mock('../../../fleet-kpi/FleetKpiPanel', () => ({ FleetKpiPanel: stub('kpi') }))
vi.mock('../../../auditing/components/AuditHistoryPanel', () => ({ AuditHistoryPanel: stub('history') }))
vi.mock('../../../fleet-assignment/AssignmentSlot', () => ({ AssignmentSlot: ({ title }: { title: string }) => <div>{`slot:${title}`}</div> }))
vi.mock('../../../drivers/api/driversApi', () => ({ searchDrivers: () => Promise.resolve({ items: [] }) }))

function Search() {
  return <output data-testid="search">{useLocation().search}</output>
}

function renderPage(url = '/vehicles/v1') {
  const router = createMemoryRouter(
    [{ path: '/vehicles/:id', element: <><VehicleDetailPage /><Search /></> }],
    { initialEntries: [url] },
  )
  render(<RouterProvider router={router} />)
}

const ALL = [
  'vehicles.edit', 'vehicles.delete', 'fleet_documents.view', 'inspections.view', 'tachograph.view', 'maintenance.view',
  'damage_reports.view', 'fuel.view', 'kpi.view', 'audit_logs.view',
]

beforeEach(() => {
  auth.granted = new Set(ALL)
  listDocs.mockReset()
  listDocs.mockResolvedValue([])
  Element.prototype.scrollIntoView = vi.fn()
})

describe('VehicleDetailPage (B16)', () => {
  it('shows one navigation layer of six groups and a read-first overview', async () => {
    renderPage()
    expect(await screen.findByRole('heading', { name: 'Volvo FH' })).toBeInTheDocument()
    expect(screen.getAllByRole('tab').map((tab) => tab.textContent)).toEqual([
      'Overzicht', 'Techniek & toewijzing', 'Documenten & keuringen', 'Onderhoud & schade', 'Leasing & kosten', 'Historiek',
    ])
    expect(screen.getByRole('link', { name: 'Karim El Amrani' })).toHaveAttribute('href', '/drivers/drv-1')
    expect(screen.queryByRole('textbox')).not.toBeInTheDocument()
    const attention = await screen.findByRole('region', { name: 'Aandacht' })
    expect(within(attention).getByText('1 openstaande schade')).toBeInTheDocument()
    expect(within(attention).getByText('Geen ADR-certificaat geregistreerd')).toBeInTheDocument()
  })

  it('jumps from an attention line to the block that resolves it', async () => {
    renderPage()
    const attention = await screen.findByRole('region', { name: 'Aandacht' })
    const line = within(attention).getByText('1 openstaande schade').closest('li')!
    await userEvent.click(within(line).getByRole('button', { name: 'Bekijken' }))
    expect(screen.getByTestId('search')).toHaveTextContent('focus=schade')
    expect(screen.getByRole('tab', { name: 'Onderhoud & schade' })).toHaveAttribute('aria-selected', 'true')
    expect(screen.getByText('panel:damage')).toBeInTheDocument()
  })

  it.each([
    ['brandstof', 'Leasing & kosten', 'focus=brandstof'],
    ['keuringen', 'Documenten & keuringen', 'focus=keuringen'],
    ['toewijzing', 'Techniek & toewijzing', 'focus=toewijzing'],
    ['kpi', 'Leasing & kosten', 'focus=kpi'],
  ])('keeps the retired ?tab=%s deep link working', async (old, group, param) => {
    renderPage(`/vehicles/v1?tab=${old}`)
    expect(await screen.findByRole('tab', { name: group })).toHaveAttribute('aria-selected', 'true')
    await waitFor(() => expect(screen.getByTestId('search')).toHaveTextContent(param))
  })

  it('hides costs, fuel, KPI and compliance groups the user may not see', async () => {
    auth.granted = new Set(['vehicles.edit'])
    renderPage('/vehicles/v1?tab=kosten')
    expect(await screen.findByText('panel:leasing')).toBeInTheDocument()
    expect(screen.queryByText('panel:fuel')).not.toBeInTheDocument()
    expect(screen.queryByText('panel:kpi')).not.toBeInTheDocument()
    expect(screen.queryByRole('tab', { name: 'Documenten & keuringen' })).not.toBeInTheDocument()
    expect(screen.queryByRole('tab', { name: 'Onderhoud & schade' })).not.toBeInTheDocument()
    // Without audit rights the history tab would only say "no permission".
    expect(screen.queryByRole('tab', { name: 'Historiek' })).not.toBeInTheDocument()
  })

  it('never requests compliance lists without permission and then claims nothing', async () => {
    auth.granted = new Set([])
    renderPage()
    await screen.findByRole('heading', { name: 'Volvo FH' })
    await waitFor(() => expect(screen.getByText('Niets gepland.')).toBeInTheDocument())
    expect(listDocs).not.toHaveBeenCalled()
    expect(screen.queryByRole('region', { name: 'Aandacht' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Bewerken' })).not.toBeInTheDocument()
  })

  it('keeps deactivate and delete behind Meer, delete only with its permission', async () => {
    renderPage()
    await userEvent.click(await screen.findByRole('button', { name: 'Meer' }))
    expect(screen.getAllByRole('menuitem').map((i) => i.textContent)).toEqual(['Deactiveren', 'Verwijderen'])
  })

  it('offers no delete without vehicles.delete', async () => {
    auth.granted.delete('vehicles.delete')
    renderPage()
    await userEvent.click(await screen.findByRole('button', { name: 'Meer' }))
    expect(screen.getAllByRole('menuitem').map((i) => i.textContent)).toEqual(['Deactiveren'])
  })
})
