import { useEffect, useRef, useState, type ChangeEvent, type FormEvent } from 'react'
import { Badge, type BadgeTone } from '../../components/ui/Badge'
import { Button } from '../../components/ui/Button'
import { ConfirmDialog } from '../../components/ui/ConfirmDialog'
import { FormField } from '../../components/ui/FormField'
import { Modal } from '../../components/ui/Modal'
import { useToast } from '../../components/ui/toastContext'
import { useAuth } from '../auth/authContextValue'
import {
  createTachographCalibration,
  deleteTachographCalibration,
  downloadTachographFile,
  listTachographCalibrations,
  TACHOGRAPH_STATUS_LABELS,
  updateTachographCalibration,
  uploadTachographFile,
  type TachographCalibration,
  type TachographInput,
  type TachographStatus,
} from './tachographApi'
import './fleet-compliance.css'

const ACCEPT = '.pdf,.jpg,.jpeg,.png'

const STATUS_TONE: Record<TachographStatus, BadgeTone> = {
  Valid: 'success',
  ExpiringSoon: 'warning',
  Overdue: 'danger',
}

function emptyForm(): TachographInput {
  return {
    tachographType: null,
    manufacturer: null,
    model: null,
    serialNumber: null,
    calibrationDate: '',
    nextCalibrationDue: '',
    workshop: null,
    certificateNumber: null,
    sealReference: null,
    odometerKm: null,
    tyreCircumferenceMm: null,
    notes: null,
  }
}

/** Tachograph calibrations for a vehicle: list, add, edit, delete, attachment, status. */
export function TachographPanel({ vehicleId }: { vehicleId: string }) {
  const { showSuccess, showError } = useToast()
  const { hasPermission } = useAuth()
  const canManage = hasPermission('tachograph.manage')

  const [items, setItems] = useState<TachographCalibration[] | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [reloadToken, setReloadToken] = useState(0)

  const [editorOpen, setEditorOpen] = useState(false)
  const [editingId, setEditingId] = useState<string | null>(null)
  const [form, setForm] = useState<TachographInput>(emptyForm())
  const [formError, setFormError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const [deleteTarget, setDeleteTarget] = useState<TachographCalibration | null>(null)

  const fileInputRef = useRef<HTMLInputElement | null>(null)
  const [uploadTargetId, setUploadTargetId] = useState<string | null>(null)
  const [uploadingId, setUploadingId] = useState<string | null>(null)

  useEffect(() => {
    let mounted = true
    listTachographCalibrations(vehicleId)
      .then((data) => {
        if (!mounted) return
        setItems(data)
        setLoadError(null)
      })
      .catch(() => {
        if (mounted) setLoadError('Tachograafgegevens konden niet worden geladen.')
      })
    return () => {
      mounted = false
    }
  }, [vehicleId, reloadToken])

  function set<K extends keyof TachographInput>(key: K, value: TachographInput[K]) {
    setForm((f) => ({ ...f, [key]: value }))
  }

  function openCreate() {
    setEditingId(null)
    setForm(emptyForm())
    setFormError(null)
    setEditorOpen(true)
  }

  function openEdit(item: TachographCalibration) {
    setEditingId(item.id)
    setForm({
      tachographType: item.tachographType,
      manufacturer: item.manufacturer,
      model: item.model,
      serialNumber: item.serialNumber,
      calibrationDate: item.calibrationDate,
      nextCalibrationDue: item.nextCalibrationDue,
      workshop: item.workshop,
      certificateNumber: item.certificateNumber,
      sealReference: item.sealReference,
      odometerKm: item.odometerKm,
      tyreCircumferenceMm: item.tyreCircumferenceMm,
      notes: item.notes,
    })
    setFormError(null)
    setEditorOpen(true)
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setFormError(null)
    if (!form.calibrationDate || !form.nextCalibrationDue) {
      setFormError('IJkdatum en volgende ijkdatum zijn verplicht.')
      return
    }
    setSaving(true)
    try {
      if (editingId) {
        await updateTachographCalibration(vehicleId, editingId, form)
        showSuccess('Tachograafijking bijgewerkt.')
      } else {
        await createTachographCalibration(vehicleId, form)
        showSuccess('Tachograafijking toegevoegd.')
      }
      setEditorOpen(false)
      setReloadToken((t) => t + 1)
    } catch {
      setFormError('De ijking kon niet worden opgeslagen.')
    } finally {
      setSaving(false)
    }
  }

  async function handleDelete() {
    if (!deleteTarget) return
    try {
      await deleteTachographCalibration(vehicleId, deleteTarget.id)
      showSuccess('Tachograafijking verwijderd.')
      setDeleteTarget(null)
      setReloadToken((t) => t + 1)
    } catch {
      showError('De ijking kon niet worden verwijderd.')
      setDeleteTarget(null)
    }
  }

  function pickFile(id: string) {
    setUploadTargetId(id)
    fileInputRef.current?.click()
  }

  async function handleFileSelected(event: ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0]
    event.target.value = ''
    if (!file || !uploadTargetId) return
    const id = uploadTargetId
    setUploadTargetId(null)
    setUploadingId(id)
    try {
      await uploadTachographFile(vehicleId, id, file)
      showSuccess('Certificaat geüpload.')
      setReloadToken((t) => t + 1)
    } catch {
      showError('Het certificaat kon niet worden geüpload (max. 10 MB, pdf/jpg/png).')
    } finally {
      setUploadingId(null)
    }
  }

  return (
    <section className="fleet-compliance">
      <div className="fleet-compliance-header">
        <h2>Tachograaf</h2>
        {canManage && (
          <Button variant="secondary" onClick={openCreate}>
            IJking toevoegen
          </Button>
        )}
      </div>

      {loadError && <p className="placeholder-text">{loadError}</p>}
      {!loadError && items === null && <p className="placeholder-text">Laden…</p>}
      {!loadError && items !== null && items.length === 0 && (
        <p className="placeholder-text">Nog geen tachograafijkingen geregistreerd.</p>
      )}

      {!loadError && items !== null && items.length > 0 && (
        <table className="fleet-compliance-table">
          <thead>
            <tr>
              <th>Type</th>
              <th>IJkdatum</th>
              <th>Volgende ijking</th>
              <th>Werkplaats</th>
              <th>Status</th>
              <th>Bestand</th>
              <th aria-label="Acties" />
            </tr>
          </thead>
          <tbody>
            {items.map((item) => (
              <tr key={item.id}>
                <td>{item.tachographType ?? '—'}</td>
                <td>{item.calibrationDate}</td>
                <td>{item.nextCalibrationDue}</td>
                <td>{item.workshop ?? '—'}</td>
                <td>
                  <Badge tone={STATUS_TONE[item.status]}>{TACHOGRAPH_STATUS_LABELS[item.status]}</Badge>
                </td>
                <td className="fleet-compliance-file">
                  {item.hasAttachment ? (
                    <button type="button" className="fleet-compliance-link" onClick={() => downloadTachographFile(vehicleId, item.id, item.fileName ?? 'certificaat').catch(() => showError('Downloaden mislukt.'))}>
                      Downloaden
                    </button>
                  ) : (
                    '—'
                  )}
                  {canManage && (
                    <button type="button" className="fleet-compliance-link" onClick={() => pickFile(item.id)} disabled={uploadingId === item.id}>
                      {uploadingId === item.id ? 'Bezig…' : item.hasAttachment ? 'Vervangen' : 'Uploaden'}
                    </button>
                  )}
                </td>
                <td className="fleet-compliance-actions">
                  {canManage && (
                    <button type="button" className="fleet-compliance-link" onClick={() => openEdit(item)}>
                      Bewerken
                    </button>
                  )}
                  {canManage && (
                    <button type="button" className="fleet-compliance-link fleet-compliance-link-danger" onClick={() => setDeleteTarget(item)}>
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
          title={editingId ? 'Tachograafijking bewerken' : 'Tachograafijking toevoegen'}
          onClose={() => setEditorOpen(false)}
          busy={saving}
          footer={
            <>
              <Button variant="secondary" onClick={() => setEditorOpen(false)} disabled={saving}>
                Annuleren
              </Button>
              <Button type="submit" form="tacho-form" disabled={saving}>
                {saving ? 'Opslaan…' : 'Opslaan'}
              </Button>
            </>
          }
        >
          <form id="tacho-form" className="fleet-compliance-form" onSubmit={handleSubmit} noValidate>
            {formError && (
              <div className="fleet-compliance-form-error" role="alert">
                {formError}
              </div>
            )}
            <div className="fleet-compliance-form-row">
              <FormField label="IJkdatum" htmlFor="tf-date" required>
                <input id="tf-date" type="date" value={form.calibrationDate} onChange={(e) => set('calibrationDate', e.target.value)} disabled={saving} />
              </FormField>
              <FormField label="Volgende ijking" htmlFor="tf-due" required>
                <input id="tf-due" type="date" value={form.nextCalibrationDue} onChange={(e) => set('nextCalibrationDue', e.target.value)} disabled={saving} />
              </FormField>
            </div>
            <FormField label="Type" htmlFor="tf-type" hint="Analoog / Digitaal / Smart">
              <input id="tf-type" value={form.tachographType ?? ''} onChange={(e) => set('tachographType', e.target.value || null)} disabled={saving} maxLength={50} />
            </FormField>
            <div className="fleet-compliance-form-row">
              <FormField label="Fabrikant" htmlFor="tf-manu">
                <input id="tf-manu" value={form.manufacturer ?? ''} onChange={(e) => set('manufacturer', e.target.value || null)} disabled={saving} maxLength={100} />
              </FormField>
              <FormField label="Model" htmlFor="tf-model">
                <input id="tf-model" value={form.model ?? ''} onChange={(e) => set('model', e.target.value || null)} disabled={saving} maxLength={100} />
              </FormField>
            </div>
            <div className="fleet-compliance-form-row">
              <FormField label="Serienummer" htmlFor="tf-serial">
                <input id="tf-serial" value={form.serialNumber ?? ''} onChange={(e) => set('serialNumber', e.target.value || null)} disabled={saving} maxLength={100} />
              </FormField>
              <FormField label="Werkplaats" htmlFor="tf-workshop">
                <input id="tf-workshop" value={form.workshop ?? ''} onChange={(e) => set('workshop', e.target.value || null)} disabled={saving} maxLength={150} />
              </FormField>
            </div>
            <div className="fleet-compliance-form-row">
              <FormField label="Certificaatnummer" htmlFor="tf-cert">
                <input id="tf-cert" value={form.certificateNumber ?? ''} onChange={(e) => set('certificateNumber', e.target.value || null)} disabled={saving} maxLength={100} />
              </FormField>
              <FormField label="Zegelreferentie" htmlFor="tf-seal">
                <input id="tf-seal" value={form.sealReference ?? ''} onChange={(e) => set('sealReference', e.target.value || null)} disabled={saving} maxLength={100} />
              </FormField>
            </div>
            <div className="fleet-compliance-form-row">
              <FormField label="Kilometerstand" htmlFor="tf-odo">
                <input id="tf-odo" type="number" min={0} value={form.odometerKm ?? ''} onChange={(e) => set('odometerKm', e.target.value === '' ? null : Number(e.target.value))} disabled={saving} />
              </FormField>
              <FormField label="Bandomtrek (mm)" htmlFor="tf-tyre">
                <input id="tf-tyre" type="number" min={0} value={form.tyreCircumferenceMm ?? ''} onChange={(e) => set('tyreCircumferenceMm', e.target.value === '' ? null : Number(e.target.value))} disabled={saving} />
              </FormField>
            </div>
            <FormField label="Notities" htmlFor="tf-notes">
              <textarea id="tf-notes" rows={2} value={form.notes ?? ''} onChange={(e) => set('notes', e.target.value || null)} disabled={saving} />
            </FormField>
          </form>
        </Modal>
      )}

      {deleteTarget && (
        <ConfirmDialog
          title="IJking verwijderen"
          message="Weet je zeker dat je deze tachograafijking wilt verwijderen?"
          confirmLabel="Verwijderen"
          destructive
          onConfirm={handleDelete}
          onCancel={() => setDeleteTarget(null)}
        />
      )}

      <input ref={fileInputRef} type="file" accept={ACCEPT} className="fleet-compliance-file-input" onChange={handleFileSelected} />
    </section>
  )
}
