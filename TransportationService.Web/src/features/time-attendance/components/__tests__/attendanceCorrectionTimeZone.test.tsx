import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import * as api from '../../api/timeAttendanceApi'
import { resetTimeZonePreference } from '../../../../utils/dates'
import type { AttendanceHistory } from '../../types'
import { AttendanceTab } from '../AttendanceTab'

/**
 * Wave 1 fix A (A13) — the attendance tab read the punch times through the tenant-zone formatters
 * (timeline row, correction heading, cancel confirmation) while its correction dialog pre-filled
 * and wrote back its `datetime-local` fields in the BROWSER zone. An HR user one hour off the
 * tenant zone saw a timeline row at 07:54 and a field saying 06:54 for the same punch — and saving
 * that field moved the punch by the browser offset.
 *
 * Unlike the dock board, attendance needs no data re-encoding to fix this: a punch is stamped
 * `UtcNow` server-side (`AttendanceService`) and a correction is stored as the instant it receives
 * (`AttendanceCorrectionService`), so these values are real instants in every row already. The
 * screen therefore moves ONTO the app-wide tenant-zone convention on both read and write rather
 * than back to the browser zone.
 *
 * The runner zone is Asia/Tokyo (UTC+9) so a browser-zone render cannot pass by accident.
 */
declare const process: { env: Record<string, string | undefined> }

const ORIGINAL_TZ = process.env.TZ

beforeAll(() => {
  process.env.TZ = 'Asia/Tokyo'
  expect(new Date('2026-08-20T05:54:00Z').getHours()).toBe(14)
})

afterAll(() => {
  if (ORIGINAL_TZ === undefined) delete process.env.TZ
  else process.env.TZ = ORIGINAL_TZ
})

afterEach(() => resetTimeZonePreference())

const auth = vi.hoisted(() => ({ permissions: ['attendance.view', 'attendance.correct'] as string[] }))
vi.mock('../../../auth/authContextValue', () => ({
  useAuth: () => ({
    hasPermission: (code: string) => auth.permissions.includes(code),
    hasAnyPermission: (codes: string[]) => codes.some((code) => auth.permissions.includes(code)),
  }),
}))

const toast = vi.hoisted(() => ({ showSuccess: vi.fn(), showError: vi.fn() }))
vi.mock('../../../../components/ui/toastContext', () => ({ useToast: () => toast }))

/** One session: clocked in at 07:54 Amsterdam (05:54Z) on 20 August. */
const history: AttendanceHistory = {
  from: '2026-08-07', to: '2026-08-20',
  totalGrossMinutes: 510, totalBreakMinutes: 30, totalNetMinutes: 480, totalPlannedMinutes: 480,
  days: [
    {
      date: '2026-08-20', grossMinutes: 510, breakMinutes: 30, netMinutes: 480,
      plannedMinutes: 480, deviationMinutes: 0,
      sessions: [
        {
          id: 's1', employeeId: 'e1', clockInAt: '2026-08-20T05:54:00Z', clockOutAt: '2026-08-20T14:24:00Z',
          status: 'Completed', clockInSource: 'Kiosk', locationId: null, locationName: 'Magazijn',
          grossMinutes: 510, breakMinutes: 30, netMinutes: 480, hasCorrections: false, version: 'v1',
          breaks: [], corrections: [],
        },
      ],
    },
  ],
}

beforeEach(() => {
  vi.restoreAllMocks()
  auth.permissions = ['attendance.view', 'attendance.correct']
  vi.spyOn(api, 'getEmployeeAttendance').mockResolvedValue(history)
  vi.spyOn(api, 'getAttendanceCredentialStatus').mockResolvedValue({
    hasCredential: false, isActive: false, lastUsedAt: null, lockedUntil: null,
  })
})

describe('AttendanceTab — one clock per screen (A13)', () => {
  it('pre-fills the correction field with the same wall clock the timeline shows', async () => {
    render(<AttendanceTab employeeId="e1" />)
    expect(await screen.findByText('07:54')).toBeInTheDocument() // timeline row, tenant zone

    await userEvent.click(screen.getAllByRole('button', { name: 'Corrigeren' })[0])

    const clockIn = (await screen.findByLabelText('Ingepunt')) as HTMLInputElement
    expect(clockIn.value).toBe('2026-08-20T07:54')
    expect(clockIn.value).not.toContain('T14:54') // the browser (Tokyo) clock
  })

  it('writes the corrected wall clock back as the instant it denotes', async () => {
    const correct = vi.spyOn(api, 'correctSession').mockResolvedValue({ version: 'v2' } as never)
    render(<AttendanceTab employeeId="e1" />)
    await screen.findByText('07:54')

    await userEvent.click(screen.getAllByRole('button', { name: 'Corrigeren' })[0])
    const clockIn = (await screen.findByLabelText('Ingepunt')) as HTMLInputElement
    await userEvent.clear(clockIn)
    await userEvent.type(clockIn, '2026-08-20T08:00')
    await userEvent.type(document.getElementById('corr-reason') as HTMLTextAreaElement, 'te laat aangemeld')
    await userEvent.click(screen.getByRole('button', { name: 'Correctie opslaan' }))

    expect(correct).toHaveBeenCalledTimes(1)
    // 08:00 Amsterdam on 20 August is 06:00Z — never 23:00Z, which is what a browser-zone
    // (Tokyo) reading of the same typed text would have stored.
    expect(correct.mock.calls[0][1].clockInAt).toBe('2026-08-20T06:00:00Z')
  })
})
