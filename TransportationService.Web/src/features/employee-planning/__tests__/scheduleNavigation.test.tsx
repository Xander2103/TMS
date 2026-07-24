import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ScheduleChip } from '../components/ScheduleChip'
import type { ScheduleEntry } from '../types'

const tripEntry: ScheduleEntry = {
  state: 'Trip',
  shiftId: null,
  absenceId: null,
  tripId: 'trip-1',
  sourceType: 'Trip',
  label: 'RIT-0007',
  startTime: '08:00:00',
  endTime: '16:00:00',
  shiftType: null,
  workLocation: 'Antwerpen → Rotterdam',
  vehicleSummary: 'V-0001 · 1-ABC-123',
  statusLabel: 'Gepland',
  conflictSeverity: null,
  conflictNotes: null,
  colour: null,
  noteId: null,
}

describe('ScheduleChip interaction', () => {
  it('renders an accessible button with full description when clickable', async () => {
    const onClick = vi.fn()
    render(<ScheduleChip entry={tripEntry} onClick={onClick} />)

    const chip = screen.getByRole('button', { name: /Rit RIT-0007/ })
    expect(chip).toHaveAccessibleName(expect.stringContaining('Antwerpen → Rotterdam'))
    await userEvent.click(chip)
    expect(onClick).toHaveBeenCalledTimes(1)
  })

  it('renders a non-interactive chip without onClick (no permission → no hidden affordance)', () => {
    render(<ScheduleChip entry={tripEntry} />)

    expect(screen.queryByRole('button')).toBeNull()
    expect(screen.getByText('RIT-0007')).toBeInTheDocument()
  })

  it('marks conflicts visibly and in the accessible description', () => {
    render(
      <ScheduleChip
        entry={{ ...tripEntry, conflictSeverity: 'Blocking', conflictNotes: ['Overlapt met rit RIT-0008'] }}
        onClick={() => {}}
      />,
    )

    const chip = screen.getByRole('button')
    expect(chip.className).toContain('schedule-chip-conflict')
    expect(chip).toHaveAccessibleName(expect.stringContaining('Conflict (Blokkerend)'))
  })
})
