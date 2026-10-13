import { useEffect, useRef, useState, type ChangeEvent, type FormEvent } from 'react'
import { Badge, type BadgeTone } from '../../components/ui/Badge'
import { Button } from '../../components/ui/Button'
import { ConfirmDialog } from '../../components/ui/ConfirmDialog'
import { FormField } from '../../components/ui/FormField'
import { Modal } from '../../components/ui/Modal'
import { useToast } from '../../components/ui/toastContext'
import { useLocale } from '../../i18n/localeContext'
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
  const { t } = useLocale()
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
        if (mounted) setLoadError(t('fleet.tachograph.loadFailed'))
      })
    return () => {
      mounted = false
    }
  }, [vehicleId, reloadToken, t])

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
      setFormError(t('fleet.tachograph.datesRequired'))
      return
    }
    setSaving(true)
    try {
      if (editingId) {
        await updateTachographCalibration(vehicleId, editingId, form)
        showSuccess(t('fleet.tachograph.updated'))
      } else {
        await createTachographCalibration(vehicleId, form)
        showSuccess(t('fleet.tachograph.created'))
      }
      setEditorOpen(false)
      setReloadToken((token) => token + 1)
    } catch {
      setFormError(t('fleet.tachograph.saveFailed'))
    } finally {
      setSaving(false)
    }
  }

  async function handleDelete() {
    if (!deleteTarget) return
    try {
      await deleteTachographCalibration(vehicleId, deleteTarget.id)
      showSuccess(t('fleet.tachograph.deleted'))
      setDeleteTarget(null)
      setReloadToken((token) => token + 1)
    } catch {
      showError(t('fleet.tachograph.deleteFailed'))
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
      showSuccess(t('fleet.tachograph.certUploaded'))
      setReloadToken((token) => token + 1)
    } catch {
      showError(t('fleet.tachograph.certUploadFailed'))
    } finally {
      setUploadingId(null)
    }
  }

  return (
    <section className="fleet-compliance">
      <div className="fleet-compliance-header">
        <h2>{t('fleet.tachograph.title')}</h2>
        {canManage && (
          <Button variant="secondary" onClick={openCreate}>
            {t('fleet.tachograph.add')}
          </Button>
        )}
      </div>

      {loadError && <p className="placeholder-text">{loadError}</p>}
      {!loadError && items === null && <p className="placeholder-text">{t('fleet.common.loading')}</p>}
      {!loadError && items !== null && items.length === 0 && (
        <p className="placeholder-text">{t('fleet.tachograph.empty')}</p>
      )}

      {!loadError && items !== null && items.length > 0 && (
        <table className="fleet-compliance-table">
          <thead>
            <tr>
              <th>{t('fleet.tachograph.colType')}</th>
              <th>{t('fleet.tachograph.colDate')}</th>
              <th>{t('fleet.tachograph.colNext')}</th>
              <th>{t('fleet.tachograph.colWorkshop')}</th>
              <th>{t('fleet.tachograph.colStatus')}</th>
              <th>{t('fleet.tachograph.colFile')}</th>
              <th aria-label={t('fleet.common.actions')} />
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
                  <Badge tone={STATUS_TONE[item.status]}>{t(TACHOGRAPH_STATUS_LABELS[item.status])}</Badge>
                </td>
                <td className="fleet-compliance-file">
                  {item.hasAttachment ? (
                    <button type="button" className="fleet-compliance-link" onClick={() => downloadTachographFile(vehicleId, item.id, item.fileName ?? 'certificaat').catch(() => showError(t('fleet.common.downloadFailed')))}>
                      {t('fleet.common.download')}
                    </button>
                  ) : (
                    '—'
                  )}
                  {canManage && (
                    <button type="button" className="fleet-compliance-link" onClick={() => pickFile(item.id)} disabled={uploadingId === item.id}>
                      {uploadingId === item.id ? t('fleet.common.busy') : item.hasAttachment ? t('fleet.common.replace') : t('fleet.common.upload')}
                    </button>
                  )}
                </td>
                <td className="fleet-compliance-actions">
                  {canManage && (
                    <button type="button" className="fleet-compliance-link" onClick={() => openEdit(item)}>
                      {t('ui.actions.edit')}
                    </button>
                  )}
                  {canManage && (
                    <button type="button" className="fleet-compliance-link fleet-compliance-link-danger" onClick={() => setDeleteTarget(item)}>
                      {t('ui.actions.delete')}
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
          title={editingId ? t('fleet.tachograph.editTitle') : t('fleet.tachograph.addTitle')}
          onClose={() => setEditorOpen(false)}
          busy={saving}
          footer={
            <>
              <Button variant="secondary" onClick={() => setEditorOpen(false)} disabled={saving}>
                {t('ui.actions.cancel')}
              </Button>
              <Button type="submit" form="tacho-form" disabled={saving}>
                {saving ? t('fleet.common.saving') : t('ui.actions.save')}
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
              <FormField label={t('fleet.tachograph.date')} htmlFor="tf-date" required>
                <input id="tf-date" type="date" value={form.calibrationDate} onChange={(e) => set('calibrationDate', e.target.value)} disabled={saving} />
              </FormField>
              <FormField label={t('fleet.tachograph.nextDue')} htmlFor="tf-due" required>
                <input id="tf-due" type="date" value={form.nextCalibrationDue} onChange={(e) => set('nextCalibrationDue', e.target.value)} disabled={saving} />
              </FormField>
            </div>
            <FormField label={t('fleet.tachograph.type')} htmlFor="tf-type" hint={t('fleet.tachograph.typeHint')}>
              <input id="tf-type" value={form.tachographType ?? ''} onChange={(e) => set('tachographType', e.target.value || null)} disabled={saving} maxLength={50} />
            </FormField>
            <div className="fleet-compliance-form-row">
              <FormField label={t('fleet.tachograph.manufacturer')} htmlFor="tf-manu">
                <input id="tf-manu" value={form.manufacturer ?? ''} onChange={(e) => set('manufacturer', e.target.value || null)} disabled={saving} maxLength={100} />
              </FormField>
              <FormField label={t('fleet.tachograph.model')} htmlFor="tf-model">
                <input id="tf-model" value={form.model ?? ''} onChange={(e) => set('model', e.target.value || null)} disabled={saving} maxLength={100} />
              </FormField>
            </div>
            <div className="fleet-compliance-form-row">
              <FormField label={t('fleet.tachograph.serialNumber')} htmlFor="tf-serial">
                <input id="tf-serial" value={form.serialNumber ?? ''} onChange={(e) => set('serialNumber', e.target.value || null)} disabled={saving} maxLength={100} />
              </FormField>
              <FormField label={t('fleet.tachograph.workshop')} htmlFor="tf-workshop">
                <input id="tf-workshop" value={form.workshop ?? ''} onChange={(e) => set('workshop', e.target.value || null)} disabled={saving} maxLength={150} />
              </FormField>
            </div>
            <div className="fleet-compliance-form-row">
              <FormField label={t('fleet.tachograph.certificateNumber')} htmlFor="tf-cert">
                <input id="tf-cert" value={form.certificateNumber ?? ''} onChange={(e) => set('certificateNumber', e.target.value || null)} disabled={saving} maxLength={100} />
              </FormField>
              <FormField label={t('fleet.tachograph.sealReference')} htmlFor="tf-seal">
                <input id="tf-seal" value={form.sealReference ?? ''} onChange={(e) => set('sealReference', e.target.value || null)} disabled={saving} maxLength={100} />
              </FormField>
            </div>
            <div className="fleet-compliance-form-row">
              <FormField label={t('fleet.tachograph.odometer')} htmlFor="tf-odo">
                <input id="tf-odo" type="number" min={0} value={form.odometerKm ?? ''} onChange={(e) => set('odometerKm', e.target.value === '' ? null : Number(e.target.value))} disabled={saving} />
              </FormField>
              <FormField label={t('fleet.tachograph.tyreCircumference')} htmlFor="tf-tyre">
                <input id="tf-tyre" type="number" min={0} value={form.tyreCircumferenceMm ?? ''} onChange={(e) => set('tyreCircumferenceMm', e.target.value === '' ? null : Number(e.target.value))} disabled={saving} />
              </FormField>
            </div>
            <FormField label={t('fleet.tachograph.notes')} htmlFor="tf-notes">
              <textarea id="tf-notes" rows={2} value={form.notes ?? ''} onChange={(e) => set('notes', e.target.value || null)} disabled={saving} />
            </FormField>
          </form>
        </Modal>
      )}

      {deleteTarget && (
        <ConfirmDialog
          title={t('fleet.tachograph.deleteTitle')}
          message={t('fleet.tachograph.deleteMessage')}
          confirmLabel={t('ui.actions.delete')}
          destructive
          onConfirm={handleDelete}
          onCancel={() => setDeleteTarget(null)}
        />
      )}

      <input ref={fileInputRef} type="file" accept={ACCEPT} className="fleet-compliance-file-input" onChange={handleFileSelected} />
    </section>
  )
}
