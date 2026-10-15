import { describe, expect, it, vi } from 'vitest'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom'
import { AttentionList } from '../AttentionList'
import { DetailCard, DetailGrid } from '../DetailCard'
import { FactList } from '../FactList'
import { useDetailTab } from '../../../hooks/useDetailTab'
import { PageHeader } from '../../layout/PageHeader'

describe('DetailCard', () => {
  it('names its section by the title and keeps the action in the title row', () => {
    render(
      <DetailGrid>
        <DetailCard title="Contact" aside="2" action={<button>Bewerken</button>} id="contact">
          <p>body</p>
        </DetailCard>
      </DetailGrid>,
    )
    const region = screen.getByRole('region', { name: 'Contact' })
    expect(region).toHaveAttribute('id', 'contact')
    expect(within(region).getByRole('button', { name: 'Bewerken' }).closest('header')).not.toBeNull()
  })
})

describe('FactList', () => {
  it('renders empty values as a muted dash and skips falsy facts', () => {
    render(<FactList columns={2} facts={[{ label: 'Status', value: 'Open' }, null, false, { label: 'Dossier', value: null }]} />)
    expect(screen.getAllByRole('term').map((n) => n.textContent)).toEqual(['Status', 'Dossier'])
    const empty = screen.getAllByRole('definition')[1]
    expect(empty).toHaveTextContent('—')
    expect(empty).toHaveClass('is-empty')
  })
})

describe('AttentionList', () => {
  it('renders nothing when there is nothing to act on', () => {
    const { container } = render(<AttentionList items={[]} />)
    expect(container).toBeEmptyDOMElement()
  })

  it('takes the colour of the worst item and labels severity in words', async () => {
    const jump = vi.fn()
    render(
      <MemoryRouter>
        <AttentionList
          items={[
            { key: 'a', severity: 'warning', text: 'Keuring verloopt op 25/10/2026', action: { label: 'Bekijken', onSelect: jump } },
            { key: 'b', severity: 'critical', text: 'Open schade: 1', action: { label: 'Naar schade', to: '/x' } },
          ]}
        />
      </MemoryRouter>,
    )
    const region = screen.getByRole('region', { name: 'Aandacht' })
    expect(region).toHaveClass('is-critical')
    expect(screen.getByRole('img', { name: 'Kritiek' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Naar schade' })).toHaveAttribute('href', '/x')
    await userEvent.click(screen.getByRole('button', { name: 'Bekijken' }))
    expect(jump).toHaveBeenCalled()
  })
})

describe('PageHeader badges', () => {
  it('puts status badges on the title line', () => {
    render(<PageHeader title="Volvo FH" badges={<span>Actief</span>} />)
    expect(screen.getByRole('heading', { name: 'Volvo FH' }).parentElement).toHaveTextContent('Actief')
  })
})

function TabProbe() {
  const { tab, setTab } = useDetailTab({
    ids: ['overzicht', 'kosten', 'historiek'] as const,
    defaultId: 'overzicht',
    aliases: { brandstof: { tab: 'kosten', params: { focus: 'fuel' } } },
  })
  const location = useLocation()
  return (
    <>
      <output data-testid="tab">{tab}</output>
      <output data-testid="search">{location.search}</output>
      <button onClick={() => setTab('historiek')}>historiek</button>
      <button onClick={() => setTab('overzicht')}>overzicht</button>
    </>
  )
}

function renderProbe(url: string) {
  return render(
    <MemoryRouter initialEntries={[url]}>
      <Routes>
        <Route path="/v/:id" element={<TabProbe />} />
      </Routes>
    </MemoryRouter>,
  )
}

describe('useDetailTab', () => {
  it('redirects a retired tab id to its new section, keeping the implied params', () => {
    renderProbe('/v/1?tab=brandstof')
    expect(screen.getByTestId('tab')).toHaveTextContent('kosten')
    expect(screen.getByTestId('search')).toHaveTextContent('?tab=kosten&focus=fuel')
  })

  it('falls back to the default for unknown or forbidden tabs and keeps the default URL clean', async () => {
    renderProbe('/v/1?tab=kpi')
    expect(screen.getByTestId('tab')).toHaveTextContent('overzicht')
    await userEvent.click(screen.getByRole('button', { name: 'historiek' }))
    expect(screen.getByTestId('search')).toHaveTextContent('?tab=historiek')
    await userEvent.click(screen.getByRole('button', { name: 'overzicht' }))
    expect(screen.getByTestId('search')).toBeEmptyDOMElement()
  })
})
