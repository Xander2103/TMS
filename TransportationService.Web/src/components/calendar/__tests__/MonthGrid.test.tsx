import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MonthGrid } from '../MonthGrid'

interface Item {
  label: string
}

// August 2026: the 1st is a Saturday, so the grid pads 5 days back to Monday 27 July and
// (to complete the 6th week) 6 days forward to Sunday 6 September — 42 cells total.
const anchor = new Date(2026, 7, 15)

describe('MonthGrid', () => {
  it('renders a full 6-week grid with correct leading/trailing pad cells for the anchor month', () => {
    render(<MonthGrid<Item> anchor={anchor} entriesByDate={new Map()} renderEntry={(item) => item.label} />)

    const cells = screen.getAllByRole('button')
    expect(cells).toHaveLength(42)

    // Leading pad: 27-31 July land in the grid but outside August.
    const firstCell = cells[0]
    expect(firstCell.className).toContain('cal-month-cell-outside')
    expect(firstCell).toHaveAccessibleName(expect.stringContaining('27 juli'))

    // Trailing pad: 1-6 September.
    const lastCell = cells[41]
    expect(lastCell.className).toContain('cal-month-cell-outside')
    expect(lastCell).toHaveAccessibleName(expect.stringContaining('6 september'))

    // A day inside August is not padded.
    const augFourth = cells.find((cell) => cell.textContent?.startsWith('4'))!
    expect(augFourth.className).not.toContain('cal-month-cell-outside')
  })

  it('places entries on the cell matching their ISO date and gives an accessible item count', () => {
    const entriesByDate = new Map<string, Item[]>([['2026-08-04', [{ label: 'Shift A' }]]])
    render(<MonthGrid<Item> anchor={anchor} entriesByDate={entriesByDate} renderEntry={(item) => item.label} />)

    expect(screen.getByText('Shift A')).toBeInTheDocument()
    // "dinsdag 4 augustus, 1 item" — nl-BE weekday + day + month, singular count.
    expect(screen.getByRole('button', { name: 'dinsdag 4 augustus, 1 item' })).toBeInTheDocument()
    // A day with no entries still gets an accessible label.
    expect(screen.getByRole('button', { name: 'woensdag 5 augustus, geen items' })).toBeInTheDocument()
  })

  it('shows a "+N meer" overflow marker beyond maxVisible entries', () => {
    const entriesByDate = new Map<string, Item[]>([
      ['2026-08-04', [{ label: 'Shift A' }, { label: 'Shift B' }, { label: 'Shift C' }]],
    ])
    render(<MonthGrid<Item> anchor={anchor} entriesByDate={entriesByDate} renderEntry={(item) => item.label} />)

    expect(screen.getByText('Shift A')).toBeInTheDocument()
    expect(screen.getByText('Shift B')).toBeInTheDocument()
    expect(screen.queryByText('Shift C')).toBeNull()
    expect(screen.getByText('+1 meer')).toBeInTheDocument()
  })

  it('highlights the injected "today" cell', () => {
    render(
      <MonthGrid<Item>
        anchor={anchor}
        entriesByDate={new Map()}
        renderEntry={(item) => item.label}
        today={new Date(2026, 7, 4)}
      />,
    )

    const todayCell = screen.getByRole('button', { name: /^dinsdag 4 augustus/ })
    expect(todayCell.className).toContain('cal-today')
  })

  it('calls onSelectDate with the ISO date of the clicked cell, including pad cells', async () => {
    const onSelectDate = vi.fn()
    render(
      <MonthGrid<Item> anchor={anchor} entriesByDate={new Map()} renderEntry={(item) => item.label} onSelectDate={onSelectDate} />,
    )

    await userEvent.click(screen.getByRole('button', { name: /^dinsdag 4 augustus/ }))
    expect(onSelectDate).toHaveBeenCalledWith('2026-08-04')

    await userEvent.click(screen.getByRole('button', { name: /^maandag 27 juli/ }))
    expect(onSelectDate).toHaveBeenCalledWith('2026-07-27')
  })
})
