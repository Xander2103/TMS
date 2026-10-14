import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, useLocation } from 'react-router-dom'
import { QualificationsOverviewPage } from '../pages/QualificationsOverviewPage'
import type { ExpiringQualification } from '../../employees/types/qualification'

const api = vi.hoisted(() => ({ getExpiringQualifications: vi.fn(), getExpiredQualifications: vi.fn() }))

vi.mock('../../employees/api/qualificationsApi', () => ({
  getExpiringQualifications: (...args: unknown[]) => api.getExpiringQualifications(...args),
  getExpiredQualifications: (...args: unknown[]) => api.getExpiredQualifications(...args),
}))

const EXPIRED: ExpiringQualification = {
  id: 'q-1',
  employeeId: 'e-1',
  employeeName: 'Jan Peeters',
  employeeNumber: 'P-001',
  qualificationTypeCode: 'CODE95',
  qualificationTypeName: 'Code 95',
  expiryDate: '2020-01-01',
  effectiveStatus: 'Expired',
  employeeIsDriver: true,
}

function Probe() {
  const location = useLocation()
  return <span data-testid="url">{location.search}</span>
}

function renderAt(url: string) {
  return render(
    <MemoryRouter initialEntries={[url]}>
      <QualificationsOverviewPage />
      <Probe />
    </MemoryRouter>,
  )
}

describe('QualificationsOverviewPage (B14)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    api.getExpiringQualifications.mockResolvedValue([])
    api.getExpiredQualifications.mockResolvedValue([EXPIRED])
  })

  afterEach(cleanup)

  it('restores the window from the URL and writes a new choice back to it', async () => {
    renderAt('/qualifications?window=60')
    await waitFor(() => expect(api.getExpiringQualifications).toHaveBeenCalledWith(60))
    expect(screen.getByRole('button', { name: '60 dagen' })).toHaveAttribute('aria-pressed', 'true')

    await userEvent.click(screen.getByRole('button', { name: '90 dagen' }))
    await waitFor(() => expect(api.getExpiringQualifications).toHaveBeenCalledWith(90))
    expect(screen.getByTestId('url')).toHaveTextContent('window=90')

    await userEvent.click(screen.getByRole('button', { name: '30 dagen' }))
    expect(screen.getByTestId('url')).toHaveTextContent(/^$/)
  })

  it('ignores an unknown window in the URL', async () => {
    renderAt('/qualifications?window=7')
    await waitFor(() => expect(api.getExpiringQualifications).toHaveBeenCalledWith(30))
  })

  it('shows an expired row on one line with the expiry spelled out, not by colour alone', async () => {
    renderAt('/qualifications')
    const row = (await screen.findByText('Jan Peeters')).closest('tr')!
    expect(within(row).getByText('P-001 · chauffeur')).toBeInTheDocument()
    expect(within(row).getByText(/dagen geleden/)).toBeInTheDocument()
    expect(within(row).getByText('Verlopen')).toBeInTheDocument()
  })
})
