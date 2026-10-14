import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ActionMenu } from '../ActionMenu'

describe('ActionMenu (B03/B04)', () => {
  it('renders nothing without items and a closed "Meer" trigger with them', () => {
    const { container, rerender } = render(<ActionMenu items={[]} />)
    expect(container).toBeEmptyDOMElement()
    rerender(<ActionMenu items={[{ key: 'a', label: 'Dupliceren', onSelect: vi.fn() }]} />)
    const trigger = screen.getByRole('button', { name: 'Meer' })
    expect(trigger).toHaveAttribute('aria-haspopup', 'menu')
    expect(trigger).toHaveAttribute('aria-expanded', 'false')
    expect(screen.queryByRole('menu')).not.toBeInTheDocument()
  })

  it('groups destructive items last behind a separator, selects and closes', async () => {
    const duplicate = vi.fn()
    const remove = vi.fn()
    render(
      <ActionMenu
        items={[
          { key: 'delete', label: 'Verwijderen', onSelect: remove, danger: true },
          { key: 'duplicate', label: 'Dupliceren', onSelect: duplicate },
        ]}
      />,
    )
    await userEvent.click(screen.getByRole('button', { name: 'Meer' }))
    const items = screen.getAllByRole('menuitem')
    expect(items.map((item) => item.textContent)).toEqual(['Dupliceren', 'Verwijderen'])
    expect(items[1]).toHaveClass('ui-action-menu-item-danger')
    expect(screen.getByRole('separator')).toBeInTheDocument()

    await userEvent.click(screen.getByRole('menuitem', { name: 'Verwijderen' }))
    expect(remove).toHaveBeenCalledTimes(1)
    expect(duplicate).not.toHaveBeenCalled()
    expect(screen.queryByRole('menu')).not.toBeInTheDocument()
  })

  it('closes on Escape and on an outside click', async () => {
    render(
      <div>
        <button type="button">Elders</button>
        <ActionMenu items={[{ key: 'a', label: 'Dupliceren', onSelect: vi.fn() }]} />
      </div>,
    )
    await userEvent.click(screen.getByRole('button', { name: 'Meer' }))
    expect(screen.getByRole('menu')).toBeInTheDocument()
    await userEvent.keyboard('{Escape}')
    expect(screen.queryByRole('menu')).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Meer' })).toHaveFocus()

    await userEvent.click(screen.getByRole('button', { name: 'Meer' }))
    await userEvent.click(screen.getByRole('button', { name: 'Elders' }))
    expect(screen.queryByRole('menu')).not.toBeInTheDocument()
  })
})
