import { describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { SearchableSelect, type SearchableSelectOption } from '../SearchableSelect'

const OPTIONS: SearchableSelectOption[] = [
  { value: 'be', label: 'België', keywords: 'BE BEL' },
  { value: 'nl', label: 'Nederland', keywords: 'NL NLD' },
  { value: 'de', label: 'Duitsland', keywords: 'DE DEU' },
]

function ControlledSelect(props: {
  onCreate?: React.ComponentProps<typeof SearchableSelect>['onCreate']
  options?: SearchableSelectOption[]
}) {
  const [value, setValue] = useState<string | null>(null)
  return (
    <SearchableSelect
      id="test-select"
      ariaLabel="Land"
      value={value}
      onChange={setValue}
      options={props.options ?? OPTIONS}
      onCreate={props.onCreate}
    />
  )
}

describe('SearchableSelect', () => {
  it('filters options on label and keywords', async () => {
    const user = userEvent.setup()
    render(<ControlledSelect />)

    const input = screen.getByRole('combobox', { name: 'Land' })
    await user.click(input)
    expect(screen.getAllByRole('option')).toHaveLength(3)

    await user.type(input, 'neder')
    expect(screen.getAllByRole('option')).toHaveLength(1)
    expect(screen.getByRole('option', { name: 'Nederland' })).toBeInTheDocument()

    await user.clear(input)
    await user.type(input, 'DEU')
    expect(screen.getByRole('option', { name: 'Duitsland' })).toBeInTheDocument()
  })

  it('selects with keyboard navigation and shows the selected label', async () => {
    const user = userEvent.setup()
    render(<ControlledSelect />)

    const input = screen.getByRole('combobox', { name: 'Land' })
    await user.click(input)
    await user.keyboard('{ArrowDown}{Enter}')

    expect(screen.queryByRole('listbox')).not.toBeInTheDocument()
    expect((input as HTMLInputElement).value).toBe('Nederland')
  })

  it('clears the selection via the clear button', async () => {
    const user = userEvent.setup()
    render(<ControlledSelect />)

    const input = screen.getByRole('combobox', { name: 'Land' })
    await user.click(input)
    await user.click(screen.getByRole('option', { name: 'België' }))
    expect((input as HTMLInputElement).value).toBe('België')

    await user.click(screen.getByRole('button', { name: 'Selectie wissen' }))
    expect((input as HTMLInputElement).value).toBe('')
  })

  it('shows an empty state when nothing matches', async () => {
    const user = userEvent.setup()
    render(<ControlledSelect />)

    const input = screen.getByRole('combobox', { name: 'Land' })
    await user.type(input, 'xyz')
    expect(screen.getByText('Geen resultaten')).toBeInTheDocument()
  })

  it('creates and auto-selects a new option through the create row', async () => {
    const user = userEvent.setup()
    const create = vi.fn(async (query: string) => ({ value: 'fr', label: `Frankrijk (${query})` }))
    render(<ControlledSelect onCreate={{ label: (q) => `"${q}" toevoegen`, create }} />)

    const input = screen.getByRole('combobox', { name: 'Land' })
    await user.type(input, 'Frank')
    await user.click(screen.getByRole('option', { name: '+ "Frank" toevoegen' }))

    await waitFor(() => expect(create).toHaveBeenCalledWith('Frank'))
    expect((input as HTMLInputElement).value).toBe('Frankrijk (Frank)')
  })

  it('hides the create row while the query is empty unless alwaysShow is set', async () => {
    const user = userEvent.setup()
    const create = vi.fn(async () => null)
    render(<ControlledSelect onCreate={{ label: (q) => `"${q}" toevoegen`, create }} />)

    await user.click(screen.getByRole('combobox', { name: 'Land' }))
    expect(screen.getAllByRole('option')).toHaveLength(3)
    expect(screen.queryByRole('option', { name: /toevoegen/ })).not.toBeInTheDocument()
  })

  it('always renders the shortcut row with alwaysShow and reaches it with the keyboard', async () => {
    const user = userEvent.setup()
    const create = vi.fn(async (query: string) => ({ value: 'fr', label: query ? `Frankrijk (${query})` : 'Frankrijk' }))
    render(
      <ControlledSelect
        onCreate={{ label: (q) => `"${q}" toevoegen`, create, alwaysShow: true, emptyQueryLabel: '+ Nieuw land' }}
      />,
    )

    const input = screen.getByRole('combobox', { name: 'Land' })
    await user.click(input)
    // Three options + the permanent shortcut row at the bottom.
    expect(screen.getAllByRole('option')).toHaveLength(4)
    const shortcut = screen.getByRole('option', { name: '+ Nieuw land' })
    expect(screen.getAllByRole('option').at(-1)).toBe(shortcut)

    // With a query the usual query-driven label takes over.
    await user.type(input, 'Frank')
    expect(screen.getByRole('option', { name: '+ "Frank" toevoegen' })).toBeInTheDocument()
    expect(screen.queryByRole('option', { name: '+ Nieuw land' })).not.toBeInTheDocument()
    await user.clear(input)

    // ArrowDown ×3 from the first option lands on the shortcut row; Enter triggers it.
    await user.keyboard('{ArrowDown}{ArrowDown}{ArrowDown}')
    expect(input).toHaveAttribute('aria-activedescendant', shortcut.id)
    await user.keyboard('{Enter}')
    await waitFor(() => expect(create).toHaveBeenCalledWith(''))
    expect((input as HTMLInputElement).value).toBe('Frankrijk')
  })

  it('shows the empty state together with the alwaysShow shortcut when there are no options', async () => {
    const user = userEvent.setup()
    render(
      <ControlledSelect
        options={[]}
        onCreate={{ label: (q) => `"${q}" toevoegen`, create: async () => null, alwaysShow: true, emptyQueryLabel: '+ Nieuw land' }}
      />,
    )
    await user.click(screen.getByRole('combobox', { name: 'Land' }))
    expect(screen.getByText('Geen resultaten')).toBeInTheDocument()
    expect(screen.getByRole('option', { name: '+ Nieuw land' })).toBeInTheDocument()
  })

  // Sprint 2026-09-21 — one end-adornment zone instead of two loose absolutely positioned glyphs.
  describe('end-adornment zone', () => {
    it('keeps caret and clear button together in one zone and flags the wider zone on the root', async () => {
      const user = userEvent.setup()
      const { container } = render(<ControlledSelect />)
      const root = container.querySelector('.ui-searchable-select')!
      const zone = container.querySelector('.ui-searchable-select-adornments')!
      const input = screen.getByRole('combobox', { name: 'Land' })

      expect(input).toHaveClass('ui-searchable-select-input')
      expect(zone.parentElement).toBe(input.parentElement)
      expect(zone.querySelector('.ui-searchable-select-caret')).toHaveAttribute('aria-hidden', 'true')
      expect(zone.querySelector('button')).toBeNull()
      expect(root).not.toHaveClass('has-clear')

      await user.click(input)
      await user.click(screen.getByRole('option', { name: 'België' }))

      const clear = screen.getByRole('button', { name: 'Selectie wissen' })
      expect(zone).toContainElement(clear)
      // Clear sits left of the caret; the caret stays the outermost adornment.
      expect(zone.lastElementChild).toHaveClass('ui-searchable-select-caret')
      expect(root).toHaveClass('has-clear')

      await user.click(clear)
      expect(root).not.toHaveClass('has-clear')
      expect(input).toHaveFocus()
    })

    it('renders no clear button (and no wide zone) when disabled or not clearable', () => {
      const { container, rerender } = render(
        <SearchableSelect ariaLabel="Land" value="be" onChange={() => {}} options={OPTIONS} disabled />,
      )
      expect(container.querySelector('.ui-searchable-select')).not.toHaveClass('has-clear')
      rerender(<SearchableSelect ariaLabel="Land" value="be" onChange={() => {}} options={OPTIONS} clearable={false} />)
      expect(screen.queryByRole('button', { name: 'Selectie wissen' })).not.toBeInTheDocument()
      expect(container.querySelector('.ui-searchable-select')).not.toHaveClass('has-clear')
    })
  })

  it('matches every word of the query across label and keywords, in any order', async () => {
    const user = userEvent.setup()
    render(
      <ControlledSelect
        options={[
          { value: '1', label: 'Jan Peeters (CH-001)', keywords: 'P-4711' },
          { value: '2', label: 'Jan Maes (CH-002)', keywords: 'P-0815' },
        ]}
      />,
    )
    const input = screen.getByRole('combobox', { name: 'Land' })
    await user.type(input, 'peeters jan')
    expect(screen.getAllByRole('option')).toHaveLength(1)
    await user.clear(input)
    await user.type(input, 'jan 0815')
    expect(screen.getByRole('option', { name: /Jan Maes/ })).toBeInTheDocument()
    expect(screen.getAllByRole('option')).toHaveLength(1)
  })

  it('shows a caller-supplied load error instead of the empty state', async () => {
    const user = userEvent.setup()
    render(
      <SearchableSelect ariaLabel="Land" value={null} onChange={() => {}} options={[]} errorMessage="Landen laden mislukt" />,
    )
    await user.click(screen.getByRole('combobox', { name: 'Land' }))
    expect(screen.getByText('Landen laden mislukt')).toBeInTheDocument()
    expect(screen.queryByText('Geen resultaten')).not.toBeInTheDocument()
  })
})
