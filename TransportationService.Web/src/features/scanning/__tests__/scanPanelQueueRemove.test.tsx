import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ScanPanel } from '../components/ScanPanel'
import type { QueuedScan } from '../scanQueue'

/** B47: removing an unsent offline scan loses it — the scan panel asks first. */

const queue = vi.hoisted(() => ({
  subscribe: vi.fn(),
  replay: vi.fn(),
  remove: vi.fn(),
  retry: vi.fn(),
  enqueue: vi.fn(),
}))
vi.mock('../scanQueue', () => ({ scanQueue: queue }))
vi.mock('../api/scanningApi', () => ({
  getStopScanSummary: vi.fn().mockResolvedValue({ scanType: 'Load', unexpectedScanCount: 0, items: [] }),
  listScans: vi.fn().mockResolvedValue([]),
  submitScan: vi.fn(),
  correctScan: vi.fn(),
}))
vi.mock('../../packages/api/packagesApi', () => ({
  getTripPackageChecklist: vi.fn().mockResolvedValue({ stops: [] }),
  markPackageMissing: vi.fn(),
}))
vi.mock('../components/CameraScanner', () => ({ CameraScanner: () => null }))
vi.mock('../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }),
}))
vi.mock('../../auth/authContextValue', () => ({
  useAuth: () => ({ hasPermission: () => true }),
}))

const failed: QueuedScan = {
  clientEventId: 'evt-1', tripId: 'trip-1', stopId: 'stop-1',
  input: { barcode: 'PKG-0042' } as QueuedScan['input'],
  queuedAt: '2026-10-15T08:00:00Z', attempts: 2, state: 'failed', lastError: 'Afgekeurd',
}

beforeEach(() => {
  vi.clearAllMocks()
  queue.subscribe.mockImplementation((listener: (items: QueuedScan[]) => void) => {
    listener([failed])
    return () => {}
  })
  queue.replay.mockResolvedValue({ processed: 0, succeeded: 0, failed: 0, remaining: 1 })
})

function renderPanel() {
  return render(
    <ScanPanel tripId="trip-1" stopId="stop-1" stopLabel="RIT-1 — Antwerpen" scanType="Load" canCorrect onClose={() => {}} />,
  )
}

describe('ScanPanel — offline queue', () => {
  it('does not drop a queued scan until the removal is confirmed', async () => {
    renderPanel()
    const queueSection = await screen.findByRole('region', { name: 'Wachtrij' })
    await userEvent.click(within(queueSection).getByRole('button', { name: 'Verwijderen' }))

    const confirm = await screen.findByRole('dialog', { name: 'Scan uit wachtrij verwijderen' })
    expect(confirm).toHaveTextContent('PKG-0042')
    expect(queue.remove).not.toHaveBeenCalled()

    await userEvent.click(within(confirm).getByRole('button', { name: 'Annuleren' }))
    expect(queue.remove).not.toHaveBeenCalled()

    await userEvent.click(within(queueSection).getByRole('button', { name: 'Verwijderen' }))
    const again = await screen.findByRole('dialog', { name: 'Scan uit wachtrij verwijderen' })
    await userEvent.click(within(again).getByRole('button', { name: 'Verwijderen' }))
    expect(queue.remove).toHaveBeenCalledWith('evt-1')
  })

  it('retries without asking (nothing is lost)', async () => {
    renderPanel()
    const queueSection = await screen.findByRole('region', { name: 'Wachtrij' })
    await userEvent.click(within(queueSection).getByRole('button', { name: 'Opnieuw' }))
    expect(queue.retry).toHaveBeenCalledWith('evt-1')
  })
})
