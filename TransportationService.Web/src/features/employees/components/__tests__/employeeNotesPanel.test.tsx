import { describe, expect, it, vi, beforeEach } from 'vitest'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { EmployeeNotesPanel } from '../EmployeeNotesPanel'
import type { EmployeeNote } from '../../api/employeeNotesApi'

const auth = vi.hoisted(() => ({ permissions: new Set<string>(['employee_notes.view', 'employee_notes.manage', 'employee_notes.pin']) }))
const state = vi.hoisted(() => ({ notes: [] as EmployeeNote[] }))
const spies = vi.hoisted(() => ({
  createEmployeeNote: vi.fn(),
  updateEmployeeNote: vi.fn(),
  deleteEmployeeNote: vi.fn(),
  pinEmployeeNote: vi.fn(),
  unpinEmployeeNote: vi.fn(),
}))

vi.mock('../../../auth/authContextValue', () => ({
  useAuth: () => ({ hasPermission: (code: string) => auth.permissions.has(code) }),
}))
vi.mock('../../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }),
}))
vi.mock('../../api/employeeNotesApi', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../api/employeeNotesApi')>()),
  listEmployeeNotes: () => Promise.resolve(state.notes),
  createEmployeeNote: (...args: unknown[]) => {
    spies.createEmployeeNote(...args)
    return Promise.resolve(note({ id: 'note-new', text: (args[1] as string) ?? '' }))
  },
  updateEmployeeNote: (...args: unknown[]) => {
    spies.updateEmployeeNote(...args)
    return Promise.resolve(note({ id: args[1] as string, text: args[2] as string }))
  },
  deleteEmployeeNote: (...args: unknown[]) => {
    spies.deleteEmployeeNote(...args)
    return Promise.resolve()
  },
  pinEmployeeNote: (...args: unknown[]) => {
    spies.pinEmployeeNote(...args)
    const noteId = args[1] as string
    state.notes = state.notes.map((n) => (n.id === noteId ? { ...n, isPinnedToDashboard: true } : n))
    return Promise.resolve(state.notes.find((n) => n.id === noteId)!)
  },
  unpinEmployeeNote: (...args: unknown[]) => {
    spies.unpinEmployeeNote(...args)
    const noteId = args[1] as string
    state.notes = state.notes.map((n) => (n.id === noteId ? { ...n, isPinnedToDashboard: false } : n))
    return Promise.resolve(state.notes.find((n) => n.id === noteId)!)
  },
}))

function note(overrides: Partial<EmployeeNote> = {}): EmployeeNote {
  return {
    id: 'note-1',
    employeeId: 'emp-1',
    text: 'Notitietekst',
    isPinnedToDashboard: false,
    createdAt: '2026-07-28T10:00:00Z',
    createdByUserId: null,
    updatedAt: '2026-07-28T10:00:00Z',
    updatedByUserId: null,
    ...overrides,
  }
}

describe('EmployeeNotesPanel', () => {
  beforeEach(() => {
    auth.permissions = new Set(['employee_notes.view', 'employee_notes.manage', 'employee_notes.pin'])
    state.notes = []
    Object.values(spies).forEach((spy) => spy.mockClear())
  })

  it('renders multiple notes as cards, newest first', async () => {
    state.notes = [
      note({ id: 'older', text: 'Oudere notitie', createdAt: '2026-07-01T10:00:00Z' }),
      note({ id: 'newer', text: 'Nieuwere notitie', createdAt: '2026-07-20T10:00:00Z' }),
    ]
    render(<EmployeeNotesPanel employeeId="emp-1" />)

    const cards = await screen.findAllByText(/notitie$/i)
    expect(cards[0]).toHaveTextContent('Nieuwere notitie')
    expect(cards[1]).toHaveTextContent('Oudere notitie')
  })

  it('shows a pin badge only for pinned notes', async () => {
    state.notes = [note({ id: 'pinned', text: 'Vast', isPinnedToDashboard: true })]
    render(<EmployeeNotesPanel employeeId="emp-1" />)

    expect(await screen.findByText('Op startscherm')).toBeInTheDocument()
  })

  it('delete shows a ConfirmDialog first and only deletes after confirming', async () => {
    const user = userEvent.setup()
    state.notes = [note()]
    render(<EmployeeNotesPanel employeeId="emp-1" />)

    await screen.findByText('Notitietekst')
    await user.click(screen.getByRole('button', { name: 'Verwijderen' }))

    const dialog = await screen.findByRole('dialog', { name: 'Notitie verwijderen' })
    expect(spies.deleteEmployeeNote).not.toHaveBeenCalled()

    await user.click(within(dialog).getByRole('button', { name: 'Verwijderen' }))
    expect(spies.deleteEmployeeNote).toHaveBeenCalledWith('emp-1', 'note-1')
  })

  it('pin toggle calls the endpoint and flips the badge', async () => {
    const user = userEvent.setup()
    state.notes = [note()]
    render(<EmployeeNotesPanel employeeId="emp-1" />)

    await screen.findByText('Notitietekst')
    expect(screen.queryByText('Op startscherm')).not.toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Toevoegen aan melding startscherm' }))
    expect(spies.pinEmployeeNote).toHaveBeenCalledWith('emp-1', 'note-1')
    expect(await screen.findByText('Op startscherm')).toBeInTheDocument()
    expect(await screen.findByRole('button', { name: 'Verwijderen van startscherm' })).toBeInTheDocument()
  })

  it('adds a note through the add-note form', async () => {
    const user = userEvent.setup()
    render(<EmployeeNotesPanel employeeId="emp-1" />)

    await screen.findByText('Nog geen notities voor deze medewerker.')
    await user.type(screen.getByLabelText('Nieuwe notitie'), 'Nieuwe tekst')
    await user.click(screen.getByRole('button', { name: 'Opslaan' }))

    expect(spies.createEmployeeNote).toHaveBeenCalledWith('emp-1', 'Nieuwe tekst')
  })

  it('hides Bewerken/Verwijderen without employee_notes.manage, and the pin toggle without employee_notes.pin', async () => {
    auth.permissions = new Set(['employee_notes.view'])
    state.notes = [note()]
    render(<EmployeeNotesPanel employeeId="emp-1" />)

    await screen.findByText('Notitietekst')
    expect(screen.queryByRole('button', { name: 'Bewerken' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Verwijderen' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Toevoegen aan melding startscherm' })).not.toBeInTheDocument()
    // Read-only: no add-note form either.
    expect(screen.queryByLabelText('Nieuwe notitie')).not.toBeInTheDocument()
  })

  it('renders nothing at all without employee_notes.view', () => {
    auth.permissions = new Set()
    state.notes = [note()]
    const { container } = render(<EmployeeNotesPanel employeeId="emp-1" />)
    expect(container).toBeEmptyDOMElement()
  })
})
