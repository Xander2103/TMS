import { describe, expect, it } from 'vitest'
import { useState } from 'react'
import { fireEvent, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { OpeningHoursEditor } from '../OpeningHoursEditor'
import type { LocationOpeningInterval } from '../../types'

// The editor is fully controlled, so tests drive it through a tiny stateful harness that
// also records the latest emitted value + validity.
const last: { value: LocationOpeningInterval[]; isValid: boolean } = { value: [], isValid: true }

function Harness({ initial = [] as LocationOpeningInterval[] }) {
  const [value, setValue] = useState<LocationOpeningInterval[]>(initial)
  return (
    <OpeningHoursEditor
      value={value}
      onChange={(next, isValid) => {
        setValue(next)
        last.value = next
        last.isValid = isValid
      }}
    />
  )
}

const monday = (fromTime: string, toTime: string, note: string | null = null): LocationOpeningInterval => ({
  dayOfWeek: 1,
  fromTime,
  toTime,
  note,
})

describe('OpeningHoursEditor', () => {
  it('renders all seven days as "Gesloten" when there are no intervals', () => {
    render(<Harness />)
    for (const label of ['Ma', 'Di', 'Wo', 'Do', 'Vr', 'Za', 'Zo']) {
      expect(screen.getByText(label)).toBeInTheDocument()
    }
    expect(screen.getAllByText('Gesloten')).toHaveLength(7)
  })

  it('adds an interval for the clicked day with ISO day numbering (Za = 6)', async () => {
    render(<Harness />)
    await userEvent.click(screen.getByRole('button', { name: 'Tijdvak toevoegen (Za)' }))
    expect(last.value).toEqual([{ dayOfWeek: 6, fromTime: '08:00', toTime: '17:00', note: null }])
    expect(last.isValid).toBe(true)
    expect(screen.getAllByText('Gesloten')).toHaveLength(6)
  })

  it('flags an interval whose end is not after its start', () => {
    render(<Harness initial={[monday('09:00', '12:00')]} />)
    fireEvent.change(screen.getByLabelText('Tot (Ma)'), { target: { value: '08:00' } })
    expect(screen.getByText('Eindtijd moet na starttijd liggen.')).toBeInTheDocument()
    expect(last.isValid).toBe(false)
  })

  it('flags overlapping intervals within the same day', () => {
    render(<Harness initial={[monday('08:00', '12:00'), monday('13:00', '17:00')]} />)
    // Pull the second interval's start into the first window.
    fireEvent.change(screen.getAllByLabelText('Van (Ma)')[1], { target: { value: '11:00' } })
    expect(screen.getAllByText('Tijdvakken overlappen.')).toHaveLength(2)
    expect(last.isValid).toBe(false)
  })

  it('does not treat touching intervals as overlap', () => {
    render(<Harness initial={[monday('08:00', '12:00'), monday('12:00', '17:00')]} />)
    fireEvent.change(screen.getAllByLabelText('Notitie (Ma)')[0], { target: { value: 'voormiddag' } })
    expect(screen.queryByText('Tijdvakken overlappen.')).not.toBeInTheDocument()
    expect(last.isValid).toBe(true)
  })

  it('copies monday to Di–Vr, leaving the weekend untouched', async () => {
    render(
      <Harness
        initial={[
          monday('08:00', '12:00'),
          monday('13:00', '17:00', 'namiddag'),
          { dayOfWeek: 6, fromTime: '09:00', toTime: '12:00', note: null },
        ]}
      />,
    )
    await userEvent.click(screen.getByRole('button', { name: 'Kopieer maandag naar weekdagen' }))
    for (const day of [2, 3, 4, 5]) {
      expect(last.value).toContainEqual({ dayOfWeek: day, fromTime: '08:00', toTime: '12:00', note: null })
      expect(last.value).toContainEqual({ dayOfWeek: day, fromTime: '13:00', toTime: '17:00', note: 'namiddag' })
    }
    // Saturday untouched, monday kept, total = 2 (ma) + 8 (di-vr) + 1 (za).
    expect(last.value).toContainEqual({ dayOfWeek: 6, fromTime: '09:00', toTime: '12:00', note: null })
    expect(last.value).toHaveLength(11)
    expect(last.isValid).toBe(true)
  })

  it('clears everything with "Wis alles"', async () => {
    render(<Harness initial={[monday('08:00', '12:00')]} />)
    await userEvent.click(screen.getByRole('button', { name: 'Wis alles' }))
    expect(last.value).toEqual([])
    expect(last.isValid).toBe(true)
    expect(screen.getAllByText('Gesloten')).toHaveLength(7)
  })

  it('removes a single interval', async () => {
    render(<Harness initial={[monday('08:00', '12:00'), monday('13:00', '17:00')]} />)
    await userEvent.click(screen.getAllByRole('button', { name: 'Tijdvak verwijderen (Ma)' })[0])
    expect(last.value).toEqual([monday('13:00', '17:00')])
  })

  // --- 24h TimeInput + grid layout (UX sprint 2026-09-09) ---

  it('accepts 08:00→12:00 without an error and keeps "12:00" as 24h text in the DOM', () => {
    render(<Harness initial={[monday('08:00', '12:00')]} />)
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    const to = screen.getByLabelText('Tot (Ma)') as HTMLInputElement
    expect(to.value).toBe('12:00')
    // A text field, never the native (AM/PM-prone) time control.
    expect(to).toHaveAttribute('type', 'text')
    fireEvent.change(to, { target: { value: '12:00' } })
    expect(to.value).toBe('12:00')
    expect(screen.queryByDisplayValue(/AM|PM/)).not.toBeInTheDocument()
    expect(last.isValid).toBe(true)
  })

  it('round-trips 00:00 as a start time', () => {
    render(<Harness initial={[monday('00:00', '06:00')]} />)
    const from = screen.getByLabelText('Van (Ma)') as HTMLInputElement
    expect(from.value).toBe('00:00')
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    fireEvent.change(from, { target: { value: '01:00' } })
    expect(last.value[0].fromTime).toBe('01:00')
    fireEvent.change(from, { target: { value: '00:00' } })
    expect(last.value[0].fromTime).toBe('00:00')
    expect(last.isValid).toBe(true)
    expect(from.value).toBe('00:00')
  })

  it('accepts 13:00→17:00 as a valid afternoon window', () => {
    render(<Harness initial={[monday('13:00', '17:00')]} />)
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    fireEvent.change(screen.getByLabelText('Tot (Ma)'), { target: { value: '17:30' } })
    expect(last.value[0]).toEqual(monday('13:00', '17:30'))
    expect(last.isValid).toBe(true)
  })

  it('renders two Monday intervals as two rows inside the Monday group, both labelled for Ma', () => {
    const { container } = render(<Harness initial={[monday('08:00', '12:00'), monday('13:00', '17:00')]} />)
    const mondayGroup = screen.getByRole('group', { name: 'Ma' })
    const rows = mondayGroup.querySelectorAll('[data-interval-row]')
    expect(rows).toHaveLength(2)
    expect(container.querySelectorAll('[data-interval-row]')).toHaveLength(2)
    const second = rows[1] as HTMLElement
    expect(within(second).getByLabelText('Van (Ma)')).toHaveValue('13:00')
    expect(within(second).getByLabelText('Tot (Ma)')).toHaveValue('17:00')
    expect(within(second).getByLabelText('Notitie (Ma)')).toBeInTheDocument()
    expect(within(second).getByRole('button', { name: 'Tijdvak verwijderen (Ma)' })).toBeInTheDocument()
    // "+ Tijdvak" is the last row of the day; the label is rendered once, first.
    expect(mondayGroup.lastElementChild).toHaveAccessibleName('Tijdvak toevoegen (Ma)')
    expect(mondayGroup.firstElementChild).toHaveTextContent('Ma')
    expect(within(mondayGroup).getAllByText('Ma')).toHaveLength(1)
    // Other days only show "Gesloten" + "+ Tijdvak".
    expect(within(screen.getByRole('group', { name: 'Di' })).getByText('Gesloten')).toBeInTheDocument()
  })

  it('keeps the note and remove button of an interval in place when its error row appears', () => {
    render(<Harness initial={[monday('08:00', '12:00', 'voormiddag')]} />)
    const row = screen.getByRole('group', { name: 'Ma' }).querySelector('[data-interval-row]') as HTMLElement
    const before = Array.from(row.children).map((el) => el.className.split(' ')[0])
    fireEvent.change(within(row).getByLabelText('Tot (Ma)'), { target: { value: '07:00' } })
    const alert = within(row).getByRole('alert')
    expect(alert).toHaveTextContent('Eindtijd moet na starttijd liggen.')
    const after = Array.from(row.children).map((el) => el.className.split(' ')[0])
    // Same cells in the same order; the error is appended as its own (last) row of the interval.
    expect(after.slice(0, before.length)).toEqual(before)
    expect(after).toHaveLength(before.length + 1)
    expect(row.lastElementChild).toBe(alert)
    expect(within(row).getByLabelText('Notitie (Ma)')).toHaveValue('voormiddag')
    expect(within(row).getByRole('button', { name: 'Tijdvak verwijderen (Ma)' })).toBeInTheDocument()
  })

  it('copies exact HH:mm values (08:00–12:00 and 13:00–17:00) from monday to weekdays', async () => {
    render(<Harness initial={[monday('08:00', '12:00'), monday('13:00', '17:00')]} />)
    await userEvent.click(screen.getByRole('button', { name: 'Kopieer maandag naar weekdagen' }))
    for (const day of [2, 3, 4, 5]) {
      expect(last.value).toContainEqual({ dayOfWeek: day, fromTime: '08:00', toTime: '12:00', note: null })
      expect(last.value).toContainEqual({ dayOfWeek: day, fromTime: '13:00', toTime: '17:00', note: null })
    }
    expect(last.isValid).toBe(true)
    // The DOM shows the copies as 24h text too.
    const friday = screen.getByRole('group', { name: 'Vr' })
    expect(within(friday).getAllByLabelText('Van (Vr)').map((el) => (el as HTMLInputElement).value)).toEqual(['08:00', '13:00'])
    expect(within(friday).getAllByLabelText('Tot (Vr)').map((el) => (el as HTMLInputElement).value)).toEqual(['12:00', '17:00'])
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('rejects 08:00→00:00 (windows must end on the same day)', () => {
    render(<Harness initial={[monday('08:00', '12:00')]} />)
    fireEvent.change(screen.getByLabelText('Tot (Ma)'), { target: { value: '00:00' } })
    expect(screen.getByRole('alert')).toHaveTextContent('Eindtijd moet na starttijd liggen.')
    expect(last.value[0].toTime).toBe('00:00')
    expect(last.isValid).toBe(false)
  })

  it('shows the incomplete error when a time is cleared', () => {
    render(<Harness initial={[monday('08:00', '12:00')]} />)
    fireEvent.change(screen.getByLabelText('Van (Ma)'), { target: { value: '' } })
    expect(screen.getByRole('alert')).toHaveTextContent('Vul start- en eindtijd in.')
    expect(last.value[0].fromTime).toBe('')
    expect(last.isValid).toBe(false)
  })

  it('exposes each weekday as a labelled group', () => {
    render(<Harness />)
    for (const label of ['Ma', 'Di', 'Wo', 'Do', 'Vr', 'Za', 'Zo']) {
      expect(screen.getByRole('group', { name: label })).toBeInTheDocument()
    }
  })
})
