import { describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useSectionNavigation, firstSectionWithError } from '../useSectionNavigation'

function Harness({ ids }: { ids: string[] }) {
  const { activeId, setActive } = useSectionNavigation(ids, ids[0])
  return (
    <div>
      <span data-testid="active">{activeId}</span>
      {ids.map((id) => (
        <button key={id} onClick={() => setActive(id)}>{id}</button>
      ))}
    </div>
  )
}

describe('useSectionNavigation', () => {
  it('defaults to the first section', () => {
    render(<Harness ids={['a', 'b']} />)
    expect(screen.getByTestId('active')).toHaveTextContent('a')
  })

  it('changes the active section on setActive (component state, no router)', async () => {
    render(<Harness ids={['a', 'b']} />)
    await userEvent.click(screen.getByRole('button', { name: 'b' }))
    expect(screen.getByTestId('active')).toHaveTextContent('b')
  })

  it('ignores an unknown section id', async () => {
    function UnknownHarness() {
      const { activeId, setActive } = useSectionNavigation(['a', 'b'], 'a')
      return (
        <div>
          <span data-testid="active">{activeId}</span>
          <button onClick={() => setActive('zzz')}>bad</button>
        </div>
      )
    }
    render(<UnknownHarness />)
    await userEvent.click(screen.getByRole('button', { name: 'bad' }))
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
