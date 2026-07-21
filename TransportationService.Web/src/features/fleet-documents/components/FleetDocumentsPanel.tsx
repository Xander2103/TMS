import { useEffect, useRef, useState, type ChangeEvent, type FormEvent } from 'react'
import { Badge, type BadgeTone } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import {
  createFleetDocument,
  deleteFleetDocument,
  deleteFleetDocumentFile,
  downloadFleetDocumentFile,
  FLEET_DOCUMENT_ACCEPT,
  listFleetDocuments,
  updateFleetDocument,
  uploadFleetDocumentFile,
  type FleetDocumentOwnerType,
} from '../api/fleetDocumentsApi'
import {
  FLEET_DOCUMENT_STATUS_LABELS,
  FLEET_DOCUMENT_TYPE_LABELS,
  FLEET_DOCUMENT_TYPES,
  fleetDocumentDisplayName,
  type FleetDocument,
  type FleetDocumentInput,
  type FleetDocumentType,
} from '../types'
import './fleet-documents.css'

const STATUS_TONE: Record<FleetDocument['status'], BadgeTone> = {
  NoExpiry: 'neutral',
  Valid: 'success',
  ExpiringSoon: 'warning',
  Expired: 'danger',
}

const EMPTY_FORM: FleetDocumentInput = {
  documentType: 'Registration',
  customTypeName: null,
  documentNumber: null,
  issueDate: null,
  expiryDate: null,
  warningDays: null,
  issuingAuthority: null,
  notes: null,
}

interface FleetDocumentsPanelProps {
  ownerType: FleetDocumentOwnerType
  ownerId: string
}

/** Documents section for a vehicle or trailer detail page: list, add, edit, delete. */
export function FleetDocumentsPanel({ ownerType, ownerId }: FleetDocumentsPanelProps) {
  const { showSuccess, showError } = useToast()
  const { hasPermission } = useAuth()

  const [documents, setDocuments] = useState<FleetDocument[] | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [reloadToken, setReloadToken] = useState(0)

  const [editorOpen, setEditorOpen] = useState(false)
  const [editingId, setEditingId] = useState<string | null>(null)
  const [form, setForm] = useState<FleetDocumentInput>(EMPTY_FORM)
  const [formError, setFormError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const [deleteTarget, setDeleteTarget] = useState<FleetDocument | null>(null)

  useEffect(() => {
    let mounted = true
    listFleetDocuments(ownerType, ownerId)
      .then((data) => {
        if (!mounted) return
        setDocuments(data)
        setLoadError(null)
      })
      .catch(() => {
        if (mounted) setLoadError('Documenten konden niet worden geladen.')
      })
    return () => {
      mounted = false
    }
  }, [ownerType, ownerId, reloadToken])

  function set<K extends keyof FleetDocumentInput>(key: K, value: FleetDocumentInput[K]) {
    setForm((f) => ({ ...f, [key]: value }))
  }

  function openCreate() {
    setEditingId(null)
    setForm(EMPTY_FORM)
    setFormError(null)
    setEditorOpen(true)
  }

  function openEdit(doc: FleetDocument) {
    setEditingId(doc.id)
    setForm({
      documentType: doc.documentType,
      customTypeName: doc.customTypeName,
      documentNumber: doc.documentNumber,
      issueDate: doc.issueDate,
      expiryDate: doc.expiryDate,
      warningDays: doc.warningDays,
      issuingAuthority: doc.issuingAuthority,
      notes: doc.notes,
    })
    setFormError(null)
    setEditorOpen(true)
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setFormError(null)
    if (form.documentType === 'Other' && !form.customTypeName?.trim()) {
      setFormError('Geef een naam op voor het aangepaste documenttype.')
      return
    }
    setSaving(true)
    try {
      if (editingId) {
        await updateFleetDocument(editingId, form)
        showSuccess('Document bijgewerkt.')
      } else {
        await createFleetDocument(ownerType, ownerId, form)
        showSuccess('Document toegevoegd.')
      }
      setEditorOpen(false)
      setReloadToken((t) => t + 1)
    } catch {
      setFormError('Het document kon niet worden opgeslagen.')
    } finally {
      setSaving(false)
    }
  }

  async function handleDelete() {
    if (!deleteTarget) return
    try {
      await deleteFleetDocument(deleteTarget.id)
      showSuccess('Document verwijderd.')
      setDeleteTarget(null)
      setReloadToken((t) => t + 1)
    } catch {
      showError('Het document kon niet worden verwijderd.')
      setDeleteTarget(null)
    }
  }

  // Attachment upload/replace goes through a hidden file input, keyed to the row being uploaded.
  const fileInputRef = useRef<HTMLInputElement | null>(null)
  const [uploadTargetId, setUploadTargetId] = useState<string | null>(null)
  const [uploadingId, setUploadingId] = useState<string | null>(null)

  function pickFile(docId: string) {
    setUploadTargetId(docId)
    fileInputRef.current?.click()
  }

  async function handleFileSelected(event: ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0]
    event.target.value = ''
    if (!file || !uploadTargetId) return
    const docId = uploadTargetId
    setUploadTargetId(null)
    setUploadingId(docId)
    try {
      await uploadFleetDocumentFile(docId, file)
      showSuccess('Bestand geüpload.')
      setReloadToken((t) => t + 1)
    } catch {
      showError('Het bestand kon niet worden geüpload (max. 10 MB, pdf/jpg/png).')
    } finally {
      setUploadingId(null)
    }
  }

  async function handleDownload(doc: FleetDocument) {
    try {
      await downloadFleetDocumentFile(doc.id, doc.fileName ?? 'document')
    } catch {
      showError('Het bestand kon niet worden gedownload.')
    }
  }

  async function handleRemoveFile(doc: FleetDocument) {
    try {
      await deleteFleetDocumentFile(doc.id)
      showSuccess('Bestand verwijderd.')
      setReloadToken((t) => t + 1)
    } catch {
      showError('Het bestand kon niet worden verwijderd.')
    }
  }

  return (
    <section className="fleet-docs">
      <div className="fleet-docs-header">
        <h2>Documenten</h2>
        {hasPermission('fleet_documents.create') && (
          <Button variant="secondary" onClick={openCreate}>
            Document toevoegen
          </Button>
        )}
      </div>

      {loadError && <p className="placeholder-text">{loadError}</p>}
      {!loadError && documents === null && <p className="placeholder-text">Documenten laden…</p>}
      {!loadError && documents !== null && documents.length === 0 && (
        <p className="placeholder-text">Nog geen documenten geregistreerd.</p>
      )}

      {!loadError && documents !== null && documents.length > 0 && (
        <table className="fleet-docs-table">
          <thead>
            <tr>
              <th>Document</th>
              <th>Nummer</th>
              <th>Uitgifte</th>
              <th>Vervaldatum</th>
              <th>Status</th>
              <th>Bestand</th>
              <th aria-label="Acties" />
            </tr>
          </thead>
          <tbody>
            {documents.map((doc) => (
              <tr key={doc.id}>
                <td>{fleetDocumentDisplayName(doc)}</td>
                <td>{doc.documentNumber ?? '—'}</td>
                <td>{doc.issueDate ?? '—'}</td>
                <td>{doc.expiryDate ?? '—'}</td>
                <td>
                  <Badge tone={STATUS_TONE[doc.status]}>{FLEET_DOCUMENT_STATUS_LABELS[doc.status]}</Badge>
                </td>
                <td className="fleet-docs-file">
                  {doc.hasAttachment ? (
                    <>
                      <button type="button" className="fleet-docs-link" onClick={() => handleDownload(doc)}>
                        Downloaden
                      </button>
                      {hasPermission('fleet_documents.edit') && (
                        <button type="button" className="fleet-docs-link" onClick={() => pickFile(doc.id)} disabled={uploadingId === doc.id}>
                          {uploadingId === doc.id ? 'Bezig…' : 'Vervangen'}
                        </button>
                      )}
                      {hasPermission('fleet_documents.edit') && (
                        <button type="button" className="fleet-docs-link fleet-docs-link-danger" onClick={() => handleRemoveFile(doc)}>
                          Wissen
                        </button>
                      )}
                    </>
                  ) : hasPermission('fleet_documents.edit') ? (
                    <button type="button" className="fleet-docs-link" onClick={() => pickFile(doc.id)} disabled={uploadingId === doc.id}>
                      {uploadingId === doc.id ? 'Bezig…' : 'Uploaden'}
                    </button>
                  ) : (
                    '—'
                  )}
                </td>
                <td className="fleet-docs-actions">
                  {hasPermission('fleet_documents.edit') && (
                    <button type="button" className="fleet-docs-link" onClick={() => openEdit(doc)}>
                      Bewerken
                    </button>
                  )}
                  {hasPermission('fleet_documents.delete') && (
                    <button type="button" className="fleet-docs-link fleet-docs-link-danger" onClick={() => setDeleteTarget(doc)}>
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
          title={editingId ? 'Document bewerken' : 'Document toevoegen'}
          onClose={() => setEditorOpen(false)}
          busy={saving}
          footer={
            <>
              <Button variant="secondary" onClick={() => setEditorOpen(false)} disabled={saving}>
                Annuleren
              </Button>
              <Button type="submit" form="fleet-doc-form" disabled={saving}>
                {saving ? 'Opslaan…' : 'Opslaan'}
              </Button>
            </>
          }
        >
          <form id="fleet-doc-form" className="fleet-docs-form" onSubmit={handleSubmit} noValidate>
            {formError && (
              <div className="fleet-docs-form-error" role="alert">
                {formError}
              </div>
            )}
            <FormField label="Documenttype" htmlFor="fd-type" required>
              <select
                id="fd-type"
                value={form.documentType}
                onChange={(e) => set('documentType', e.target.value as FleetDocumentType)}
                disabled={saving}
              >
                {FLEET_DOCUMENT_TYPES.map((type) => (
                  <option key={type} value={type}>
                    {FLEET_DOCUMENT_TYPE_LABELS[type]}
                  </option>
                ))}
              </select>
            </FormField>
            {form.documentType === 'Other' && (
              <FormField label="Naam documenttype" htmlFor="fd-custom" required>
                <input
                  id="fd-custom"
                  value={form.customTypeName ?? ''}
                  onChange={(e) => set('customTypeName', e.target.value || null)}
                  disabled={saving}
                  maxLength={100}
                />
              </FormField>
            )}
            <FormField label="Documentnummer" htmlFor="fd-number">
              <input
                id="fd-number"
                value={form.documentNumber ?? ''}
                onChange={(e) => set('documentNumber', e.target.value || null)}
                disabled={saving}
                maxLength={100}
              />
            </FormField>
            <FormField label="Uitgevende instantie" htmlFor="fd-authority">
              <input
                id="fd-authority"
                value={form.issuingAuthority ?? ''}
                onChange={(e) => set('issuingAuthority', e.target.value || null)}
                disabled={saving}
                maxLength={150}
              />
            </FormField>
            <div className="fleet-docs-form-row">
              <FormField label="Uitgiftedatum" htmlFor="fd-issue">
                <input
                  id="fd-issue"
                  type="date"
                  value={form.issueDate ?? ''}
                  onChange={(e) => set('issueDate', e.target.value || null)}
                  disabled={saving}
                />
              </FormField>
              <FormField label="Vervaldatum" htmlFor="fd-expiry">
                <input
                  id="fd-expiry"
                  type="date"
                  value={form.expiryDate ?? ''}
                  onChange={(e) => set('expiryDate', e.target.value || null)}
                  disabled={saving}
                />
              </FormField>
            </div>
            <FormField
              label="Waarschuwing (dagen vóór verval)"
              htmlFor="fd-warning"
              hint="Leeg = standaard 60 dagen"
            >
              <input
                id="fd-warning"
                type="number"
                min={0}
                max={365}
                value={form.warningDays ?? ''}
                onChange={(e) => set('warningDays', e.target.value === '' ? null : Number(e.target.value))}
                disabled={saving}
              />
            </FormField>
            <FormField label="Notities" htmlFor="fd-notes">
              <textarea
                id="fd-notes"
                rows={2}
                value={form.notes ?? ''}
                onChange={(e) => set('notes', e.target.value || null)}
                disabled={saving}
              />
            </FormField>
          </form>
        </Modal>
      )}

      {deleteTarget && (
        <ConfirmDialog
          title="Document verwijderen"
          message={`Weet je zeker dat je "${fleetDocumentDisplayName(deleteTarget)}" wilt verwijderen?`}
          confirmLabel="Verwijderen"
          destructive
          onConfirm={handleDelete}
          onCancel={() => setDeleteTarget(null)}
        />
      )}

      <input
        ref={fileInputRef}
        type="file"
        accept={FLEET_DOCUMENT_ACCEPT}
        className="fleet-docs-file-input"
        onChange={handleFileSelected}
      />
    </section>
  )
}
