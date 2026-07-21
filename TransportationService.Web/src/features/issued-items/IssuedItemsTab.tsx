import { useEffect, useState, type FormEvent } from 'react'
import { Badge, type BadgeTone } from '../../components/ui/Badge'
import { Button } from '../../components/ui/Button'
import { ConfirmDialog } from '../../components/ui/ConfirmDialog'
import { FormField } from '../../components/ui/FormField'
import { Modal } from '../../components/ui/Modal'
import { useToast } from '../../components/ui/toastContext'
import { useAuth } from '../auth/authContextValue'
import {
  deleteEmployeeIssuedItem,
  downloadIssuedItemsAcknowledgement,
  ISSUED_ITEM_STATUS_LABELS,
  ISSUED_ITEM_STATUSES,
  listEmployeeIssuedItems,
  listIssuedItemTemplates,
  saveEmployeeIssuedItem,
  type EmployeeIssuedItem,
  type EmployeeIssuedItemInput,
  type IssuedItemStatus,
  type IssuedItemTemplate,
} from './issuedItemsApi'
import './issued-items.css'

const STATUS_TONE: Record<IssuedItemStatus, BadgeTone> = {
  NotIssued: 'neutral',
  Issued: 'success',
  Returned: 'info',
  Missing: 'danger',
  Damaged: 'warning',
}

function emptyForm(): EmployeeIssuedItemInput {
  return {
    templateId: null,
    name: '',
    category: '',
    status: 'Issued',
    issuedDate: new Date().toISOString().slice(0, 10),
    quantity: 1,
    serialNumber: null,
    notes: null,
    returnedDate: null,
    returnCondition: null,
  }
}

/** Employee "Bedrijfsmiddelen" checklist: issued items with status, add/edit, PDF acknowledgement. */
export function IssuedItemsTab({ employeeId }: { employeeId: string }) {
  const { showSuccess, showError } = useToast()
  const { hasPermission } = useAuth()
  const canManage = hasPermission('issued_items.manage')

  const [items, setItems] = useState<EmployeeIssuedItem[] | null>(null)
  const [templates, setTemplates] = useState<IssuedItemTemplate[]>([])
  const [loadError, setLoadError] = useState<string | null>(null)
  const [reloadToken, setReloadToken] = useState(0)

  const [editorOpen, setEditorOpen] = useState(false)
  const [editingId, setEditingId] = useState<string | null>(null)
  const [form, setForm] = useState<EmployeeIssuedItemInput>(emptyForm())
  const [formError, setFormError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const [deleteTarget, setDeleteTarget] = useState<EmployeeIssuedItem | null>(null)

  useEffect(() => {
    let mounted = true
    listEmployeeIssuedItems(employeeId)
      .then((data) => {
        if (!mounted) return
        setItems(data)
        setLoadError(null)
      })
      .catch(() => {
        if (mounted) setLoadError('Bedrijfsmiddelen konden niet worden geladen.')
      })
    return () => {
      mounted = false
    }
  }, [employeeId, reloadToken])

  useEffect(() => {
    let mounted = true
    if (canManage) {
      listIssuedItemTemplates()
        .then((data) => {
          if (mounted) setTemplates(data)
        })
        .catch(() => {})
    }
    return () => {
      mounted = false
    }
  }, [canManage])

  function set<K extends keyof EmployeeIssuedItemInput>(key: K, value: EmployeeIssuedItemInput[K]) {
    setForm((f) => ({ ...f, [key]: value }))
  }

  function applyTemplate(templateId: string) {
    const template = templates.find((t) => t.id === templateId)
    if (!template) {
      setForm((f) => ({ ...f, templateId: null }))
      return
    }
    setForm((f) => ({
      ...f,
      templateId: template.id,
      name: template.name,
      category: template.category,
      quantity: template.defaultQuantity,
    }))
  }

  function openCreate() {
    setEditingId(null)
    setForm(emptyForm())
    setFormError(null)
    setEditorOpen(true)
  }

  function openEdit(item: EmployeeIssuedItem) {
    setEditingId(item.id)
    setForm({
      templateId: item.templateId,
      name: item.name,
      category: item.category,
      status: item.status,
      issuedDate: item.issuedDate,
      quantity: item.quantity,
      serialNumber: item.serialNumber,
      notes: item.notes,
      returnedDate: item.returnedDate,
      returnCondition: item.returnCondition,
    })
    setFormError(null)
    setEditorOpen(true)
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setFormError(null)
    if (!form.name?.trim() || !form.category?.trim()) {
      setFormError('Naam en categorie zijn verplicht.')
      return
    }
    setSaving(true)
    try {
      await saveEmployeeIssuedItem(employeeId, editingId, form)
      showSuccess(editingId ? 'Bedrijfsmiddel bijgewerkt.' : 'Bedrijfsmiddel toegevoegd.')
      setEditorOpen(false)
      setReloadToken((t) => t + 1)
    } catch {
      setFormError('Het bedrijfsmiddel kon niet worden opgeslagen.')
    } finally {
      setSaving(false)
    }
  }

  async function handleDelete() {
    if (!deleteTarget) return
    try {
      await deleteEmployeeIssuedItem(employeeId, deleteTarget.id)
      showSuccess('Bedrijfsmiddel verwijderd.')
      setDeleteTarget(null)
      setReloadToken((t) => t + 1)
    } catch {
      showError('Het bedrijfsmiddel kon niet worden verwijderd.')
      setDeleteTarget(null)
    }
  }

  async function handleDownload() {
    try {
      await downloadIssuedItemsAcknowledgement(employeeId)
    } catch {
      showError('Het ontvangstbewijs kon niet worden gedownload.')
    }
  }

  return (
    <section className="issued-items">
      <div className="issued-items-header">
        <h2>Bedrijfsmiddelen</h2>
        <div className="issued-items-actions-top">
          {items !== null && items.length > 0 && (
            <Button variant="secondary" onClick={handleDownload}>
              Ontvangstbewijs (PDF)
            </Button>
          )}
          {canManage && <Button onClick={openCreate}>Bedrijfsmiddel toevoegen</Button>}
        </div>
      </div>

      {loadError && <p className="placeholder-text">{loadError}</p>}
      {!loadError && items === null && <p className="placeholder-text">Laden…</p>}
      {!loadError && items !== null && items.length === 0 && (
        <p className="placeholder-text">Nog geen bedrijfsmiddelen geregistreerd.</p>
      )}

      {!loadError && items !== null && items.length > 0 && (
        <table className="issued-items-table">
          <thead>
            <tr>
              <th>Middel</th>
              <th>Categorie</th>
              <th>Aantal</th>
              <th>Serienr.</th>
              <th>Uitgereikt</th>
              <th>Status</th>
              <th aria-label="Acties" />
            </tr>
          </thead>
          <tbody>
            {items.map((item) => (
              <tr key={item.id}>
                <td>{item.name}</td>
                <td>{item.category}</td>
                <td>{item.quantity}</td>
                <td>{item.serialNumber ?? '—'}</td>
                <td>{item.issuedDate ?? '—'}</td>
                <td>
                  <Badge tone={STATUS_TONE[item.status]}>{ISSUED_ITEM_STATUS_LABELS[item.status]}</Badge>
                </td>
                <td className="issued-items-row-actions">
                  {canManage && (
                    <button type="button" className="issued-items-link" onClick={() => openEdit(item)}>
                      Bewerken
                    </button>
                  )}
                  {canManage && (
                    <button type="button" className="issued-items-link issued-items-link-danger" onClick={() => setDeleteTarget(item)}>
                      Verwijderen
                    </button>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      {editorOpen && (
        <Modal
          title={editingId ? 'Bedrijfsmiddel bewerken' : 'Bedrijfsmiddel toevoegen'}
          onClose={() => setEditorOpen(false)}
          busy={saving}
          footer={
            <>
              <Button variant="secondary" onClick={() => setEditorOpen(false)} disabled={saving}>
                Annuleren
              </Button>
              <Button type="submit" form="issued-item-form" disabled={saving}>
                {saving ? 'Opslaan…' : 'Opslaan'}
              </Button>
            </>
          }
        >
          <form id="issued-item-form" className="issued-items-form" onSubmit={handleSubmit} noValidate>
            {formError && (
              <div className="issued-items-form-error" role="alert">
                {formError}
              </div>
            )}
            {!editingId && templates.length > 0 && (
              <FormField label="Uit sjabloon" htmlFor="ii-template" hint="Vult naam, categorie en aantal automatisch in.">
                <select id="ii-template" value={form.templateId ?? ''} onChange={(e) => applyTemplate(e.target.value)} disabled={saving}>
                  <option value="">— Aangepast —</option>
                  {templates.map((t) => (
                    <option key={t.id} value={t.id}>
                      {t.name} ({t.category})
                    </option>
                  ))}
                </select>
              </FormField>
            )}
            <div className="issued-items-form-row">
              <FormField label="Middel" htmlFor="ii-name" required>
                <input id="ii-name" value={form.name ?? ''} onChange={(e) => set('name', e.target.value)} disabled={saving} maxLength={150} />
              </FormField>
              <FormField label="Categorie" htmlFor="ii-cat" required>
                <input id="ii-cat" value={form.category ?? ''} onChange={(e) => set('category', e.target.value)} disabled={saving} maxLength={100} />
              </FormField>
            </div>
            <div className="issued-items-form-row">
              <FormField label="Status" htmlFor="ii-status" required>
                <select id="ii-status" value={form.status} onChange={(e) => set('status', e.target.value as IssuedItemStatus)} disabled={saving}>
                  {ISSUED_ITEM_STATUSES.map((s) => (
                    <option key={s} value={s}>
                      {ISSUED_ITEM_STATUS_LABELS[s]}
                    </option>
                  ))}
                </select>
              </FormField>
              <FormField label="Aantal" htmlFor="ii-qty">
                <input id="ii-qty" type="number" min={1} value={form.quantity} onChange={(e) => set('quantity', Number(e.target.value) || 1)} disabled={saving} />
              </FormField>
            </div>
            <div className="issued-items-form-row">
              <FormField label="Uitreikingsdatum" htmlFor="ii-date">
                <input id="ii-date" type="date" value={form.issuedDate ?? ''} onChange={(e) => set('issuedDate', e.target.value || null)} disabled={saving} />
              </FormField>
              <FormField label="Serienummer" htmlFor="ii-serial">
                <input id="ii-serial" value={form.serialNumber ?? ''} onChange={(e) => set('serialNumber', e.target.value || null)} disabled={saving} maxLength={100} />
              </FormField>
            </div>
            {(form.status === 'Returned' || form.status === 'Damaged') && (
              <div className="issued-items-form-row">
                <FormField label="Datum teruggave" htmlFor="ii-retdate">
                  <input id="ii-retdate" type="date" value={form.returnedDate ?? ''} onChange={(e) => set('returnedDate', e.target.value || null)} disabled={saving} />
                </FormField>
                <FormField label="Staat bij teruggave" htmlFor="ii-retcond">
                  <input id="ii-retcond" value={form.returnCondition ?? ''} onChange={(e) => set('returnCondition', e.target.value || null)} disabled={saving} maxLength={150} />
                </FormField>
              </div>
            )}
            <FormField label="Notities" htmlFor="ii-notes">
              <textarea id="ii-notes" rows={2} value={form.notes ?? ''} onChange={(e) => set('notes', e.target.value || null)} disabled={saving} />
            </FormField>
          </form>
        </Modal>
      )}

      {deleteTarget && (
        <ConfirmDialog
          title="Bedrijfsmiddel verwijderen"
          message={`Weet je zeker dat je "${deleteTarget.name}" wilt verwijderen?`}
          confirmLabel="Verwijderen"
          destructive
          onConfirm={handleDelete}
          onCancel={() => setDeleteTarget(null)}
        />
      )}
    </section>
  )
}
