import { describe, expect, it } from 'vitest'
import { useState } from 'react'
import { fireEvent, render, screen } from '@testing-library/react'
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
})
