import { useEffect, useState, type FormEvent } from 'react'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { DataTable, type Column } from '../../../components/ui/DataTable'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { useToast } from '../../../components/ui/toastContext'
import { describeApiError } from '../../../api/problemDetails'
import {
  createPortalAnnouncement,
  deletePortalAnnouncement,
  listPortalAnnouncementsAdmin,
  updatePortalAnnouncement,
  type PortalAnnouncement,
  type SavePortalAnnouncementInput,
} from '../api/portalAnnouncementsApi'

const EMPTY_FORM: SavePortalAnnouncementInput = { title: '', body: '', activeFrom: null, activeUntil: null, isActive: true }

function toDateTimeLocal(iso: string | null): string {
  if (!iso) return ''
  const date = new Date(iso.endsWith('Z') || iso.includes('+') ? iso : `${iso}Z`)
  return date.toISOString().slice(0, 16)
}

function fromDateTimeLocal(value: string): string | null {
  return value ? new Date(value).toISOString() : null
}

/** Admin CRUD for customer-portal broadcast announcements (Beheer → Klantportaal mededelingen). */
export function PortalAnnouncementsSettingsPage() {
  const toast = useToast()
  const [announcements, setAnnouncements] = useState<PortalAnnouncement[]>([])
  const [loaded, setLoaded] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [editing, setEditing] = useState<PortalAnnouncement | null>(null)
  const [creating, setCreating] = useState(false)
  const [form, setForm] = useState<SavePortalAnnouncementInput>(EMPTY_FORM)
  const [formError, setFormError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [deleteTarget, setDeleteTarget] = useState<PortalAnnouncement | null>(null)

  function load() {
    listPortalAnnouncementsAdmin()
      .then((rows) => {
        setAnnouncements(rows)
        setLoaded(true)
        setError(null)
      })
      .catch(() => {
        setError('De mededelingen konden niet worden geladen.')
        setLoaded(true)
      })
  }

  useEffect(load, [])

  function openCreate() {
    setForm(EMPTY_FORM)
    setFormError(null)
    setCreating(true)
  }

  function openEdit(row: PortalAnnouncement) {
    setForm({ title: row.title, body: row.body, activeFrom: row.activeFrom, activeUntil: row.activeUntil, isActive: row.isActive })
    setFormError(null)
    setEditing(row)
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setBusy(true)
    setFormError(null)
    try {
      if (editing) {
        await updatePortalAnnouncement(editing.id, form)
        toast.showSuccess('Mededeling bijgewerkt.')
      } else {
        await createPortalAnnouncement(form)
        toast.showSuccess('Mededeling aangemaakt.')
      }
      setEditing(null)
      setCreating(false)
      load()
    } catch (err) {
      setFormError(describeApiError(err, 'De mededeling kon niet worden opgeslagen.').message)
    } finally {
      setBusy(false)
    }
  }

  async function handleDelete() {
    if (!deleteTarget) return
    setBusy(true)
    try {
      await deletePortalAnnouncement(deleteTarget.id)
      toast.showSuccess('Mededeling verwijderd.')
      setDeleteTarget(null)
      load()
    } catch {
      toast.showError('De mededeling kon niet worden verwijderd.')
    } finally {
      setBusy(false)
    }
  }

  const columns: Column<PortalAnnouncement>[] = [
    { key: 'title', header: 'Titel', render: (row) => <button type="button" className="link-button" onClick={() => openEdit(row)}>{row.title}</button> },
    { key: 'window', header: 'Periode', render: (row) => `${row.activeFrom ? toDateTimeLocal(row.activeFrom) : '—'} t/m ${row.activeUntil ? toDateTimeLocal(row.activeUntil) : '—'}` },
    { key: 'status', header: 'Status', render: (row) => <Badge tone={row.isActive ? 'success' : 'neutral'}>{row.isActive ? 'Actief' : 'Inactief'}</Badge> },
    {
      key: 'actions',
      header: '',
      render: (row) => (
        <Button variant="secondary" onClick={() => setDeleteTarget(row)}>
          Verwijderen
        </Button>
      ),
    },
  ]

  const formOpen = creating || editing !== null

  return (
    <div>
      <Breadcrumbs items={[{ label: 'Beheer' }, { label: 'Klantportaal mededelingen' }]} />
      <PageHeader
        title="Klantportaal mededelingen"
        subtitle="Mededelingen zichtbaar in het klantportaal binnen hun actieve periode."
        action={<Button onClick={openCreate}>Nieuwe mededeling</Button>}
      />
      <DataTable
        columns={columns}
        rows={announcements}
        rowKey={(row) => row.id}
        isLoading={!loaded}
        error={error}
        emptyMessage="Nog geen mededelingen."
        loadingMessage="Mededelingen laden..."
      />

      {formOpen && (
        <Modal
          title={editing ? 'Mededeling bewerken' : 'Nieuwe mededeling'}
          onClose={() => {
            setEditing(null)
            setCreating(false)
          }}
          busy={busy}
          footer={
            <>
              <Button
                variant="secondary"
                disabled={busy}
                onClick={() => {
                  setEditing(null)
                  setCreating(false)
                }}
              >
                Annuleren
              </Button>
              <Button type="submit" form="portal-announcement-form" disabled={busy}>
                {busy ? 'Bezig...' : 'Opslaan'}
              </Button>
            </>
          }
        >
          <form id="portal-announcement-form" onSubmit={(e) => void handleSubmit(e)}>
            {formError && <p className="placeholder-text" role="alert">{formError}</p>}
            <FormField label="Titel" htmlFor="pa-title" required>
              <input
                id="pa-title"
                value={form.title}
                onChange={(e) => setForm({ ...form, title: e.target.value })}
                maxLength={200}
                required
              />
            </FormField>
            <FormField label="Inhoud" htmlFor="pa-body" required>
              <textarea
                id="pa-body"
                value={form.body}
                onChange={(e) => setForm({ ...form, body: e.target.value })}
                maxLength={4000}
                rows={4}
                required
              />
            </FormField>
            <FormField label="Actief vanaf" htmlFor="pa-from" hint="Leeg = direct zichtbaar">
              <input
                id="pa-from"
                type="datetime-local"
                value={toDateTimeLocal(form.activeFrom)}
                onChange={(e) => setForm({ ...form, activeFrom: fromDateTimeLocal(e.target.value) })}
              />
            </FormField>
            <FormField label="Actief tot" htmlFor="pa-until" hint="Leeg = geen einddatum">
              <input
                id="pa-until"
                type="datetime-local"
                value={toDateTimeLocal(form.activeUntil)}
                onChange={(e) => setForm({ ...form, activeUntil: fromDateTimeLocal(e.target.value) })}
              />
            </FormField>
            <FormField label="Status" htmlFor="pa-active">
              <label>
                <input
                  id="pa-active"
                  type="checkbox"
                  checked={form.isActive}
                  onChange={(e) => setForm({ ...form, isActive: e.target.checked })}
                />{' '}
                Actief
              </label>
            </FormField>
          </form>
        </Modal>
      )}

      {deleteTarget && (
        <ConfirmDialog
          title="Mededeling verwijderen"
          message={`Weet u zeker dat u "${deleteTarget.title}" wilt verwijderen?`}
          destructive
          busy={busy}
          onConfirm={() => void handleDelete()}
          onCancel={() => setDeleteTarget(null)}
        />
      )}
    </div>
  )
}
