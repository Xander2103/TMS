import { describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, useSearchParams } from 'react-router-dom'
import { useSectionNavigation, firstSectionWithError } from '../useSectionNavigation'

function Harness({ ids }: { ids: string[] }) {
  const { activeId, setActive } = useSectionNavigation(ids, ids[0])
  const [params] = useSearchParams()
  return (
    <div>
      <span data-testid="active">{activeId}</span>
      <span data-testid="param">{params.get('section') ?? ''}</span>
      {ids.map((id) => (
        <button key={id} onClick={() => setActive(id)}>{id}</button>
      ))}
    </div>
  )
}

describe('useSectionNavigation', () => {
  it('defaults to the first section', () => {
    render(<MemoryRouter><Harness ids={['a', 'b']} /></MemoryRouter>)
    expect(screen.getByTestId('active')).toHaveTextContent('a')
  })

  it('reads the initial section from the URL', () => {
    render(<MemoryRouter initialEntries={['/?section=b']}><Harness ids={['a', 'b']} /></MemoryRouter>)
    expect(screen.getByTestId('active')).toHaveTextContent('b')
  })

  it('writes the active section to the URL on change', async () => {
    render(<MemoryRouter><Harness ids={['a', 'b']} /></MemoryRouter>)
    await userEvent.click(screen.getByRole('button', { name: 'b' }))
    expect(screen.getByTestId('param')).toHaveTextContent('b')
  })

  it('ignores an unknown section in the URL and falls back to default', () => {
    render(<MemoryRouter initialEntries={['/?section=zzz']}><Harness ids={['a', 'b']} /></MemoryRouter>)
    expect(screen.getByTestId('active')).toHaveTextContent('a')
  })
})

describe('firstSectionWithError', () => {
  const sections = [
    { id: 'a', fieldKeys: ['firstName', 'lastName'] },
    { id: 'b', fieldKeys: ['iban'] },
  ]
  it('returns the first section owning a failing field', () => {
    expect(firstSectionWithError(sections, { iban: 'bad' })).toBe('b')
  })
  it('returns null when there are no errors', () => {
    expect(firstSectionWithError(sections, {})).toBe(null)
    expect(firstSectionWithError(sections, null)).toBe(null)
  })
})
