import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { Truck } from 'lucide-react'
import { NavModule } from '../NavModule'
import type { VisibleModule } from '../navState'

const vm: VisibleModule = {
  module: { id: 'vloot', label: 'Vloot', icon: Truck, items: [] },
  items: [
    { label: 'Voertuigen', to: '/vehicles' },
    { label: 'Meldingen', to: '/notifications', badge: 'notifications' },
  ],
  subgroups: [],
}

function renderModule(props: Partial<Parameters<typeof NavModule>[0]> = {}) {
  return render(
    <MemoryRouter initialEntries={['/vehicles']}>
      <NavModule vm={vm} expanded active={false} unreadCount={0} onToggle={vi.fn()} {...props} />
    </MemoryRouter>,
  )
}

describe('NavModule', () => {
  it('renders an accessible header reflecting expanded state', () => {
    renderModule({ expanded: false })
    const header = screen.getByRole('button', { name: /Vloot/ })
    expect(header).toHaveAttribute('aria-expanded', 'false')
  })

  it('toggles via the header button', async () => {
    const onToggle = vi.fn()
    renderModule({ onToggle })
    await userEvent.click(screen.getByRole('button', { name: /Vloot/ }))
    expect(onToggle).toHaveBeenCalledWith('vloot')
  })

  it('renders item links when expanded', () => {
    renderModule({ expanded: true })
    expect(screen.getByRole('link', { name: 'Voertuigen' })).toHaveAttribute('href', '/vehicles')
  })

  it('shows the unread badge on a badged item', () => {
    renderModule({ expanded: true, unreadCount: 5 })
    expect(screen.getByText('5')).toBeInTheDocument()
  })

  it('marks the header active when the active prop is set', () => {
    const { container } = renderModule({ active: true })
    expect(container.querySelector('.nav-module-active')).not.toBeNull()
  })

  it('renders a nested submenu for an item with children (future-proofing)', async () => {
    const nested: VisibleModule = {
      module: { id: 'x', label: 'X', icon: Truck, items: [] },
      items: [{ label: 'Ouder', to: '/parent', children: [{ label: 'Kind', to: '/parent/child' }] }],
      subgroups: [],
    }
    render(
      <MemoryRouter>
        <NavModule vm={nested} expanded active={false} unreadCount={0} onToggle={vi.fn()} />
      </MemoryRouter>,
    )
    const parentToggle = screen.getByRole('button', { name: /Ouder/ })
    expect(screen.queryByRole('link', { name: 'Kind' })).toBeNull()
    await userEvent.click(parentToggle)
    expect(screen.getByRole('link', { name: 'Kind' })).toHaveAttribute('href', '/parent/child')
  })
})
