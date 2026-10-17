import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { DamagePanel } from '../DamagePanel'
import type { DamageReport } from '../../types'

// FL-M7: damage reports carry an optional trip, costs at reporting time, and a photo/document.

vi.mock('../../../auth/authContextValue', () => ({ useAuth: () => ({ hasPermission: () => true }) }))
const toast = vi.hoisted(() => ({ showSuccess: vi.fn(), showError: vi.fn(), showToast: vi.fn() }))
vi.mock('../../../../components/ui/toastContext', () => ({ useToast: () => toast }))
vi.mock('../../../drivers/api/driversApi', () => ({ searchDrivers: () => Promise.resolve({ items: [] }) }))
vi.mock('../../../fleet-readiness/fleetReadinessApi', () => ({
  getAssetTrips: () => Promise.resolve([{ id: 'trip-1', tripNumber: 'RIT-0043', tripDate: '2026-10-29', status: 'Planned' }]),
}))
const api = vi.hoisted(() => ({
  listDamageReports: vi.fn(),
  createDamageReport: vi.fn(),
  updateDamageReport: vi.fn(),
  deleteDamageReport: vi.fn(),
  uploadDamageAttachment: vi.fn(),
  downloadDamageAttachment: vi.fn(),
  deleteDamageAttachment: vi.fn(),
  DAMAGE_ATTACHMENT_ACCEPT: '.pdf,.jpg,.jpeg,.png',
}))
vi.mock('../../api/damageApi', () => api)

const report = (overrides: Partial<DamageReport> = {}): DamageReport => ({
  id: 'd1', vehicleId: 'v1', trailerId: null, driverId: null, driverName: null, incidentDate: '2026-10-29', location: null,
  description: 'Aanrijding bij lossen', severity: 'Severe', status: 'Reported', insuranceReference: null, repairCost: null,
  downtimeDays: null, hasAttachment: false, notes: null, tripId: 'trip-1', tripNumber: 'RIT-0043', ...overrides,
})

const renderPanel = () => render(<MemoryRouter><DamagePanel ownerType="vehicle" ownerId="v1" /></MemoryRouter>)

beforeEach(() => {
  vi.clearAllMocks()
  api.listDamageReports.mockResolvedValue([report()])
  api.createDamageReport.mockResolvedValue(report())
  api.uploadDamageAttachment.mockResolvedValue(report({ hasAttachment: true }))
})

describe('DamagePanel (FL-M7)', () => {
  it('shows the trip link and uploads a photo for a report without attachment', async () => {
    renderPanel()
    const row = (await screen.findByText('Aanrijding bij lossen')).closest('tr')!
    expect(within(row).getByRole('link', { name: 'RIT-0043' })).toHaveAttribute('href', '/planning/trip-1')
    await userEvent.click(within(row).getByRole('button', { name: 'Uploaden' }))
    const file = new File(['x'], 'foto.jpg', { type: 'image/jpeg' })
    await userEvent.upload(screen.getByTestId('damage-file-input'), file)
    await waitFor(() => expect(api.uploadDamageAttachment).toHaveBeenCalledWith('d1', file))
  })

  it('a report with attachment offers download', async () => {
    api.listDamageReports.mockResolvedValue([report({ hasAttachment: true })])
    renderPanel()
    const row = (await screen.findByText('Aanrijding bij lossen')).closest('tr')!
    await userEvent.click(within(row).getByRole('button', { name: 'Downloaden' }))
    expect(api.downloadDamageAttachment).toHaveBeenCalledWith('d1', 'schade-2026-10-29')
  })

  it('reports with trip, repair cost and downtime already at creation', async () => {
    api.listDamageReports.mockResolvedValue([])
    renderPanel()
    await userEvent.click(await screen.findByRole('button', { name: 'Schade melden' }))
    await userEvent.type(screen.getByLabelText(/Omschrijving/), 'Spiegel afgebroken')
    await screen.findByRole('option', { name: /RIT-0043/ })
    await userEvent.selectOptions(screen.getByLabelText(/^Rit/), 'trip-1')
    await userEvent.type(screen.getByLabelText(/Herstelkost/), '350.5')
    await userEvent.type(screen.getByLabelText(/Stilstand/), '2')
    await userEvent.click(screen.getByRole('button', { name: 'Opslaan' }))
    await waitFor(() =>
      expect(api.createDamageReport).toHaveBeenCalledWith('vehicle', 'v1', expect.objectContaining({ tripId: 'trip-1', repairCost: 350.5, downtimeDays: 2 })),
    )
  })
})
