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
import { useLocale } from '../../../i18n/localeContext'
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
  const { t } = useLocale()
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
        setError('portalAnnouncements.loadFailed')
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
        toast.showSuccess(t('portalAnnouncements.updated'))
      } else {
        await createPortalAnnouncement(form)
        toast.showSuccess(t('portalAnnouncements.created'))
      }
      setEditing(null)
      setCreating(false)
      load()
    } catch (err) {
      setFormError(describeApiError(err, t('portalAnnouncements.saveFailed')).message)
    } finally {
      setBusy(false)
    }
  }

  async function handleDelete() {
    if (!deleteTarget) return
    setBusy(true)
    try {
      await deletePortalAnnouncement(deleteTarget.id)
      toast.showSuccess(t('portalAnnouncements.deleted'))
      setDeleteTarget(null)
      load()
    } catch {
      toast.showError(t('portalAnnouncements.deleteFailed'))
    } finally {
      setBusy(false)
    }
  }

  const columns: Column<PortalAnnouncement>[] = [
    {
      key: 'title',
      header: t('portalAnnouncements.columnTitle'),
      render: (row) => (
        <button type="button" className="link-button" onClick={() => openEdit(row)}>
          {row.title}
        </button>
      ),
    },
    {
      key: 'window',
      header: t('portalAnnouncements.columnWindow'),
      render: (row) =>
        t('portalAnnouncements.windowRange', {
          from: row.activeFrom ? toDateTimeLocal(row.activeFrom) : '—',
          until: row.activeUntil ? toDateTimeLocal(row.activeUntil) : '—',
        }),
    },
    {
      key: 'status',
      header: t('portalAnnouncements.columnStatus'),
      render: (row) => (
        <Badge tone={row.isActive ? 'success' : 'neutral'}>
          {row.isActive ? t('ui.statusBadges.active') : t('ui.statusBadges.inactive')}
        </Badge>
      ),
    },
    {
      key: 'actions',
      header: '',
      render: (row) => (
        <Button variant="secondary" onClick={() => setDeleteTarget(row)}>
          {t('ui.actions.delete')}
        </Button>
      ),
    },
  ]

  const formOpen = creating || editing !== null

  return (
    <div>
      <Breadcrumbs
        items={[{ label: t('portalAnnouncements.breadcrumbAdmin') }, { label: t('navigation.menu.portalAnnouncements') }]}
      />
      <PageHeader
        title={t('navigation.menu.portalAnnouncements')}
        subtitle={t('portalAnnouncements.subtitle')}
        action={<Button onClick={openCreate}>{t('portalAnnouncements.newAnnouncement')}</Button>}
      />
      <DataTable
        columns={columns}
        rows={announcements}
        rowKey={(row) => row.id}
        isLoading={!loaded}
        error={error ? t(error) : null}
        emptyMessage={t('portalAnnouncements.empty')}
        loadingMessage={t('portalAnnouncements.loading')}
      />

      {formOpen && (
        <Modal
          title={editing ? t('portalAnnouncements.editTitle') : t('portalAnnouncements.newAnnouncement')}
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
                {t('ui.actions.cancel')}
              </Button>
              <Button type="submit" form="portal-announcement-form" disabled={busy}>
                {busy ? t('ui.actions.busy') : t('ui.actions.save')}
              </Button>
            </>
          }
        >
          <form id="portal-announcement-form" onSubmit={(e) => void handleSubmit(e)}>
            {formError && <p className="placeholder-text" role="alert">{formError}</p>}
            <FormField label={t('portalAnnouncements.titleField')} htmlFor="pa-title" required>
              <input
                id="pa-title"
                value={form.title}
                onChange={(e) => setForm({ ...form, title: e.target.value })}
                maxLength={200}
                required
              />
            </FormField>
            <FormField label={t('portalAnnouncements.bodyField')} htmlFor="pa-body" required>
              <textarea
                id="pa-body"
                value={form.body}
                onChange={(e) => setForm({ ...form, body: e.target.value })}
                maxLength={4000}
                rows={4}
                required
              />
            </FormField>
            <FormField label={t('portalAnnouncements.activeFromField')} htmlFor="pa-from" hint={t('portalAnnouncements.activeFromHint')}>
              <input
                id="pa-from"
                type="datetime-local"
                value={toDateTimeLocal(form.activeFrom)}
                onChange={(e) => setForm({ ...form, activeFrom: fromDateTimeLocal(e.target.value) })}
              />
            </FormField>
            <FormField label={t('portalAnnouncements.activeUntilField')} htmlFor="pa-until" hint={t('portalAnnouncements.activeUntilHint')}>
              <input
                id="pa-until"
                type="datetime-local"
                value={toDateTimeLocal(form.activeUntil)}
                onChange={(e) => setForm({ ...form, activeUntil: fromDateTimeLocal(e.target.value) })}
              />
            </FormField>
            <FormField label={t('portalAnnouncements.columnStatus')} htmlFor="pa-active">
              <label>
                <input
                  id="pa-active"
                  type="checkbox"
                  checked={form.isActive}
                  onChange={(e) => setForm({ ...form, isActive: e.target.checked })}
                />{' '}
                {t('ui.statusBadges.active')}
              </label>
            </FormField>
          </form>
        </Modal>
      )}

      {deleteTarget && (
        <ConfirmDialog
          title={t('portalAnnouncements.deleteTitle')}
          message={t('portalAnnouncements.deleteMessage', { title: deleteTarget.title })}
          destructive
          busy={busy}
          onConfirm={() => void handleDelete()}
          onCancel={() => setDeleteTarget(null)}
        />
      )}
    </div>
  )
}
