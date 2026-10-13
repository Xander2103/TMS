import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { WeekGrid } from '../WeekGrid'

// The grid deliberately carries no ARIA row/gridcell roles (see WeekGrid's doc comment), so day
// columns are located by class instead of role="row".

interface Item {
  label: string
}

// Tuesday 4 August 2026 — the week runs Monday 3 August .. Sunday 9 August.
const anchor = new Date(2026, 7, 4)

describe('WeekGrid', () => {
  it('renders 7 day columns with date headers for the anchor week (Monday-first)', () => {
    const { container } = render(<WeekGrid<Item> anchor={anchor} entriesByDate={new Map()} renderEntry={(item) => item.label} />)

    expect(screen.getByText('ma 03/08')).toBeInTheDocument()
    expect(screen.getByText('di 04/08')).toBeInTheDocument()
    expect(screen.getByText('wo 05/08')).toBeInTheDocument()
    expect(screen.getByText('do 06/08')).toBeInTheDocument()
    expect(screen.getByText('vr 07/08')).toBeInTheDocument()
    expect(screen.getByText('za 08/08')).toBeInTheDocument()
    expect(screen.getByText('zo 09/08')).toBeInTheDocument()
    expect(container.querySelectorAll('.cal-week-day')).toHaveLength(7)
  })

  it('places entries under the column matching their ISO date', () => {
    const entriesByDate = new Map<string, Item[]>([
      ['2026-08-05', [{ label: 'Shift Wo' }]],
      ['2026-08-09', [{ label: 'Shift Zo' }]],
    ])
    render(<WeekGrid<Item> anchor={anchor} entriesByDate={entriesByDate} renderEntry={(item) => item.label} />)

    const wednesday = screen.getByText('wo 05/08').closest('.cal-week-day')!
    expect(wednesday.textContent).toContain('Shift Wo')
    expect(wednesday.textContent).not.toContain('Shift Zo')

    const sunday = screen.getByText('zo 09/08').closest('.cal-week-day')!
    expect(sunday.textContent).toContain('Shift Zo')
  })

  it('shows the empty-day fallback label when a column has no entries', () => {
    render(<WeekGrid<Item> anchor={anchor} entriesByDate={new Map()} renderEntry={(item) => item.label} emptyLabel="vrij" />)

    expect(screen.getAllByText('vrij')).toHaveLength(7)
  })

  it('highlights the injected "today" column', () => {
    render(
      <WeekGrid<Item> anchor={anchor} entriesByDate={new Map()} renderEntry={(item) => item.label} today={new Date(2026, 7, 5)} />,
    )

    const wednesday = screen.getByText('wo 05/08').closest('.cal-week-day')!
    expect(wednesday.className).toContain('cal-today')
  })

  it('calls onSelectDate with the ISO date when a day header is clickable', async () => {
    const onSelectDate = vi.fn()
    render(
      <WeekGrid<Item> anchor={anchor} entriesByDate={new Map()} renderEntry={(item) => item.label} onSelectDate={onSelectDate} />,
    )

    await userEvent.click(screen.getByText('wo 05/08'))
    expect(onSelectDate).toHaveBeenCalledWith('2026-08-05')
  })
})
