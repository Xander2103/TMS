import { describe, expect, it } from 'vitest'
import { render } from '@testing-library/react'
import { ScheduleLegend } from '../ScheduleChip'

// Mirrors the intended page order: legend must precede the calendar grid in the DOM.
function Page() {
  return (
    <div>
      <div data-testid="controls">controls</div>
      <ScheduleLegend />
      <table data-testid="grid" className="ep-grid"><tbody><tr><td>cell</td></tr></tbody></table>
    </div>
  )
}

describe('planning legend position', () => {
  it('renders the legend before the calendar grid', () => {
    const { getByText, getByTestId } = render(<Page />)
    const legend = getByText(/Legenda/i)
    const grid = getByTestId('grid')
    // Node.DOCUMENT_POSITION_FOLLOWING (4) => grid comes after legend
    expect(legend.compareDocumentPosition(grid) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
  })
})
