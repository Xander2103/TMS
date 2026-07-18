import { useEffect, useState, type FormEvent } from 'react'
import { ApiError } from '../../../api/apiClient'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import {
  cancelAbsence,
  createAbsence,
  decideAbsence,
  deleteAbsence,
  listEmployeeAbsences,
  updateAbsence,
} from '../api/absencesApi'
import {
  ABSENCE_STATUS_LABELS,
  ABSENCE_STATUS_TONE,
  ABSENCE_TYPE_LABELS,
  ABSENCE_TYPES,
  type Absence,
  type AbsenceInput,
  type AbsenceType,
} from '../types'
import './absences.css'

interface AbsenceForm {
  type: AbsenceType
  startDate: string
  endDate: string
  reason: string
}

const EMPTY_FORM: AbsenceForm = {
  type: 'Vacation',
  startDate: '',
  endDate: '',
  reason: '',
}

interface AbsencesTabProps {
  employeeId: string
}

/** Absences for one employee: request, edit while requested, approve/reject, cancel, delete. */
export function AbsencesTab({ employeeId }: AbsencesTabProps) {
  const { showSuccess, showError } = useToast()
  const { hasPermission } = useAuth()

  const [absences, setAbsences] = useState<Absence[] | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [reloadToken, setReloadToken] = useState(0)

  const [editorOpen, setEditorOpen] = useState(false)
  const [editing, setEditing] = useState<Absence | null>(null)
  const [form, setForm] = useState<AbsenceForm>(EMPTY_FORM)
  const [formError, setFormError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)

  const [decideTarget, setDecideTarget] = useState<{ absence: Absence; approve: boolean } | null>(null)
  const [decisionNote, setDecisionNote] = useState('')
  const [cancelTarget, setCancelTarget] = useState<Absence | null>(null)
  const [deleteTarget, setDeleteTarget] = useState<Absence | null>(null)

  useEffect(() => {
    let mounted = true
    listEmployeeAbsences(employeeId)
      .then((data) => {
        if (!mounted) return
        setAbsences(data)
        setLoadError(null)
      })
      .catch(() => {
        if (mounted) setLoadError('Afwezigheden konden niet worden geladen.')
      })
    return () => {
      mounted = false
    }
  }, [employeeId, reloadToken])

  function set<K extends keyof AbsenceForm>(key: K, value: AbsenceForm[K]) {
    setForm((f) => ({ ...f, [key]: value }))
  }

  function openCreate() {
    setEditing(null)
    setForm(EMPTY_FORM)
    setFormError(null)
    setEditorOpen(true)
  }

  function openEdit(absence: Absence) {
    setEditing(absence)
    setForm({
      type: absence.type,
      startDate: absence.startDate,
      endDate: absence.endDate,
      reason: absence.reason ?? '',
    })
    setFormError(null)
    setEditorOpen(true)
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setFormError(null)
    if (!form.startDate || !form.endDate) {
      setFormError('Begin- en einddatum zijn verplicht.')
      return
    }
    if (form.endDate < form.startDate) {
      setFormError('De einddatum moet op of na de begindatum liggen.')
      return
    }
    const input: AbsenceInput = {
      type: form.type,
      startDate: form.startDate,
      endDate: form.endDate,
      reason: form.reason.trim() || null,
    }
    setSaving(true)
    try {
      if (editing) {
        await updateAbsence(editing.id, input)
        showSuccess('Afwezigheid bijgewerkt.')
      } else {
        await createAbsence(employeeId, input)
        showSuccess('Afwezigheid aangevraagd.')
      }
      setEditorOpen(false)
      setReloadToken((t) => t + 1)
    } catch (err) {
      setFormError(
        err instanceof ApiError && err.status === 409
          ? 'De periode overlapt met een bestaande afwezigheid.'
          : 'De afwezigheid kon niet worden opgeslagen.',
      )
    } finally {
      setSaving(false)
    }
  }

  async function handleDecide(event: FormEvent) {
    event.preventDefault()
    if (!decideTarget) return
    setSaving(true)
    try {
      await decideAbsence(decideTarget.absence.id, decideTarget.approve, decisionNote.trim() || null)
      showSuccess(decideTarget.approve ? 'Afwezigheid goedgekeurd.' : 'Afwezigheid afgewezen.')
      setDecideTarget(null)
      setDecisionNote('')
      setReloadToken((t) => t + 1)
    } catch {
      showError('De beslissing kon niet worden opgeslagen.')
    } finally {
      setSaving(false)
    }
  }

  async function handleCancel() {
    if (!cancelTarget) return
    try {
      await cancelAbsence(cancelTarget.id)
      showSuccess('Afwezigheid geannuleerd.')
      setCancelTarget(null)
      setReloadToken((t) => t + 1)
    } catch {
      showError('De afwezigheid kon niet worden geannuleerd.')
      setCancelTarget(null)
    }
  }

  async function handleDelete() {
    if (!deleteTarget) return
    try {
      await deleteAbsence(deleteTarget.id)
      showSuccess('Afwezigheid verwijderd.')
      setDeleteTarget(null)
      setReloadToken((t) => t + 1)
    } catch {
      showError('De afwezigheid kon niet worden verwijderd.')
      setDeleteTarget(null)
    }
  }

  const canEdit = hasPermission('absences.edit')
  const canApprove = hasPermission('absences.approve')

  return (
    <section className="abs">
      <div className="abs-header">
        <h2>Afwezigheden</h2>
        {hasPermission('absences.create') && (
          <Button variant="secondary" onClick={openCreate}>
            Afwezigheid aanvragen
          </Button>
        )}
      </div>

      {loadError && <p className="placeholder-text">{loadError}</p>}
      {!loadError && absences === null && <p className="placeholder-text">Afwezigheden laden…</p>}
      {!loadError && absences !== null && absences.length === 0 && (
        <p className="placeholder-text">Geen afwezigheden geregistreerd.</p>
      )}

      {!loadError && absences !== null && absences.length > 0 && (
        <table className="abs-table">
          <thead>
            <tr>
              <th>Type</th>
              <th>Van</th>
              <th>Tot en met</th>
              <th>Status</th>
              <th>Reden</th>
              <th aria-label="Acties" />
            </tr>
          </thead>
          <tbody>
            {absences.map((absence) => (
              <tr key={absence.id}>
                <td>{ABSENCE_TYPE_LABELS[absence.type]}</td>
                <td>{absence.startDate}</td>
                <td>{absence.endDate}</td>
                <td>
                  <span title={absence.decisionNote ?? undefined}>
                    <Badge tone={ABSENCE_STATUS_TONE[absence.status]}>{ABSENCE_STATUS_LABELS[absence.status]}</Badge>
                  </span>
                </td>
                <td className="abs-reason" title={absence.reason ?? undefined}>
                  {absence.reason ?? '—'}
                </td>
                <td className="abs-actions">
                  {canApprove && absence.status === 'Requested' && (
                    <>
                      <button
                        type="button"
                        className="abs-link"
                        onClick={() => {
                          setDecideTarget({ absence, approve: true })
                          setDecisionNote('')
                        }}
                      >
                        Goedkeuren
                      </button>
                      <button
                        type="button"
                        className="abs-link abs-link-danger"
                        onClick={() => {
                          setDecideTarget({ absence, approve: false })
                          setDecisionNote('')
                        }}
                      >
                        Afwijzen
                      </button>
                    </>
                  )}
                  {canEdit && absence.status === 'Requested' && (
                    <button type="button" className="abs-link" onClick={() => openEdit(absence)}>
                      Bewerken
                    </button>
                  )}
                  {canEdit && (absence.status === 'Requested' || absence.status === 'Approved') && (
                    <button type="button" className="abs-link" onClick={() => setCancelTarget(absence)}>
                      Annuleren
                    </button>
                  )}
                  {hasPermission('absences.delete') && (
                    <button type="button" className="abs-link abs-link-danger" onClick={() => setDeleteTarget(absence)}>
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
          title={editing ? 'Afwezigheid bewerken' : 'Afwezigheid aanvragen'}
          onClose={() => setEditorOpen(false)}
          busy={saving}
          footer={
            <>
              <Button variant="secondary" onClick={() => setEditorOpen(false)} disabled={saving}>
                Annuleren
              </Button>
              <Button type="submit" form="abs-form" disabled={saving}>
                {saving ? 'Opslaan…' : 'Opslaan'}
              </Button>
            </>
          }
        >
          <form id="abs-form" className="abs-form" onSubmit={handleSubmit} noValidate>
            {formError && (
              <div className="abs-form-error" role="alert">
                {formError}
              </div>
            )}
            <FormField label="Type" htmlFor="ab-type" required>
              <select id="ab-type" value={form.type} onChange={(e) => set('type', e.target.value as AbsenceType)} disabled={saving}>
                {ABSENCE_TYPES.map((type) => (
                  <option key={type} value={type}>
                    {ABSENCE_TYPE_LABELS[type]}
                  </option>
                ))}
              </select>
            </FormField>
            <div className="abs-form-row">
              <FormField label="Van" htmlFor="ab-start" required>
                <input id="ab-start" type="date" value={form.startDate} onChange={(e) => set('startDate', e.target.value)} disabled={saving} />
              </FormField>
              <FormField label="Tot en met" htmlFor="ab-end" required>
                <input id="ab-end" type="date" value={form.endDate} onChange={(e) => set('endDate', e.target.value)} disabled={saving} />
              </FormField>
            </div>
            <FormField label="Reden" htmlFor="ab-reason">
              <textarea id="ab-reason" rows={2} value={form.reason} onChange={(e) => set('reason', e.target.value)} disabled={saving} maxLength={1000} />
            </FormField>
          </form>
        </Modal>
      )}

      {decideTarget && (
        <Modal
          title={decideTarget.approve ? 'Afwezigheid goedkeuren' : 'Afwezigheid afwijzen'}
          onClose={() => setDecideTarget(null)}
          busy={saving}
          footer={
            <>
              <Button variant="secondary" onClick={() => setDecideTarget(null)} disabled={saving}>
                Annuleren
              </Button>
              <Button type="submit" form="abs-decide-form" disabled={saving}>
                {saving ? 'Bezig…' : decideTarget.approve ? 'Goedkeuren' : 'Afwijzen'}
              </Button>
            </>
          }
        >
          <form id="abs-decide-form" className="abs-form" onSubmit={handleDecide} noValidate>
            <p className="abs-decide-text">
              {ABSENCE_TYPE_LABELS[decideTarget.absence.type]} van {decideTarget.absence.startDate} tot en met{' '}
              {decideTarget.absence.endDate}.
            </p>
            <FormField label="Opmerking" htmlFor="ab-note">
              <input id="ab-note" value={decisionNote} onChange={(e) => setDecisionNote(e.target.value)} disabled={saving} maxLength={1000} />
            </FormField>
          </form>
        </Modal>
      )}

      {cancelTarget && (
        <ConfirmDialog
          title="Afwezigheid annuleren"
          message={`${ABSENCE_TYPE_LABELS[cancelTarget.type]} van ${cancelTarget.startDate} tot en met ${cancelTarget.endDate} annuleren?`}
          confirmLabel="Annuleren"
          destructive
          onConfirm={handleCancel}
          onCancel={() => setCancelTarget(null)}
        />
      )}

      {deleteTarget && (
        <ConfirmDialog
          title="Afwezigheid verwijderen"
          message={`${ABSENCE_TYPE_LABELS[deleteTarget.type]} van ${deleteTarget.startDate} tot en met ${deleteTarget.endDate} verwijderen?`}
          confirmLabel="Verwijderen"
          destructive
          onConfirm={handleDelete}
          onCancel={() => setDeleteTarget(null)}
        />
      )}
    </section>
  )
}
