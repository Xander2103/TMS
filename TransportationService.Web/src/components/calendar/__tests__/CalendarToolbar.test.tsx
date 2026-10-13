import { afterEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { CalendarToolbar } from '../CalendarToolbar'

describe('CalendarToolbar', () => {
  afterEach(() => {
    vi.useRealTimers()
  })

  it('shows the Dutch period label per view', () => {
    const { rerender } = render(
      <CalendarToolbar anchor={new Date(2026, 7, 15)} view="month" onViewChange={vi.fn()} onNavigate={vi.fn()} />,
    )
    expect(screen.getByText('augustus 2026')).toBeInTheDocument()

    rerender(<CalendarToolbar anchor={new Date(2026, 7, 4)} view="week" onViewChange={vi.fn()} onNavigate={vi.fn()} />)
    expect(screen.getByText('week van 3 augustus 2026')).toBeInTheDocument()

    rerender(
      <CalendarToolbar anchor={new Date(2026, 7, 4)} view="list" onViewChange={vi.fn()} onNavigate={vi.fn()} listStepDays={14} />,
    )
    expect(screen.getByText('3 augustus – 16 augustus 2026')).toBeInTheDocument()
  })

  it('calls onViewChange when a view button is clicked', async () => {
    const onViewChange = vi.fn()
    render(<CalendarToolbar anchor={new Date(2026, 7, 15)} view="month" onViewChange={onViewChange} onNavigate={vi.fn()} />)

    await userEvent.click(screen.getByRole('button', { name: 'Week' }))
    expect(onViewChange).toHaveBeenCalledWith('week')
  })

  it('steps the anchor by a whole month in month view', async () => {
    const onNavigate = vi.fn()
    render(<CalendarToolbar anchor={new Date(2026, 7, 15)} view="month" onViewChange={vi.fn()} onNavigate={onNavigate} />)

    await userEvent.click(screen.getByRole('button', { name: 'Volgende periode' }))
    expect(onNavigate).toHaveBeenCalledWith(new Date(2026, 8, 1))

    await userEvent.click(screen.getByRole('button', { name: 'Vorige periode' }))
    expect(onNavigate).toHaveBeenCalledWith(new Date(2026, 6, 1))
  })

  it('steps the anchor by 7 days in week view', async () => {
    const onNavigate = vi.fn()
    render(<CalendarToolbar anchor={new Date(2026, 7, 4)} view="week" onViewChange={vi.fn()} onNavigate={onNavigate} />)

    await userEvent.click(screen.getByRole('button', { name: 'Volgende periode' }))
    expect(onNavigate).toHaveBeenCalledWith(new Date(2026, 7, 11))

    await userEvent.click(screen.getByRole('button', { name: 'Vorige periode' }))
    expect(onNavigate).toHaveBeenCalledWith(new Date(2026, 6, 28))
  })

  it('steps the anchor by the configured list window in list view', async () => {
    const onNavigate = vi.fn()
    render(
      <CalendarToolbar
        anchor={new Date(2026, 7, 3)}
        view="list"
        onViewChange={vi.fn()}
        onNavigate={onNavigate}
        listStepDays={28}
      />,
    )

    await userEvent.click(screen.getByRole('button', { name: 'Volgende periode' }))
    expect(onNavigate).toHaveBeenCalledWith(new Date(2026, 7, 31))
  })

  it('jumps to today, snapped per view (start-of-month for month, Monday for week/list)', async () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date(2026, 6, 10, 12, 0, 0)) // 10 July 2026 (Friday)

    const onNavigateMonth = vi.fn()
    const { rerender } = render(
      <CalendarToolbar anchor={new Date(2026, 7, 15)} view="month" onViewChange={vi.fn()} onNavigate={onNavigateMonth} />,
    )
    fireEvent.click(screen.getByRole('button', { name: 'Vandaag' }))
    expect(onNavigateMonth).toHaveBeenCalledWith(new Date(2026, 6, 1))

    const onNavigateWeek = vi.fn()
    rerender(<CalendarToolbar anchor={new Date(2026, 7, 15)} view="week" onViewChange={vi.fn()} onNavigate={onNavigateWeek} />)
    fireEvent.click(screen.getByRole('button', { name: 'Vandaag' }))
    // Monday of the week containing 10 July 2026.
    expect(onNavigateWeek).toHaveBeenCalledWith(new Date(2026, 6, 6))
  })
})
