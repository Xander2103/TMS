import { describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import { TruncationNotice } from '../TruncationNotice'

/** PERF-H3: a capped list says so — and says nothing when everything fits. */
describe('TruncationNotice', () => {
  it('renders nothing when the list is complete', () => {
    const { container } = render(<TruncationNotice shown={25} total={25} />)
    expect(container).toBeEmptyDOMElement()
  })

  it('names the shown count and the real total when the server reported one', () => {
    render(<TruncationNotice shown={200} total={2173} />)
    expect(screen.getByRole('status')).toHaveTextContent('Eerste 200 van 2173 weergegeven — verfijn de zoekopdracht.')
  })

  it('names the cap when the server only reported a limit', () => {
    render(<TruncationNotice shown={500} limit={500} />)
    expect(screen.getByRole('status')).toHaveTextContent('De lijst toont maximaal 500 resultaten — verfijn de periode of filters.')
  })

  it('lets the caller word it for its own domain', () => {
    render(<TruncationNotice shown={500} limit={500} truncated message="De lijst toont maximaal 500 ritten — verfijn de periode of filters." />)
    expect(screen.getByRole('status')).toHaveTextContent('maximaal 500 ritten')
  })
})
