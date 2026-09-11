import { describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { SearchableSelect, type SearchableSelectOption, type SearchableSelectSearchFn } from '../SearchableSelect'

interface Deferred<T> {
  promise: Promise<T>
  resolve: (value: T) => void
  reject: (error: unknown) => void
}

function deferred<T>(): Deferred<T> {
  let resolve!: (value: T) => void
  let reject!: (error: unknown) => void
  const promise = new Promise<T>((res, rej) => {
    resolve = res
    reject = rej
  })
  return { promise, resolve, reject }
}

const RICH: SearchableSelectOption[] = [
  {
    value: 'a',
    label: 'Magazijn Antwerpen — Noorderlaan 10, 2030 Antwerpen',
    title: 'Magazijn Antwerpen (MAG-A)',
    subtitle: 'Noorderlaan 10, 2030 Antwerpen',
    meta: 'Klantadres · Van Caudenberg BV',
    badges: ['standaard laden'],
  },
  { value: 'b', label: 'Depot Gent', subtitle: 'Kortrijksesteenweg 1, 9000 Gent', meta: 'Recent gebruikt' },
]

function AsyncSelect(props: {
  onSearch: SearchableSelectSearchFn
  onCreate?: React.ComponentProps<typeof SearchableSelect>['onCreate']
  initialValue?: string | null
  selectedLabel?: string
  debounce?: number
}) {
  const [value, setValue] = useState<string | null>(props.initialValue ?? null)
  return (
    <SearchableSelect
      id="async-select"
      ariaLabel="Adres"
      value={value}
      onChange={setValue}
      options={[]}
      selectedLabel={props.selectedLabel}
      onSearch={props.onSearch}
      searchDebounceMs={props.debounce ?? 50}
      onCreate={props.onCreate}
    />
  )
}

describe('SearchableSelect (async search)', () => {
  it('searches immediately on open and debounces typed queries', async () => {
    const user = userEvent.setup()
    const onSearch = vi.fn<SearchableSelectSearchFn>(async () => RICH)
    render(<AsyncSelect onSearch={onSearch} />)

    const input = screen.getByRole('combobox', { name: 'Adres' })
    await user.click(input)
    await waitFor(() => expect(onSearch).toHaveBeenCalledTimes(1))
    expect(onSearch.mock.calls[0][0]).toBe('')
    expect(onSearch.mock.calls[0][1]).toBeInstanceOf(AbortSignal)
    expect(await screen.findByText('Magazijn Antwerpen (MAG-A)')).toBeInTheDocument()

    await user.type(input, 'ant')
    await waitFor(() => expect(onSearch).toHaveBeenCalledTimes(2))
    expect(onSearch.mock.calls[1][0]).toBe('ant')
    // Typed characters never filter client-side: the server's list is shown as-is.
    expect(screen.getByText('Depot Gent')).toBeInTheDocument()
  })

  it('ignores a stale response and aborts the previous request', async () => {
    const user = userEvent.setup()
    const first = deferred<SearchableSelectOption[]>()
    const second = deferred<SearchableSelectOption[]>()
    const signals: AbortSignal[] = []
    const onSearch = vi.fn<SearchableSelectSearchFn>((_query, signal) => {
      signals.push(signal)
      return signals.length === 1 ? first.promise : second.promise
    })
    render(<AsyncSelect onSearch={onSearch} />)

    const input = screen.getByRole('combobox', { name: 'Adres' })
    await user.click(input)
    await waitFor(() => expect(onSearch).toHaveBeenCalledTimes(1))
    expect(screen.getByText('Laden…')).toBeInTheDocument()

    await user.type(input, 'g')
    await waitFor(() => expect(onSearch).toHaveBeenCalledTimes(2))
    expect(signals[0].aborted).toBe(true)
    expect(signals[1].aborted).toBe(false)

    second.resolve([{ value: 'b', label: 'Depot Gent' }])
    expect(await screen.findByRole('option', { name: 'Depot Gent' })).toBeInTheDocument()

    // Request 1 finishes late: its results must never replace request 2's.
    first.resolve([{ value: 'a', label: 'Magazijn Antwerpen' }])
    await new Promise((r) => setTimeout(r, 20))
    expect(screen.getByRole('option', { name: 'Depot Gent' })).toBeInTheDocument()
    expect(screen.queryByText('Magazijn Antwerpen')).not.toBeInTheDocument()
    expect(screen.getByRole('status')).toHaveTextContent('1 resultaten')
  })

  it('shows an error note when the search rejects and the empty message on no results', async () => {
    const user = userEvent.setup()
    const onSearch = vi.fn<SearchableSelectSearchFn>(async (query) => {
      if (query === 'boom') throw new Error('network')
      return []
    })
    render(<AsyncSelect onSearch={onSearch} />)

    const input = screen.getByRole('combobox', { name: 'Adres' })
    await user.click(input)
    expect(await screen.findByText('Geen resultaten')).toBeInTheDocument()
    expect(screen.getByRole('status')).toHaveTextContent('0 resultaten')

    await user.type(input, 'boom')
    expect(await screen.findByText('Zoeken mislukt.')).toBeInTheDocument()
  })

  it('renders rich options with subtitle, meta and badges and commits the label', async () => {
    const user = userEvent.setup()
    render(<AsyncSelect onSearch={async () => RICH} />)

    const input = screen.getByRole('combobox', { name: 'Adres' })
    await user.click(input)
    const title = await screen.findByText('Magazijn Antwerpen (MAG-A)')
    expect(title).toHaveClass('ui-searchable-select-option-label')
    expect(screen.getByText('Noorderlaan 10, 2030 Antwerpen')).toHaveClass('ui-searchable-select-option-subtitle')
    expect(screen.getByText('Klantadres · Van Caudenberg BV')).toHaveClass('ui-searchable-select-option-meta')
    expect(screen.getByText('standaard laden')).toHaveClass('ui-searchable-select-badge')

    await user.click(title)
    expect((input as HTMLInputElement).value).toBe('Magazijn Antwerpen — Noorderlaan 10, 2030 Antwerpen')
    // Reopening resets the list; the selected label must survive via the remembered option.
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument()
  })

  it('shows selectedLabel for a value that no option resolves yet', () => {
    render(<AsyncSelect onSearch={async () => []} initialValue="loc-9" selectedLabel="Kade 7 — Havenlaan 1, 2000 Antwerpen" />)
    expect((screen.getByRole('combobox', { name: 'Adres' }) as HTMLInputElement).value).toBe(
      'Kade 7 — Havenlaan 1, 2000 Antwerpen',
    )
  })

  it('offers the create row in async mode and auto-selects the created option', async () => {
    const user = userEvent.setup()
    const create = vi.fn(async (query: string) => ({ value: 'new', label: `${query} — Nieuwstraat 1, 1000 Brussel` }))
    render(<AsyncSelect onSearch={async () => []} onCreate={{ label: (q) => `Nieuw adres “${q}” aanmaken…`, create }} />)

    const input = screen.getByRole('combobox', { name: 'Adres' })
    await user.type(input, 'Kade 9')
    const row = await screen.findByRole('option', { name: '+ Nieuw adres “Kade 9” aanmaken…' })
    await user.click(row)

    await waitFor(() => expect(create).toHaveBeenCalledWith('Kade 9'))
    expect((input as HTMLInputElement).value).toBe('Kade 9 — Nieuwstraat 1, 1000 Brussel')
  })
})

describe('SearchableSelect (Home/End)', () => {
  it('jumps to the first and last row', async () => {
    const user = userEvent.setup()
    const options: SearchableSelectOption[] = [
      { value: 'be', label: 'België' },
      { value: 'nl', label: 'Nederland' },
      { value: 'de', label: 'Duitsland' },
    ]
    render(<SearchableSelect id="sync" ariaLabel="Land" value={null} onChange={() => {}} options={options} />)

    const input = screen.getByRole('combobox', { name: 'Land' })
    await user.click(input)
    expect(input).toHaveAttribute('aria-activedescendant', 'sync-opt-0')
    await user.keyboard('{End}')
    expect(input).toHaveAttribute('aria-activedescendant', 'sync-opt-2')
    expect(screen.getByRole('option', { name: 'Duitsland' })).toHaveClass('is-highlighted')
    await user.keyboard('{Home}')
    expect(input).toHaveAttribute('aria-activedescendant', 'sync-opt-0')
    expect(screen.getByRole('option', { name: 'België' })).toHaveClass('is-highlighted')
  })
})
