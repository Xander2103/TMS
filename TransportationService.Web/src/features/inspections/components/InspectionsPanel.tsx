import { useEffect, useState, type FormEvent } from 'react'
import { Badge, type BadgeTone } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import {
  completeInspection,
  createInspection,
  deleteInspection,
  listInspections,
  updateInspection,
  type InspectionOwnerType,
} from '../api/inspectionsApi'
import {
  INSPECTION_RESULT_LABELS,
  INSPECTION_TYPE_LABELS,
  INSPECTION_TYPES,
  INSPECTION_URGENCY_LABELS,
  inspectionDisplayName,
  type CompleteInspectionInput,
  type Inspection,
  type InspectionInput,
  type InspectionResult,
  type InspectionType,
} from '../types'
import './inspections.css'

const URGENCY_TONE: Record<Inspection['urgency'], BadgeTone> = {
  Ok: 'info',
  DueSoon: 'warning',
  Overdue: 'danger',
  Completed: 'success',
}

const RESULT_TONE: Record<InspectionResult, BadgeTone> = {
  Passed: 'success',
  PassedWithRemarks: 'warning',
  Failed: 'danger',
}

const EMPTY_FORM: InspectionInput = {
  inspectionType: 'VehicleInspection',
  customTypeName: null,
  dueDate: '',
  intervalMonths: null,
  warningDays: null,
  notes: null,
}

interface InspectionsPanelProps {
  ownerType: InspectionOwnerType
  ownerId: string
}

/** Inspections section for a vehicle or trailer detail page: plan, edit, register result, delete. */
export function InspectionsPanel({ ownerType, ownerId }: InspectionsPanelProps) {
  const { showSuccess, showError } = useToast()
  const { hasPermission } = useAuth()

  const [inspections, setInspections] = useState<Inspection[] | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [reloadToken, setReloadToken] = useState(0)

  const [editorOpen, setEditorOpen] = useState(false)
  const [editing, setEditing] = useState<Inspection | null>(null)
  const [form, setForm] = useState<InspectionInput>(EMPTY_FORM)
  const [formError, setFormError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)

  const [completeTarget, setCompleteTarget] = useState<Inspection | null>(null)
  const [completeForm, setCompleteForm] = useState<CompleteInspectionInput | null>(null)
  const [deleteTarget, setDeleteTarget] = useState<Inspection | null>(null)

  useEffect(() => {
    let mounted = true
    listInspections(ownerType, ownerId)
      .then((data) => {
        if (!mounted) return
        setInspections(data)
        setLoadError(null)
      })
      .catch(() => {
        if (mounted) setLoadError('Keuringen konden niet worden geladen.')
      })
    return () => {
      mounted = false
    }
  }, [ownerType, ownerId, reloadToken])

  function set<K extends keyof InspectionInput>(key: K, value: InspectionInput[K]) {
    setForm((f) => ({ ...f, [key]: value }))
  }

  function openCreate() {
    setEditing(null)
    setForm({
      ...EMPTY_FORM,
      inspectionType: ownerType === 'trailer' ? 'TrailerInspection' : 'VehicleInspection',
    })
    setFormError(null)
    setEditorOpen(true)
  }

  function openEdit(inspection: Inspection) {
    setEditing(inspection)
    setForm({
      inspectionType: inspection.inspectionType,
      customTypeName: inspection.customTypeName,
      dueDate: inspection.dueDate,
      intervalMonths: inspection.intervalMonths,
      warningDays: inspection.warningDays,
      notes: inspection.notes,
    })
    setFormError(null)
    setEditorOpen(true)
  }

  function openComplete(inspection: Inspection) {
    setCompleteTarget(inspection)
    setCompleteForm({
      completedDate: new Date().toISOString().slice(0, 10),
      result: 'Passed',
      notes: null,
    })
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setFormError(null)
    if (!form.dueDate) {
      setFormError('Een vervaldatum is verplicht.')
      return
    }
    if (form.inspectionType === 'Other' && !form.customTypeName?.trim()) {
      setFormError('Geef een naam op voor het aangepaste keuringstype.')
      return
    }
    setSaving(true)
    try {
      if (editing) {
        await updateInspection(editing.id, form)
        showSuccess('Keuring bijgewerkt.')
      } else {
        await createInspection(ownerType, ownerId, form)
        showSuccess('Keuring gepland.')
      }
      setEditorOpen(false)
      setReloadToken((t) => t + 1)
    } catch {
      setFormError('De keuring kon niet worden opgeslagen.')
    } finally {
      setSaving(false)
    }
  }

  async function handleComplete(event: FormEvent) {
    event.preventDefault()
    if (!completeTarget || !completeForm) return
    setSaving(true)
    try {
      const result = await completeInspection(completeTarget.id, completeForm)
      showSuccess(
        result.followUp
          ? `Keuring geregistreerd — volgende keuring gepland op ${result.followUp.dueDate}.`
          : 'Keuring geregistreerd.',
      )
      setCompleteTarget(null)
      setCompleteForm(null)
      setReloadToken((t) => t + 1)
    } catch {
      showError('De keuring kon niet worden geregistreerd.')
    } finally {
      setSaving(false)
    }
  }

  async function handleDelete() {
    if (!deleteTarget) return
    try {
      await deleteInspection(deleteTarget.id)
      showSuccess('Keuring verwijderd.')
      setDeleteTarget(null)
      setReloadToken((t) => t + 1)
    } catch {
      showError('De keuring kon niet worden verwijderd.')
      setDeleteTarget(null)
    }
  }

  const canEdit = hasPermission('inspections.edit')

  return (
    <section className="insp">
      <div className="insp-header">
        <h2>Keuringen</h2>
        {hasPermission('inspections.create') && (
          <Button variant="secondary" onClick={openCreate}>
            Keuring plannen
          </Button>
        )}
      </div>

      {loadError && <p className="placeholder-text">{loadError}</p>}
      {!loadError && inspections === null && <p className="placeholder-text">Keuringen laden…</p>}
      {!loadError && inspections !== null && inspections.length === 0 && (
        <p className="placeholder-text">Nog geen keuringen geregistreerd.</p>
      )}

      {!loadError && inspections !== null && inspections.length > 0 && (
        <table className="insp-table">
          <thead>
            <tr>
              <th>Keuring</th>
              <th>Vervaldatum</th>
              <th>Status</th>
              <th>Uitgevoerd</th>
              <th>Resultaat</th>
              <th aria-label="Acties" />
            </tr>
          </thead>
          <tbody>
            {inspections.map((inspection) => (
              <tr key={inspection.id}>
                <td>{inspectionDisplayName(inspection)}</td>
                <td>{inspection.dueDate}</td>
                <td>
                  <Badge tone={URGENCY_TONE[inspection.urgency]}>{INSPECTION_URGENCY_LABELS[inspection.urgency]}</Badge>
                </td>
                <td>{inspection.completedDate ?? '—'}</td>
                <td>
                  {inspection.result ? (
                    <Badge tone={RESULT_TONE[inspection.result]}>{INSPECTION_RESULT_LABELS[inspection.result]}</Badge>
                  ) : (
                    '—'
                  )}
                </td>
                <td className="insp-actions">
                  {canEdit && inspection.completedDate === null && (
                    <>
                      <button type="button" className="insp-link" onClick={() => openComplete(inspection)}>
                        Registreren
                      </button>
                      <button type="button" className="insp-link" onClick={() => openEdit(inspection)}>
                        Bewerken
                      </button>
                    </>
                  )}
                  {hasPermission('inspections.delete') && (
                    <button type="button" className="insp-link insp-link-danger" onClick={() => setDeleteTarget(inspection)}>
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
          title={editing ? 'Keuring bewerken' : 'Keuring plannen'}
          onClose={() => setEditorOpen(false)}
          busy={saving}
          footer={
            <>
              <Button variant="secondary" onClick={() => setEditorOpen(false)} disabled={saving}>
                Annuleren
              </Button>
              <Button type="submit" form="insp-form" disabled={saving}>
                {saving ? 'Opslaan…' : 'Opslaan'}
              </Button>
            </>
          }
        >
          <form id="insp-form" className="insp-form" onSubmit={handleSubmit} noValidate>
            {formError && (
              <div className="insp-form-error" role="alert">
                {formError}
              </div>
            )}
            <FormField label="Keuringstype" htmlFor="in-type" required>
              <select
                id="in-type"
                value={form.inspectionType}
                onChange={(e) => set('inspectionType', e.target.value as InspectionType)}
                disabled={saving}
              >
                {INSPECTION_TYPES.map((type) => (
                  <option key={type} value={type}>
                    {INSPECTION_TYPE_LABELS[type]}
                  </option>
                ))}
              </select>
            </FormField>
            {form.inspectionType === 'Other' && (
              <FormField label="Naam keuringstype" htmlFor="in-custom" required>
                <input
                  id="in-custom"
                  value={form.customTypeName ?? ''}
                  onChange={(e) => set('customTypeName', e.target.value || null)}
                  disabled={saving}
                  maxLength={100}
                />
              </FormField>
            )}
            <div className="insp-form-row">
              <FormField label="Vervaldatum" htmlFor="in-due" required>
                <input
                  id="in-due"
                  type="date"
                  value={form.dueDate}
                  onChange={(e) => set('dueDate', e.target.value)}
                  disabled={saving}
                />
              </FormField>
              <FormField
                label="Herhaal elke (maanden)"
                htmlFor="in-interval"
                hint={form.inspectionType === 'CraneInspection' ? 'Leeg = standaard 3 maanden' : 'Leeg = eenmalig'}
              >
                <input
                  id="in-interval"
                  type="number"
                  min={1}
                  max={120}
                  value={form.intervalMonths ?? ''}
                  onChange={(e) => set('intervalMonths', e.target.value === '' ? null : Number(e.target.value))}
                  disabled={saving}
                />
              </FormField>
            </div>
            <FormField label="Waarschuwing (dagen vóór verval)" htmlFor="in-warning" hint="Leeg = standaard 30 dagen">
              <input
                id="in-warning"
                type="number"
                min={0}
                max={365}
                value={form.warningDays ?? ''}
                onChange={(e) => set('warningDays', e.target.value === '' ? null : Number(e.target.value))}
                disabled={saving}
              />
            </FormField>
            <FormField label="Notities" htmlFor="in-notes">
              <textarea
                id="in-notes"
                rows={2}
                value={form.notes ?? ''}
                onChange={(e) => set('notes', e.target.value || null)}
                disabled={saving}
              />
            </FormField>
          </form>
        </Modal>
      )}

      {completeTarget && completeForm && (
        <Modal
          title="Keuring registreren"
          onClose={() => setCompleteTarget(null)}
          busy={saving}
          footer={
            <>
              <Button variant="secondary" onClick={() => setCompleteTarget(null)} disabled={saving}>
                Annuleren
              </Button>
              <Button type="submit" form="insp-complete-form" disabled={saving}>
                {saving ? 'Bezig…' : 'Registreren'}
              </Button>
            </>
          }
        >
          <form id="insp-complete-form" className="insp-form" onSubmit={handleComplete} noValidate>
            <FormField label="Datum uitgevoerd" htmlFor="ic-date" required>
              <input
                id="ic-date"
                type="date"
                value={completeForm.completedDate}
                onChange={(e) => setCompleteForm((f) => (f ? { ...f, completedDate: e.target.value } : f))}
                disabled={saving}
              />
            </FormField>
            <FormField label="Resultaat" htmlFor="ic-result" required>
              <select
                id="ic-result"
                value={completeForm.result}
                onChange={(e) => setCompleteForm((f) => (f ? { ...f, result: e.target.value as InspectionResult } : f))}
                disabled={saving}
              >
                {(Object.keys(INSPECTION_RESULT_LABELS) as InspectionResult[]).map((result) => (
                  <option key={result} value={result}>
                    {INSPECTION_RESULT_LABELS[result]}
                  </option>
                ))}
              </select>
            </FormField>
            <FormField label="Notities" htmlFor="ic-notes">
              <textarea
                id="ic-notes"
                rows={2}
                value={completeForm.notes ?? ''}
                onChange={(e) => setCompleteForm((f) => (f ? { ...f, notes: e.target.value || null } : f))}
                disabled={saving}
              />
            </FormField>
          </form>
        </Modal>
      )}

      {deleteTarget && (
        <ConfirmDialog
          title="Keuring verwijderen"
          message={`Weet je zeker dat je "${inspectionDisplayName(deleteTarget)}" (vervaldatum ${deleteTarget.dueDate}) wilt verwijderen?`}
          confirmLabel="Verwijderen"
          destructive
          onConfirm={handleDelete}
          onCancel={() => setDeleteTarget(null)}
        />
      )}
    </section>
  )
}
