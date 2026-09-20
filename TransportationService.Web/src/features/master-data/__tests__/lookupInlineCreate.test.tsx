import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { LookupSelect } from '../components/LookupSelect'
import { LookupFormDialog } from '../components/LookupFormDialog'
import { ApiError } from '../../../api/apiClient'
import type { LookupApi } from '../api/lookupApi'
import type { LookupResourceConfig } from '../lookupRegistry'

// Inline create from a lookup select: the "+ Nieuwe …" shortcut is permission-gated, the code
// field may stay empty (backend generates the next free unique code) and the created row is
// refreshed into the list and selected. The admin dialog follows the same code-optional rule.

const auth = vi.hoisted(() => ({ permissions: [] as string[] }))
const lookup = vi.hoisted(() => ({
  refresh: vi.fn(async () => {}),
  create: vi.fn(),
  options: [
    { id: 'jf-1', code: 'CHAUF', name: 'Chauffeur' },
    { id: 'jf-2', code: 'PLAN', name: 'Planner' },
  ],
}))

vi.mock('../../auth/authContextValue', () => ({
  useAuth: () => ({
    status: 'authenticated' as const,
    user: null,
    login: vi.fn(),
    logout: vi.fn(),
    hasPermission: (code: string) => auth.permissions.includes(code),
    hasAnyPermission: (codes: string[]) => codes.some((c) => auth.permissions.includes(c)),
  }),
}))
vi.mock('../hooks/useLookupOptions', () => ({
  useLookupOptions: () => ({ options: lookup.options, isLoading: false, error: null, refresh: lookup.refresh }),
}))
vi.mock('../api/lookupApi', () => ({
  createLookupApi: () => ({ create: lookup.create }),
}))

beforeEach(() => {
  auth.permissions = []
  lookup.refresh.mockClear()
  lookup.create.mockReset()
})

function renderSelect(props: Partial<React.ComponentProps<typeof LookupSelect>> = {}) {
  const onChange = vi.fn()
  render(
    <LookupSelect
      id="jf"
      basePath="/api/job-functions"
      managePermission="job_functions.manage"
      singular="masterData.singular.job-functions"
      value={null}
      onChange={onChange}
      {...props}
    />,
  )
  return { onChange }
}

describe('LookupSelect — "+ Nieuwe …" shortcut', () => {
  it('is a searchable combobox without the shortcut for users without the manage permission', async () => {
    renderSelect()
    const input = screen.getByRole('combobox')
    await userEvent.click(input)
    expect(screen.getAllByRole('option')).toHaveLength(2)
    expect(screen.queryByRole('option', { name: /Nieuwe functie/ })).not.toBeInTheDocument()

    await userEvent.type(input, 'plan')
    expect(screen.getAllByRole('option')).toHaveLength(1)
    expect(screen.getByRole('option', { name: 'Planner' })).toBeInTheDocument()
    // Without the permission there is no "add" row for an unknown query either.
    await userEvent.clear(input)
    await userEvent.type(input, 'Magazijnier')
    expect(screen.queryByRole('option')).not.toBeInTheDocument()
  })

  it('shows the shortcut row (default template) with the manage permission', async () => {
    auth.permissions = ['job_functions.manage']
    renderSelect()
    await userEvent.click(screen.getByRole('combobox'))
    expect(screen.getByRole('option', { name: '+ Nieuwe functie' })).toBeInTheDocument()
  })

  it('uses createLabel verbatim and hides excluded values', async () => {
    auth.permissions = ['job_functions.manage']
    renderSelect({ createLabel: '+ Nieuw contracttype', excludeValues: ['jf-1'] })
    await userEvent.click(screen.getByRole('combobox'))
    expect(screen.queryByRole('option', { name: 'Chauffeur' })).not.toBeInTheDocument()
    expect(screen.getByRole('option', { name: 'Planner' })).toBeInTheDocument()
    expect(screen.getByRole('option', { name: '+ Nieuw contracttype' })).toBeInTheDocument()
  })
})

describe('LookupSelect — inline create dialog', () => {
  it('posts code: null when the code is left empty, refreshes the list and selects the new row', async () => {
    auth.permissions = ['job_functions.manage']
    lookup.create.mockResolvedValue({ id: 'jf-9', code: 'MAGAZIJNIE', name: 'Magazijnier', description: null, isActive: true, sortOrder: 0 })
    const { onChange } = renderSelect()

    await userEvent.click(screen.getByRole('combobox'))
    await userEvent.click(screen.getByRole('option', { name: '+ Nieuwe functie' }))

    expect(screen.getByRole('dialog', { name: 'Nieuwe functie' })).toBeInTheDocument()
    await userEvent.type(screen.getByLabelText(/^Naam/), 'Magazijnier')
    const codeInput = screen.getByLabelText(/^Code/)
    expect(codeInput).toHaveValue('')
    expect(codeInput).toHaveAttribute('placeholder', 'MAGAZIJNIE')
    expect(screen.getByText('Laat dit veld leeg om automatisch de volgende beschikbare unieke code te laten genereren.')).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Toevoegen en selecteren' }))

    await waitFor(() => expect(lookup.create).toHaveBeenCalledTimes(1))
    expect(lookup.create.mock.calls[0][0]).toMatchObject({ code: null, name: 'Magazijnier', description: null, isActive: true })
    await waitFor(() => expect(onChange).toHaveBeenCalledWith('jf-9', { id: 'jf-9', code: 'MAGAZIJNIE', name: 'Magazijnier' }))
    expect(lookup.refresh).toHaveBeenCalledTimes(1)
    // Refresh happened before the selection was emitted.
    expect(lookup.refresh.mock.invocationCallOrder[0]).toBeLessThan(onChange.mock.invocationCallOrder[0])
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  })

  it('sends an explicitly typed code and keeps the 409 duplicate message', async () => {
    auth.permissions = ['job_functions.manage']
    lookup.create.mockRejectedValue(new ApiError('Conflict', 409))
    renderSelect()

    await userEvent.click(screen.getByRole('combobox'))
    await userEvent.click(screen.getByRole('option', { name: '+ Nieuwe functie' }))
    await userEvent.type(screen.getByLabelText(/^Naam/), 'Planner')
    await userEvent.type(screen.getByLabelText(/^Code/), 'PLAN')
    await userEvent.click(screen.getByRole('button', { name: 'Toevoegen en selecteren' }))

    await waitFor(() => expect(lookup.create).toHaveBeenCalledTimes(1))
    expect(lookup.create.mock.calls[0][0]).toMatchObject({ code: 'PLAN', name: 'Planner' })
    expect(await screen.findByRole('alert')).toHaveTextContent("Er bestaat al een functie met code 'PLAN'.")
    expect(screen.getByRole('dialog')).toBeInTheDocument()
  })

  it('does not submit a surrounding host form when the dialog form is submitted', async () => {
    auth.permissions = ['job_functions.manage']
    lookup.create.mockResolvedValue({ id: 'jf-9', code: 'MAG', name: 'Magazijnier', description: null, isActive: true, sortOrder: 0 })
    const hostSubmit = vi.fn((e: React.FormEvent) => e.preventDefault())
    render(
      <form onSubmit={hostSubmit}>
        <LookupSelect basePath="/api/job-functions" managePermission="job_functions.manage" singular="masterData.singular.job-functions" value={null} onChange={vi.fn()} />
      </form>,
    )
    await userEvent.click(screen.getByRole('combobox'))
    await userEvent.click(screen.getByRole('option', { name: '+ Nieuwe functie' }))
    await userEvent.type(screen.getByLabelText(/^Naam/), 'Magazijnier')
    await userEvent.click(screen.getByRole('button', { name: 'Toevoegen en selecteren' }))
    await waitFor(() => expect(lookup.create).toHaveBeenCalledTimes(1))
    expect(hostSubmit).not.toHaveBeenCalled()
  })

  it('cancelling leaves the selection untouched', async () => {
    auth.permissions = ['job_functions.manage']
    const { onChange } = renderSelect()
    await userEvent.click(screen.getByRole('combobox'))
    await userEvent.click(screen.getByRole('option', { name: '+ Nieuwe functie' }))
    await userEvent.click(screen.getByRole('button', { name: 'Annuleren' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(onChange).not.toHaveBeenCalled()
    expect(lookup.create).not.toHaveBeenCalled()
  })
})

const CONFIG: LookupResourceConfig = {
  slug: 'departments',
  title: 'navigation.lookups.departments',
  singular: 'masterData.singular.departments',
  basePath: '/api/departments',
  group: 'organisatie',
  viewPermission: 'departments.view',
  managePermission: 'departments.manage',
}

describe('LookupFormDialog — optional code', () => {
  it('submits code: null when the code is left empty on create', async () => {
    const create = vi.fn().mockResolvedValue({ id: 'd-1', code: 'PLANNING', name: 'Planning', description: null, isActive: true, sortOrder: 0, createdAt: '', updatedAt: '' })
    const api = { create, update: vi.fn() } as unknown as LookupApi
    const onSaved = vi.fn()
    render(<LookupFormDialog config={CONFIG} api={api} onSaved={onSaved} onClose={vi.fn()} />)

    expect(screen.getByText('Laat dit veld leeg om automatisch de volgende beschikbare unieke code te laten genereren.')).toBeInTheDocument()
    await userEvent.type(screen.getByLabelText(/^Naam/), 'Planning')
    await userEvent.click(screen.getByRole('button', { name: 'Opslaan' }))

    await waitFor(() => expect(create).toHaveBeenCalledTimes(1))
    expect(create.mock.calls[0][0]).toMatchObject({ code: null, name: 'Planning' })
    expect(screen.queryByText('Code is verplicht.')).not.toBeInTheDocument()
    await waitFor(() => expect(onSaved).toHaveBeenCalledWith(expect.objectContaining({ code: 'PLANNING' }), true))
  })

  it('still enforces the 50-character maximum on an explicit code', async () => {
    const create = vi.fn()
    const api = { create, update: vi.fn() } as unknown as LookupApi
    render(<LookupFormDialog config={CONFIG} api={api} onSaved={vi.fn()} onClose={vi.fn()} />)

    await userEvent.type(screen.getByLabelText(/^Naam/), 'Planning')
    // fireEvent bypasses the input's maxLength (a pasted/scripted value), like a real browser can.
    fireEvent.change(screen.getByLabelText(/^Code/), { target: { value: 'X'.repeat(51) } })
    await userEvent.click(screen.getByRole('button', { name: 'Opslaan' }))

    expect(screen.getByText('Code mag maximaal 50 tekens bevatten.')).toBeInTheDocument()
    expect(create).not.toHaveBeenCalled()
  })
})
