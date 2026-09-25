import { beforeEach, describe, expect, it, vi } from 'vitest'
import { act, render, renderHook, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { ApiError } from '../../../api/apiClient'
import type { DossierNote } from '../api/dossierNotesApi'
import { DossierHistorySection } from '../components/sections/DossierHistorySection'
import { DossierWorkspaceContext, type DossierWorkspace } from '../dossierWorkspace'
import { DossierNotesPanel } from '../notes/DossierNotesPanel'
import { NotePreview } from '../notes/NotePreview'
import { useDossierNotes } from '../notes/useDossierNotes'
import { dossierDetail } from './fixtures'

const auth = vi.hoisted(() => ({ permissions: new Set<string>() }))
vi.mock('../../auth/authContextValue', () => ({
  useAuth: () => ({ hasPermission: (code: string) => auth.permissions.has(code) }),
}))
const toast = vi.hoisted(() => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }))
vi.mock('../../../components/ui/toastContext', () => ({ useToast: () => toast }))

const api = vi.hoisted(() => ({
  listDossierNotes: vi.fn(),
  createDossierNote: vi.fn(),
  updateDossierNote: vi.fn(),
  deleteDossierNote: vi.fn(),
}))
vi.mock('../api/dossierNotesApi', async () => {
  const actual = await vi.importActual<typeof import('../api/dossierNotesApi')>('../api/dossierNotesApi')
  return { ...actual, ...api }
})

// Only the audit panel talks to apiClient directly here (the notes api is mocked above).
const getJson = vi.hoisted(() => vi.fn())
vi.mock('../../../api/apiClient', async () => {
  const actual = await vi.importActual<typeof import('../../../api/apiClient')>('../../../api/apiClient')
  return { ...actual, apiClient: { ...actual.apiClient, getJson } }
})

const LONG_TEXT = 'Chauffeur moet zich aanmelden bij poort 3 en daarna de veiligheidsinstructie volgen. '.repeat(4).trim()

function note(overrides: Partial<DossierNote> = {}): DossierNote {
  return {
    id: 'n-1',
    dossierId: 'd-1',
    dossierActivityId: null,
    text: 'Klant belt terug over het losuur.',
    authorName: 'Sofie Peeters',
    createdAt: '2026-09-20T08:15:00Z',
    updatedAt: '2026-09-20T08:15:00Z',
    canEdit: true,
    canDelete: true,
    ...overrides,
  }
}

/** n notes, newest first, texts "Notitie 1" (newest) … "Notitie n". */
function notes(count: number): DossierNote[] {
  return Array.from({ length: count }, (_, index) =>
    note({ id: `n-${index + 1}`, text: `Notitie ${index + 1}`, createdAt: `2026-09-${String(20 - index).padStart(2, '0')}T08:00:00Z`, updatedAt: `2026-09-${String(20 - index).padStart(2, '0')}T08:00:00Z` }),
  )
}

function noteItems() {
  return screen.queryAllByRole('listitem')
}

const ADD = '+ Notitie toevoegen'

describe('DossierNotesPanel', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    auth.permissions = new Set()
    api.listDossierNotes.mockResolvedValue([])
  })

  it('shows "Geen notities." together with the add button in the empty state', async () => {
    render(<DossierNotesPanel dossierId="d-1" canWrite />)

    expect(await screen.findByText('Geen notities.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: ADD })).toBeInTheDocument()
    expect(api.listDossierNotes).toHaveBeenCalledWith('d-1', null, expect.anything())
  })

  it('adds the first note and shows it immediately, without reloading the list', async () => {
    const user = userEvent.setup()
    api.createDossierNote.mockResolvedValue(note({ id: 'n-new', text: 'Eerste notitie' }))
    render(<DossierNotesPanel dossierId="d-1" canWrite />)
    await screen.findByText('Geen notities.')

    await user.click(screen.getByRole('button', { name: ADD }))
    const field = screen.getByLabelText('Nieuwe notitie')
    expect(field).toHaveFocus()
    expect(screen.getByRole('button', { name: 'Opslaan' })).toBeDisabled()
    await user.type(field, '  Eerste notitie  ')
    await user.click(screen.getByRole('button', { name: 'Opslaan' }))

    expect(await screen.findByText('Eerste notitie')).toBeInTheDocument()
    expect(api.createDossierNote).toHaveBeenCalledWith('d-1', 'Eerste notitie', null)
    expect(api.listDossierNotes).toHaveBeenCalledTimes(1)
    expect(screen.queryByText('Geen notities.')).not.toBeInTheDocument()
    expect(screen.queryByLabelText('Nieuwe notitie')).not.toBeInTheDocument()
    expect(within(noteItems()[0]).getByText('Sofie Peeters')).toBeInTheDocument()
  })

  it('puts a new note at the top and saves with Ctrl+Enter; saving disables the editor', async () => {
    const user = userEvent.setup()
    api.listDossierNotes.mockResolvedValue(notes(2))
    let resolveCreate: (value: DossierNote) => void = () => {}
    api.createDossierNote.mockReturnValue(new Promise<DossierNote>((resolve) => (resolveCreate = resolve)))
    render(<DossierNotesPanel dossierId="d-1" canWrite />)
    await screen.findByText('Notitie 1')

    await user.click(screen.getByRole('button', { name: ADD }))
    await user.type(screen.getByLabelText('Nieuwe notitie'), 'Nieuwste')
    await user.keyboard('{Control>}{Enter}{/Control}')

    expect(api.createDossierNote).toHaveBeenCalledTimes(1)
    expect(screen.getByLabelText('Nieuwe notitie')).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Bezig...' })).toBeDisabled()

    await act(async () => resolveCreate(note({ id: 'n-new', text: 'Nieuwste', createdAt: '2026-09-21T10:00:00Z', updatedAt: '2026-09-21T10:00:00Z' })))
    await waitFor(() => expect(noteItems()).toHaveLength(3))
    expect(within(noteItems()[0]).getByText('Nieuwste')).toBeInTheDocument()
  })

  it('shows 3 of 5 notes, expands with "Alle notities tonen (5)" and collapses with "Minder tonen"', async () => {
    const user = userEvent.setup()
    api.listDossierNotes.mockResolvedValue(notes(5))
    render(<DossierNotesPanel dossierId="d-1" canWrite />)
    await screen.findByText('Notitie 1')

    expect(noteItems()).toHaveLength(3)
    expect(screen.queryByText('Notitie 4')).not.toBeInTheDocument()
    const toggle = screen.getByRole('button', { name: 'Alle notities tonen (5)' })
    expect(toggle).toHaveAttribute('aria-expanded', 'false')
    expect(document.getElementById(toggle.getAttribute('aria-controls')!)).toBe(screen.getByRole('list'))

    await user.click(toggle)
    expect(noteItems()).toHaveLength(5)
    const collapse = screen.getByRole('button', { name: 'Minder tonen' })
    expect(collapse).toHaveAttribute('aria-expanded', 'true')

    await user.click(collapse)
    expect(noteItems()).toHaveLength(3)
    expect(screen.getByRole('button', { name: 'Alle notities tonen (5)' })).toBeInTheDocument()
  })

  it('offers no list toggle for 3 notes or fewer', async () => {
    api.listDossierNotes.mockResolvedValue(notes(3))
    render(<DossierNotesPanel dossierId="d-1" canWrite />)
    await screen.findByText('Notitie 1')

    expect(noteItems()).toHaveLength(3)
    expect(screen.queryByRole('button', { name: /Alle notities tonen/ })).not.toBeInTheDocument()
  })

  it('clamps a long note to two lines and expands it with "Meer tonen"', async () => {
    const user = userEvent.setup()
    api.listDossierNotes.mockResolvedValue([note({ id: 'n-long', text: LONG_TEXT }), note({ id: 'n-short', text: 'Kort.' })])
    render(<DossierNotesPanel dossierId="d-1" canWrite={false} />)

    const text = await screen.findByText(LONG_TEXT)
    expect(text).toHaveClass('dossier-note-text-clamped')
    // Only the long note gets the disclosure.
    const more = screen.getByRole('button', { name: 'Meer tonen' })
    expect(more).toHaveAttribute('aria-expanded', 'false')
    expect(more).toHaveAttribute('aria-controls', text.id)

    await user.click(more)
    expect(text).not.toHaveClass('dossier-note-text-clamped')
    const less = screen.getByRole('button', { name: 'Minder tonen' })
    expect(less).toHaveAttribute('aria-expanded', 'true')

    await user.click(less)
    expect(text).toHaveClass('dossier-note-text-clamped')
  })

  it('keeps the typed text and shows the server error when saving fails', async () => {
    const user = userEvent.setup()
    api.createDossierNote.mockRejectedValue(new ApiError('Dit dossier is vergrendeld.', 409))
    render(<DossierNotesPanel dossierId="d-1" canWrite />)
    await screen.findByText('Geen notities.')

    await user.click(screen.getByRole('button', { name: ADD }))
    await user.type(screen.getByLabelText('Nieuwe notitie'), 'Belangrijke afspraak')
    await user.click(screen.getByRole('button', { name: 'Opslaan' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Dit dossier is vergrendeld.')
    expect(screen.getByLabelText('Nieuwe notitie')).toHaveValue('Belangrijke afspraak')
    expect(screen.getByLabelText('Nieuwe notitie')).toBeEnabled()
    expect(noteItems()).toHaveLength(0)
    expect(toast.showSuccess).not.toHaveBeenCalled()
  })

  it('shows a character counter near the 4000 limit', async () => {
    const user = userEvent.setup()
    render(<DossierNotesPanel dossierId="d-1" canWrite />)
    await screen.findByText('Geen notities.')

    await user.click(screen.getByRole('button', { name: ADD }))
    const field = screen.getByLabelText('Nieuwe notitie')
    expect(field).toHaveAttribute('maxlength', '4000')
    expect(field).toBeRequired()
    expect(screen.queryByText(/\/ 4000 tekens/)).not.toBeInTheDocument()

    await user.click(field)
    await user.paste('x'.repeat(3950))
    expect(screen.getByText('3950 / 4000 tekens')).toBeInTheDocument()
  })

  it('offers edit/delete only where the DTO allows it, and updates the list in place', async () => {
    const user = userEvent.setup()
    api.listDossierNotes.mockResolvedValue([
      note({ id: 'n-mine', text: 'Mijn notitie' }),
      note({ id: 'n-other', text: 'Notitie van collega', authorName: 'Tom Janssens', canEdit: false, canDelete: false }),
    ])
    api.updateDossierNote.mockResolvedValue(note({ id: 'n-mine', text: 'Mijn notitie (aangepast)', updatedAt: '2026-09-21T09:00:00Z' }))
    api.deleteDossierNote.mockResolvedValue(undefined)
    render(<DossierNotesPanel dossierId="d-1" canWrite />)
    await screen.findByText('Mijn notitie')

    const [mine, other] = noteItems()
    expect(within(other).queryByRole('button', { name: 'Bewerken' })).not.toBeInTheDocument()
    expect(within(other).queryByRole('button', { name: 'Verwijderen' })).not.toBeInTheDocument()

    await user.click(within(mine).getByRole('button', { name: 'Bewerken' }))
    const field = screen.getByLabelText('Notitie bewerken')
    expect(field).toHaveValue('Mijn notitie')
    await user.type(field, ' (aangepast)')
    await user.click(screen.getByRole('button', { name: 'Opslaan' }))

    expect(await screen.findByText('Mijn notitie (aangepast)')).toBeInTheDocument()
    expect(api.updateDossierNote).toHaveBeenCalledWith('d-1', 'n-mine', 'Mijn notitie (aangepast)')
    expect(noteItems()).toHaveLength(2)

    // Delete goes through the shared confirmation dialog.
    await user.click(within(noteItems()[0]).getByRole('button', { name: 'Verwijderen' }))
    const dialog = screen.getByRole('dialog', { name: 'Notitie verwijderen' })
    expect(api.deleteDossierNote).not.toHaveBeenCalled()
    await user.click(within(dialog).getByRole('button', { name: 'Verwijderen' }))

    await waitFor(() => expect(noteItems()).toHaveLength(1))
    expect(api.deleteDossierNote).toHaveBeenCalledWith('d-1', 'n-mine')
    expect(screen.getByText('Notitie van collega')).toBeInTheDocument()
    expect(api.listDossierNotes).toHaveBeenCalledTimes(1)
  })

  it('keeps the note and shows the error when deleting fails', async () => {
    const user = userEvent.setup()
    api.listDossierNotes.mockResolvedValue([note()])
    api.deleteDossierNote.mockRejectedValue(new ApiError('Je mag deze notitie niet verwijderen.', 403))
    render(<DossierNotesPanel dossierId="d-1" canWrite />)
    await screen.findByText('Klant belt terug over het losuur.')

    await user.click(screen.getByRole('button', { name: 'Verwijderen' }))
    await user.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Verwijderen' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Je mag deze notitie niet verwijderen.')
    expect(noteItems()).toHaveLength(1)
  })

  it('renders no add/edit/delete controls without write permission, even when the DTO allows them', async () => {
    api.listDossierNotes.mockResolvedValue([note()])
    render(<DossierNotesPanel dossierId="d-1" canWrite={false} />)
    await screen.findByText('Klant belt terug over het losuur.')

    expect(screen.queryByRole('button', { name: ADD })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Bewerken' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Verwijderen' })).not.toBeInTheDocument()
  })

  it('scopes strictly: the activity panel shows only that activity, the dossier panel only dossier-level notes', async () => {
    const all = [
      note({ id: 'n-d', text: 'Dossiernotitie' }),
      note({ id: 'n-5', text: 'Notitie van 0005', dossierActivityId: 'a-5' }),
      note({ id: 'n-6', text: 'Notitie van 0006', dossierActivityId: 'a-6' }),
    ]
    // A server that ignores ?activityId= must still not leak notes across scopes.
    api.listDossierNotes.mockResolvedValue(all)

    const dossierPanel = render(<DossierNotesPanel dossierId="d-1" canWrite />)
    expect(await screen.findByText('Dossiernotitie')).toBeInTheDocument()
    expect(screen.queryByText('Notitie van 0005')).not.toBeInTheDocument()
    expect(screen.queryByText('Notitie van 0006')).not.toBeInTheDocument()
    dossierPanel.unmount()

    render(<DossierNotesPanel dossierId="d-1" activityId="a-5" canWrite compact />)
    expect(await screen.findByText('Notitie van 0005')).toBeInTheDocument()
    expect(screen.queryByText('Notitie van 0006')).not.toBeInTheDocument()
    expect(screen.queryByText('Dossiernotitie')).not.toBeInTheDocument()
    expect(api.listDossierNotes).toHaveBeenLastCalledWith('d-1', 'a-5', expect.anything())
  })

  it('creates an activity note with the activity id', async () => {
    const user = userEvent.setup()
    api.createDossierNote.mockResolvedValue(note({ id: 'n-a', text: 'Kraan om 7u', dossierActivityId: 'a-5' }))
    render(<DossierNotesPanel dossierId="d-1" activityId="a-5" canWrite compact />)
    await screen.findByText('Geen notities.')

    await user.click(screen.getByRole('button', { name: ADD }))
    await user.type(screen.getByLabelText('Nieuwe notitie'), 'Kraan om 7u')
    await user.click(screen.getByRole('button', { name: 'Opslaan' }))

    expect(await screen.findByText('Kraan om 7u')).toBeInTheDocument()
    expect(api.createDossierNote).toHaveBeenCalledWith('d-1', 'Kraan om 7u', 'a-5')
  })

  it('on a load failure shows the real error, a retry and the legacy text as read-only fallback', async () => {
    const user = userEvent.setup()
    api.listDossierNotes.mockRejectedValueOnce(new ApiError('Notities tijdelijk niet beschikbaar.', 503))
    render(<DossierNotesPanel dossierId="d-1" canWrite legacyText="Oude vrije tekst" />)

    expect(await screen.findByRole('alert')).toHaveTextContent('Notities tijdelijk niet beschikbaar.')
    expect(screen.getByText('Oude vrije tekst')).toBeInTheDocument()
    expect(screen.queryByText('Geen notities.')).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: ADD })).not.toBeInTheDocument()

    api.listDossierNotes.mockResolvedValue([note({ text: 'Oude vrije tekst (gemigreerd)' })])
    await user.click(screen.getByRole('button', { name: 'Opnieuw proberen' }))

    expect(await screen.findByText('Oude vrije tekst (gemigreerd)')).toBeInTheDocument()
    // Once the notes load, the legacy block is gone: the migrated note replaces it.
    expect(screen.queryByText('Oude vrije tekst')).not.toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('ignores a stale response after the activity changed', async () => {
    let resolveOld: (value: DossierNote[]) => void = () => {}
    api.listDossierNotes.mockImplementation((_dossierId: string, activityId: string | null) =>
      activityId === 'a-5'
        ? new Promise<DossierNote[]>((resolve) => (resolveOld = resolve))
        : Promise.resolve([note({ id: 'n-6', text: 'Notitie van 0006', dossierActivityId: 'a-6' })]),
    )
    const view = render(<DossierNotesPanel dossierId="d-1" activityId="a-5" canWrite />)
    view.rerender(<DossierNotesPanel dossierId="d-1" activityId="a-6" canWrite />)
    expect(await screen.findByText('Notitie van 0006')).toBeInTheDocument()

    // The a-5 answer arrives late — and claims a-6 to make sure it is dropped for being stale, not for its scope.
    await act(async () => resolveOld([note({ id: 'n-stale', text: 'Verouderd antwoord', dossierActivityId: 'a-6' })]))

    expect(screen.queryByText('Verouderd antwoord')).not.toBeInTheDocument()
    expect(screen.getByText('Notitie van 0006')).toBeInTheDocument()
  })
})

describe('useDossierNotes', () => {
  beforeEach(() => vi.clearAllMocks())

  it('aborts the in-flight request and drops its late answer when the scope changes', async () => {
    const signals: AbortSignal[] = []
    let resolveOld: (value: DossierNote[]) => void = () => {}
    api.listDossierNotes.mockImplementation((dossierId: string, _activityId: string | null, signal: AbortSignal) => {
      signals.push(signal)
      return dossierId === 'd-1'
        ? new Promise<DossierNote[]>((resolve) => (resolveOld = resolve))
        : Promise.resolve([note({ id: 'n-2', dossierId: 'd-2', text: 'Van dossier 2' })])
    })

    const { result, rerender } = renderHook(({ dossierId }) => useDossierNotes(dossierId, null), { initialProps: { dossierId: 'd-1' } })
    rerender({ dossierId: 'd-2' })
    await waitFor(() => expect(result.current.notes.map((n) => n.id)).toEqual(['n-2']))
    expect(signals[0].aborted).toBe(true)

    await act(async () => resolveOld([note({ id: 'n-old', text: 'Van dossier 1' })]))
    expect(result.current.notes.map((n) => n.id)).toEqual(['n-2'])
  })
})

describe('NotePreview', () => {
  it('renders nothing without notes, and the latest note with its count otherwise', async () => {
    const user = userEvent.setup()
    const onOpen = vi.fn()
    const empty = render(<NotePreview preview={null} count={0} />)
    expect(empty.container).toBeEmptyDOMElement()
    empty.unmount()

    render(<NotePreview preview="Kraan om 7u" count={2} onOpen={onOpen} />)
    const button = screen.getByRole('button', { name: /2 notities.*Kraan om 7u/ })
    await user.click(button)
    expect(onOpen).toHaveBeenCalledTimes(1)
  })
})

describe('DossierHistorySection', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    auth.permissions = new Set(['audit_logs.view'])
  })

  it('renders user notes and the audit trail as two separate blocks', async () => {
    api.listDossierNotes.mockResolvedValue([note({ text: 'Handmatige notitie' })])
    getJson.mockResolvedValue({
      items: [
        {
          id: 'al-1', userId: 'u-1', entityType: 'TransportDossier', entityId: 'd-1', action: 'Updated',
          oldValuesJson: '{"Title":"Oud"}', newValuesJson: '{"Title":"Nieuw"}', timestamp: '2026-09-20T07:00:00Z',
        },
      ],
      totalCount: 1, page: 1, pageSize: 50,
    })
    const workspace = {
      dossier: dossierDetail({ notes: 'Legacy vrije tekst' }),
      canManage: true,
      isOpen: true,
      busy: false,
      removeRelation: vi.fn(),
      reloadDossier: vi.fn(),
    } as unknown as DossierWorkspace

    render(
      <MemoryRouter>
        <DossierWorkspaceContext.Provider value={workspace}>
          <DossierHistorySection />
        </DossierWorkspaceContext.Provider>
      </MemoryRouter>,
    )

    expect(await screen.findByText('Handmatige notitie')).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Notities' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Wijzigingshistoriek' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: ADD })).toBeInTheDocument()

    // The audit row lives in the audit table, never in the notes list — and vice versa.
    const auditRow = await screen.findByText('Bijgewerkt')
    const notesList = screen.getByRole('list')
    expect(notesList).not.toContainElement(auditRow)
    expect(within(notesList).getAllByRole('listitem')).toHaveLength(1)
    expect(auditRow.closest('table')).not.toBeNull()
    expect(auditRow.closest('table')).not.toContainElement(screen.getByText('Handmatige notitie'))
    expect(getJson).toHaveBeenCalledWith(expect.stringContaining('/api/audit-logs?entityType=TransportDossier&entityId=d-1'))

    // The legacy free text is no longer a separate block once the notes loaded.
    expect(screen.queryByText('Legacy vrije tekst')).not.toBeInTheDocument()
  })
})
