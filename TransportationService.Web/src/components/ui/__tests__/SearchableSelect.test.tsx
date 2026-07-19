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
})
