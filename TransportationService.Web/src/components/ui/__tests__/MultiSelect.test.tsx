import { describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { MultiSelect, type MultiSelectOption, type MultiSelectSearchFn } from '../MultiSelect'

const OPTIONS: MultiSelectOption[] = [
  { value: 'tra', label: 'Transport' },
  { value: 'dis', label: 'Dispatch' },
  { value: 'mag', label: 'Magazijn' },
]

function SyncSelect({ initial = [] as string[], onChange }: { initial?: string[]; onChange?: (v: string[]) => void }) {
  const [values, setValues] = useState<string[]>(initial)
  return (
    <MultiSelect
      id="depts"
      label="Afdelingen"
      options={OPTIONS}
      values={values}
      onChange={(next) => {
        setValues(next)
        onChange?.(next)
      }}
      placeholder="Zoeken…"
    />
  )
}

function AsyncSelect({ onSearch }: { onSearch: MultiSelectSearchFn }) {
  const [values, setValues] = useState<string[]>([])
  return <MultiSelect id="emp" label="Medewerkers" onSearch={onSearch} values={values} onChange={setValues} searchDebounceMs={30} />
}

describe('MultiSelect', () => {
  it('filters client-side, toggles several options and keeps the list open', async () => {
    const user = userEvent.setup()
    const onChange = vi.fn()
    render(<SyncSelect onChange={onChange} />)

    const input = screen.getByRole('combobox', { name: 'Afdelingen' })
    await user.click(input)
    expect(screen.getAllByRole('option')).toHaveLength(3)

    await user.click(screen.getByRole('option', { name: 'Transport' }))
    await user.click(screen.getByRole('option', { name: 'Dispatch' }))
    expect(onChange).toHaveBeenLastCalledWith(['tra', 'dis'])
    expect(screen.getByRole('option', { name: 'Transport' })).toHaveAttribute('aria-selected', 'true')
    expect(screen.getByRole('listbox')).toBeVisible()

    await user.type(input, 'mag')
    expect(screen.getAllByRole('option')).toHaveLength(1)
    expect(screen.getByRole('option', { name: 'Magazijn' })).toBeInTheDocument()
  })

  it('supports arrow keys, Enter to toggle, Backspace to drop the last chip and Escape to close', async () => {
    const user = userEvent.setup()
    render(<SyncSelect />)
    const input = screen.getByRole('combobox', { name: 'Afdelingen' })
    await user.click(input)
    await user.keyboard('{ArrowDown}{Enter}')
    expect(screen.getByRole('option', { name: 'Dispatch' })).toHaveAttribute('aria-selected', 'true')
    expect(input).toHaveAttribute('aria-activedescendant', 'depts-opt-1')

    await user.keyboard('{Backspace}')
    expect(screen.getByRole('option', { name: 'Dispatch' })).toHaveAttribute('aria-selected', 'false')

    await user.keyboard('{Escape}')
    expect(input).toHaveAttribute('aria-expanded', 'false')
    expect(screen.queryByRole('listbox')).toBeNull()
  })

  it('renders removable chips for the selection', async () => {
    const user = userEvent.setup()
    render(<SyncSelect initial={['tra', 'mag']} />)
    expect(screen.getByRole('list', { name: 'Geselecteerd: Afdelingen' })).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Transport verwijderen' }))
    expect(screen.queryByRole('button', { name: 'Transport verwijderen' })).toBeNull()
    expect(screen.getByRole('button', { name: 'Magazijn verwijderen' })).toBeInTheDocument()
  })

  it('delegates to the server in async mode, debounces and drops stale responses', async () => {
    const user = userEvent.setup()
    const calls: string[] = []
    let resolveFirst: ((v: MultiSelectOption[]) => void) | null = null
    const onSearch: MultiSelectSearchFn = (query) => {
      calls.push(query)
      if (query === '') {
        return new Promise((resolve) => {
          resolveFirst = resolve
        })
      }
      return Promise.resolve([{ value: 'jan', label: `Jan Peeters (${query})`, description: 'Transport · Chauffeur' }])
    }
    render(<AsyncSelect onSearch={onSearch} />)

    const input = screen.getByRole('combobox', { name: 'Medewerkers' })
    await user.click(input)
    await waitFor(() => expect(calls).toEqual(['']))

    await user.type(input, 'jan')
    await waitFor(() => expect(calls).toContain('jan'))
    expect(await screen.findByRole('option', { name: /Jan Peeters \(jan\)/ })).toBeInTheDocument()
    expect(screen.getByText('Transport · Chauffeur')).toBeInTheDocument()

    // The slow first (empty) search resolves late: it must not overwrite the newer results.
    resolveFirst!([{ value: 'stale', label: 'Stale result' }])
    await new Promise((resolve) => setTimeout(resolve, 20))
    expect(screen.queryByRole('option', { name: 'Stale result' })).toBeNull()
    // Debounced: typing three letters did not fire three searches.
    expect(calls.filter((c) => c !== '').length).toBeLessThan(3)
  })
})
