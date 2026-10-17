import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { FleetHistoryPanel } from '../FleetHistoryPanel'
import type { FleetHistoryEvent } from '../fleetHistoryApi'

const api = vi.hoisted(() => ({ getFleetHistory: vi.fn() }))
vi.mock('../fleetHistoryApi', async () => {
  const actual = await vi.importActual<typeof import('../fleetHistoryApi')>('../fleetHistoryApi')
  return { ...actual, getFleetHistory: api.getFleetHistory }
})

const event = (overrides: Partial<FleetHistoryEvent>): FleetHistoryEvent => ({
  id: Math.random().toString(36).slice(2),
  at: '2026-10-17T10:15:00Z',
  actorKind: 'User',
  actorName: 'Dev Admin',
  category: 'lifecycle',
  title: 'Voertuig aangemaakt — VRT-0012 · 1-TFG-001',
  detail: null,
  financial: false,
  linkPath: null,
  technical: null,
  ...overrides,
})

const story: FleetHistoryEvent[] = [
  event({ category: 'readiness', actorKind: 'System', actorName: null, title: 'Voertuig opnieuw inzetbaar', at: '2026-10-22T14:30:00Z' }),
  event({ category: 'maintenance', title: 'Onderhoud afgerond: Herstelling', detail: 'Afgerond op 22/10/2026 · kilometerstand 125.400 km', at: '2026-10-22T14:30:00Z', technical: 'MaintenanceRecord.Completed: {"Status":"Planned"} → {"Status":"Completed"}' }),
  event({ category: 'readiness', actorKind: 'System', actorName: null, title: 'Voertuig niet inzetbaar wegens onderhoud (22/10/2026)', at: '2026-10-22T06:00:00Z' }),
  event({ category: 'planning', title: 'Ingepland op RIT-0043', detail: 'Rit van 29/10/2026 · chauffeur TEST HR Inzetbaar (CH-0006)', at: '2026-10-17T12:00:00Z' }),
  event({ category: 'document', title: 'Technische keuring toegevoegd', detail: 'Nr. TEST-KEUR · geldig tot 30/09/2027', at: '2026-10-17T11:00:00Z' }),
  event({ category: 'fuel', title: 'Tankbeurt — 380 l · 125.600 km', at: '2026-10-17T10:30:00Z' }),
  event({ at: '2026-10-17T10:15:00Z' }),
]

describe('FleetHistoryPanel (FL-H3)', () => {
  beforeEach(() => {
    api.getFleetHistory.mockReset()
  })

  it('renders WHEN / WHO (Systeem for automatic moments) / WHAT for the vehicle story', async () => {
    api.getFleetHistory.mockResolvedValue(story)
    render(<FleetHistoryPanel assetKind="vehicle" assetId="v-1" />)
    expect(await screen.findByText('Onderhoud afgerond: Herstelling')).toBeInTheDocument()
    expect(api.getFleetHistory).toHaveBeenCalledWith('vehicle', 'v-1', expect.anything())
    expect(screen.getByText('Ingepland op RIT-0043')).toBeInTheDocument()
    expect(screen.getByText('Afgerond op 22/10/2026 · kilometerstand 125.400 km')).toBeInTheDocument()
    expect(screen.getAllByText('Systeem').length).toBe(2)
    expect(screen.getAllByText('Dev Admin').length).toBeGreaterThan(0)
    // Never raw codes on a normal line.
    expect(screen.queryByText(/InMaintenance|OperationalStatus|Available/)).not.toBeInTheDocument()
  })

  it('filters by operational question and shows no cost chip when no amounts or fuel/leasing lines exist', async () => {
    const user = userEvent.setup()
    api.getFleetHistory.mockResolvedValue(story.filter((e) => e.category !== 'fuel'))
    render(<FleetHistoryPanel assetKind="vehicle" assetId="v-1" />)
    const filters = await screen.findByRole('group', { name: 'Toon' })
    expect(within(filters).queryByRole('button', { name: 'Kosten' })).not.toBeInTheDocument()
    await user.click(within(filters).getByRole('button', { name: 'Planning' }))
    expect(screen.getByText('Ingepland op RIT-0043')).toBeInTheDocument()
    expect(screen.queryByText('Onderhoud afgerond: Herstelling')).not.toBeInTheDocument()
    await user.click(within(filters).getByRole('button', { name: 'Status & inzetbaarheid' }))
    expect(screen.getByText('Voertuig opnieuw inzetbaar')).toBeInTheDocument()
    expect(screen.queryByText('Ingepland op RIT-0043')).not.toBeInTheDocument()
  })

  it('offers the cost filter when the server sent financial lines, and keeps technical detail behind Uitklappen', async () => {
    const user = userEvent.setup()
    api.getFleetHistory.mockResolvedValue([
      event({ category: 'maintenance', title: 'Onderhoud afgerond: Periodiek onderhoud', detail: 'Afgerond op 18/10/2026 · kosten € 1.480,50', financial: true, technical: 'MaintenanceRecord.Completed: raw' }),
      ...story,
    ])
    render(<FleetHistoryPanel assetKind="trailer" assetId="t-1" />)
    const filters = await screen.findByRole('group', { name: 'Toon' })
    await user.click(within(filters).getByRole('button', { name: 'Kosten' }))
    expect(screen.getByText('Onderhoud afgerond: Periodiek onderhoud')).toBeInTheDocument()
    expect(screen.getByText('Tankbeurt — 380 l · 125.600 km')).toBeInTheDocument()
    expect(screen.queryByText('Ingepland op RIT-0043')).not.toBeInTheDocument()
    expect(screen.queryByText('MaintenanceRecord.Completed: raw')).not.toBeInTheDocument()
    await user.click(screen.getAllByRole('button', { name: 'Uitklappen' })[0])
    expect(screen.getByText('MaintenanceRecord.Completed: raw')).toBeInTheDocument()
    expect(api.getFleetHistory).toHaveBeenCalledWith('trailer', 't-1', expect.anything())
  })

  it('shows the load error and the empty state in Dutch', async () => {
    api.getFleetHistory.mockRejectedValueOnce(new Error('boom'))
    const { unmount } = render(<FleetHistoryPanel assetKind="vehicle" assetId="v-err" />)
    expect(await screen.findByText('De historiek kon niet worden geladen.')).toBeInTheDocument()
    unmount()
    api.getFleetHistory.mockResolvedValueOnce([])
    render(<FleetHistoryPanel assetKind="vehicle" assetId="v-empty" />)
    expect(await screen.findByText('Nog geen gebeurtenissen.')).toBeInTheDocument()
  })
})
