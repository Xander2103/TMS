import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { ApiError } from '../../../api/apiClient'
import { AssetDeactivateDialog, AssetDeleteDialog } from '../AssetLifecycleDialogs'
import { ReadinessBlock } from '../ReadinessBlock'
import { displaySeverity, readinessHint } from '../readiness'
import type { AssetReferences } from '../types'

const api = vi.hoisted(() => ({ getAssetReadiness: vi.fn(), getAssetReferences: vi.fn(), getAssetTrips: vi.fn() }))
vi.mock('../fleetReadinessApi', () => api)

const refs = (overrides: Partial<AssetReferences> = {}): AssetReferences => ({
  plannedTrips: 0, completedTrips: 0, otherTrips: 0, maintenance: 0, damage: 0, documents: 0, inspections: 0, fuel: 0,
  leasing: 0, tachograph: 0, tankCards: 0, isFixedDriverVehicle: false, isCurrentDriverVehicle: false, upcomingTrips: [],
  totalTrips: 0, hasReferences: false, ...overrides,
})

const wrap = (ui: React.ReactNode) => render(<MemoryRouter>{ui}</MemoryRouter>)

describe('fleet readiness helpers', () => {
  it('picker hint: blocked ⛔, warning ⚠, deployable or unknown nothing', () => {
    expect(readinessHint({ readiness: 'Blocked', readinessSummary: 'Onderhoud gepland (22/10)' })).toBe('⛔ Onderhoud gepland (22/10)')
    expect(readinessHint({ readiness: 'Warning', readinessSummary: 'Tachograafijking vervallen' })).toBe('⚠ Tachograafijking vervallen')
    expect(readinessHint({ readiness: 'Deployable', readinessSummary: null })).toBeNull()
    expect(readinessHint({})).toBeNull()
  })

  it('a conditional blocker (ADR, crane) reads as a warning on the asset page', () => {
    expect(displaySeverity({ code: 'adr.expired', message: '', severity: 'Blocking', conditional: 'adr' })).toBe('Warning')
    expect(displaySeverity({ code: 'inspection.expired', message: '', severity: 'Blocking' })).toBe('Blocking')
  })
})

describe('ReadinessBlock', () => {
  beforeEach(() => vi.clearAllMocks())

  it('shows the verdict badge and every reason from the server', async () => {
    api.getAssetReadiness.mockResolvedValue({
      assetId: 'v1', assetKind: 'Vehicle', internalNumber: 'VRT-0012', licensePlate: '1-TFG-001', date: '2026-10-22',
      level: 'Blocked', deployable: false, summary: 'Technische keuring verlopen',
      issues: [
        { code: 'inspection.expired', message: 'Technische keuring verlopen sinds 30/09/2026', severity: 'Blocking' },
        { code: 'maintenance.planned', message: 'Onderhoud gepland', severity: 'Blocking', from: '2026-10-22', until: '2026-10-23' },
        { code: 'adr.expired', message: 'ADR-certificaat verlopen', severity: 'Blocking', conditional: 'adr' },
      ],
    })
    wrap(<ReadinessBlock kind="vehicle" assetId="v1" />)
    expect(await screen.findByText('Niet inzetbaar')).toBeInTheDocument()
    expect(screen.getByText(/Technische keuring verlopen sinds 30\/09\/2026/)).toBeInTheDocument()
    expect(screen.getByText(/22\/10\/2026 – 23\/10\/2026/)).toBeInTheDocument()
    expect(screen.getByText(/ADR-certificaat verlopen/).closest('li')).toHaveClass('fleet-readiness-warning')
    expect(api.getAssetReadiness).toHaveBeenCalledWith('vehicle', 'v1')
  })

  it('says so when the verdict cannot be loaded', async () => {
    api.getAssetReadiness.mockRejectedValue(new Error('x'))
    wrap(<ReadinessBlock kind="trailer" assetId="t1" />)
    expect(await screen.findByText('De inzetbaarheid kon niet worden bepaald.')).toBeInTheDocument()
  })
})

describe('AssetDeleteDialog (FL-B1)', () => {
  beforeEach(() => vi.clearAllMocks())

  it('refuses a referenced vehicle, names the references and the trips, and offers Deactiveren', async () => {
    api.getAssetReferences.mockResolvedValue(refs({
      plannedTrips: 4, maintenance: 2, documents: 5, totalTrips: 4, hasReferences: true,
      upcomingTrips: [{ id: 'trip-1', tripNumber: 'RIT-0033', tripDate: '2026-10-21', status: 'Planned' }],
    }))
    const onDelete = vi.fn()
    const onDeactivate = vi.fn()
    wrap(<AssetDeleteDialog kind="vehicle" assetId="v1" number="VRT-0007" isActive canDeactivate onDelete={onDelete} onDeactivate={onDeactivate} onCancel={vi.fn()} />)
    expect(await screen.findByText(/kan niet worden verwijderd omdat het nog gekoppeld is aan: 4 geplande ritten, 2 onderhoudsbeurten, 5 documenten/)).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'RIT-0033' })).toHaveAttribute('href', '/planning/trip-1')
    expect(screen.queryByRole('button', { name: 'Verwijderen' })).not.toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Deactiveren' }))
    expect(onDeactivate).toHaveBeenCalled()
    expect(onDelete).not.toHaveBeenCalled()
  })

  it('deletes an unreferenced vehicle, and shows the server reason on a 409', async () => {
    api.getAssetReferences.mockResolvedValue(refs())
    const onDelete = vi.fn().mockRejectedValue(new ApiError('Dit voertuig kan niet worden verwijderd omdat het nog gekoppeld is aan 1 geplande rit.', 409))
    wrap(<AssetDeleteDialog kind="vehicle" assetId="v2" number="VRT-0015" isActive canDeactivate onDelete={onDelete} onDeactivate={vi.fn()} onCancel={vi.fn()} />)
    expect(await screen.findByText(/Er zijn geen ritten of wagenparkgegevens aan gekoppeld/)).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Verwijderen' }))
    expect(onDelete).toHaveBeenCalled()
    expect(await screen.findByText(/nog gekoppeld is aan 1 geplande rit/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Verwijderen' })).not.toBeInTheDocument()
  })
})

describe('AssetDeactivateDialog (FL-M8)', () => {
  it('lists the planned trips that will get a conflict, and still lets the user deactivate', async () => {
    api.getAssetReferences.mockResolvedValue(refs({
      plannedTrips: 2, totalTrips: 2, hasReferences: true,
      upcomingTrips: [
        { id: 'a', tripNumber: 'RIT-0033', tripDate: '2026-10-21', status: 'Planned' },
        { id: 'b', tripNumber: 'RIT-0035', tripDate: '2026-10-22', status: 'Planned' },
      ],
    }))
    const onConfirm = vi.fn()
    wrap(<AssetDeactivateDialog kind="vehicle" assetId="v1" message="VRT-0007 deactiveren?" confirmLabel="Deactiveren" onConfirm={onConfirm} onCancel={vi.fn()} />)
    expect(await screen.findByText('Dit voertuig heeft 2 geplande ritten:')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'RIT-0035' })).toHaveAttribute('href', '/planning/b')
    expect(screen.getByText(/niet geannuleerd of ontkoppeld/)).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Deactiveren' }))
    await waitFor(() => expect(onConfirm).toHaveBeenCalled())
  })
})
