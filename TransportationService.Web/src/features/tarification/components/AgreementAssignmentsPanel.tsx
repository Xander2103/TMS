import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { EmptyState } from '../../../components/ui/EmptyState'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { SearchableSelect, type SearchableSelectOption } from '../../../components/ui/SearchableSelect'
import { useToast } from '../../../components/ui/toastContext'
import { describeApiError } from '../../../api/problemDetails'
import { searchCustomers } from '../../customers/api/customersApi'
import {
  getAgreementAssignments,
  saveAgreementAssignments,
  type PricingAssignment,
  type PricingAssignmentInput,
} from '../api/pricingApi'

interface AgreementAssignmentsPanelProps {
  agreementId: string
  /** Assignments only work on shared/reusable tables — the backend refuses them otherwise. */
  isShared: boolean
  canManage: boolean
}

interface AssignmentDraft {
  customerId: string
  mode: 'none' | 'percent' | 'fixed'
  value: string
  effectiveFrom: string
  effectiveUntil: string
  notes: string
}

function adjustmentLabel(assignment: PricingAssignment): string {
  if (assignment.percentAdjustment !== null) {
    return `${assignment.percentAdjustment > 0 ? '+' : ''}${assignment.percentAdjustment}%`
  }
  if (assignment.fixedAdjustment !== null) {
    return `${assignment.fixedAdjustment > 0 ? '+' : ''}€ ${assignment.fixedAdjustment.toFixed(2)}`
  }
  return '—'
}

/** "Klanten" tab: which customers a shared rate table is assigned to, with an optional per-customer ±% or fixed adjustment on top of the table's own prices. */
export function AgreementAssignmentsPanel({ agreementId, isShared, canManage }: AgreementAssignmentsPanelProps) {
  const { showSuccess, showError } = useToast()
  const [assignments, setAssignments] = useState<PricingAssignment[] | null>(null)
  const [customerOptions, setCustomerOptions] = useState<SearchableSelectOption[]>([])
  const [draft, setDraft] = useState<AssignmentDraft | null>(null)
  const [deleteTarget, setDeleteTarget] = useState<PricingAssignment | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const reload = useCallback(() => {
    if (!isShared) return
    getAgreementAssignments(agreementId)
      .then(setAssignments)
      .catch(() => setAssignments([]))
  }, [agreementId, isShared])

  useEffect(() => {
    reload()
  }, [reload])

  useEffect(() => {
    if (!isShared) return
    searchCustomers({ isActive: true, page: 1, pageSize: 200 })
      .then((result) => setCustomerOptions(result.items.map((c) => ({ value: c.id, label: c.name }))))
      .catch(() => setCustomerOptions([]))
  }, [isShared])

  if (!isShared) {
    return <EmptyState message="Klantkoppelingen zijn alleen mogelijk op herbruikbare (gedeelde) tabellen." />
  }

  if (assignments === null) return <p className="placeholder-text">Klantkoppelingen laden…</p>

  function asInputs(): PricingAssignmentInput[] {
    return (assignments ?? []).map((a) => ({
      customerId: a.customerId,
      percentAdjustment: a.percentAdjustment,
      fixedAdjustment: a.fixedAdjustment,
      effectiveFrom: a.effectiveFrom,
      effectiveUntil: a.effectiveUntil,
      notes: a.notes,
    }))
  }

  async function persist(next: PricingAssignmentInput[], message?: string): Promise<boolean> {
    try {
      const saved = await saveAgreementAssignments(agreementId, next)
      setAssignments(saved)
      if (message) showSuccess(message)
      return true
    } catch (err) {
      const described = describeApiError(err, 'De klantkoppelingen konden niet worden opgeslagen.')
      showError(described.message)
      setError(described.message)
      return false
    }
  }

  async function submitDraft(event: FormEvent) {
    event.preventDefault()
    if (!draft) return
    if (!draft.customerId) {
      setError('Kies een klant.')
      return
    }

    setBusy(true)
    setError(null)
    const ok = await persist(
      [
        ...asInputs(),
        {
          customerId: draft.customerId,
          percentAdjustment: draft.mode === 'percent' ? Number(draft.value) || 0 : null,
          fixedAdjustment: draft.mode === 'fixed' ? Number(draft.value) || 0 : null,
          effectiveFrom: draft.effectiveFrom || null,
          effectiveUntil: draft.effectiveUntil || null,
          notes: draft.notes.trim() || null,
        },
      ],
      'Klant gekoppeld.',
    )
    setBusy(false)
    if (ok) setDraft(null)
  }

  async function handleDelete() {
    if (!deleteTarget) return
    const target = deleteTarget
    setDeleteTarget(null)
    await persist(asInputs().filter((a) => a.customerId !== target.customerId), 'Klantkoppeling verwijderd.')
  }

  return (
    <section className="customer-panel">
      <div className="customer-panel-header">
        <h3>Klantkoppelingen</h3>
        {canManage && (
          <Button
            onClick={() => {
              setError(null)
              setDraft({ customerId: '', mode: 'none', value: '', effectiveFrom: '', effectiveUntil: '', notes: '' })
            }}
          >
            + Klant koppelen
          </Button>
        )}
      </div>

      {assignments.length === 0 && <EmptyState message="Nog geen klanten gekoppeld aan deze tabel." />}
      {assignments.length > 0 && (
        <table className="issued-items-table">
          <thead>
            <tr>
              <th>Klant</th>
              <th>Aanpassing</th>
              <th>Geldig van</th>
              <th>Geldig tot</th>
              <th>Notities</th>
              {canManage && <th aria-label="Acties" />}
            </tr>
          </thead>
          <tbody>
            {assignments.map((a) => (
              <tr key={a.id}>
                <td>{a.customerName}</td>
                <td>{adjustmentLabel(a)}</td>
                <td>{a.effectiveFrom ?? '—'}</td>
                <td>{a.effectiveUntil ?? '—'}</td>
                <td>{a.notes ?? '—'}</td>
                {canManage && (
                  <td className="issued-items-row-actions">
                    <button
                      type="button"
                      className="issued-items-link issued-items-link-danger"
                      onClick={() => setDeleteTarget(a)}
                    >
                      Verwijderen
                    </button>
                  </td>
                )}
              </tr>
            ))}
          </tbody>
        </table>
      )}

      {draft && (
        <Modal
          title="Klant koppelen"
          onClose={() => setDraft(null)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setDraft(null)} disabled={busy}>
                Annuleren
              </Button>
              <Button type="submit" form="assignment-form" disabled={busy}>
                Opslaan
              </Button>
            </>
          }
        >
          <form id="assignment-form" className="issued-items-form" onSubmit={submitDraft} noValidate>
            {error && (
              <div className="issued-items-form-error" role="alert">
                {error}
              </div>
            )}
            <FormField label="Klant" htmlFor="assignment-customer" required>
              <SearchableSelect
                id="assignment-customer"
                value={draft.customerId || null}
                onChange={(v) => setDraft((d) => (d ? { ...d, customerId: v ?? '' } : d))}
                options={customerOptions}
              />
            </FormField>
            <FormField label="Aanpassing" htmlFor="assignment-mode">
              <select
                id="assignment-mode"
                value={draft.mode}
                onChange={(e) => setDraft((d) => (d ? { ...d, mode: e.target.value as AssignmentDraft['mode'] } : d))}
              >
                <option value="none">Geen</option>
                <option value="percent">Percentage</option>
                <option value="fixed">Vast bedrag</option>
              </select>
            </FormField>
            {draft.mode !== 'none' && (
              <FormField label="Waarde" htmlFor="assignment-value">
                <input
                  id="assignment-value"
                  type="number"
                  step="0.01"
                  value={draft.value}
                  onChange={(e) => setDraft((d) => (d ? { ...d, value: e.target.value } : d))}
                />
              </FormField>
            )}
            <div className="issued-items-form-row">
              <FormField label="Geldig van" htmlFor="assignment-from" hint="Leeg = altijd.">
                <input
                  id="assignment-from"
                  type="date"
                  value={draft.effectiveFrom}
                  onChange={(e) => setDraft((d) => (d ? { ...d, effectiveFrom: e.target.value } : d))}
                />
              </FormField>
              <FormField label="Geldig tot" htmlFor="assignment-until" hint="Leeg = onbeperkt.">
                <input
                  id="assignment-until"
                  type="date"
                  value={draft.effectiveUntil}
                  onChange={(e) => setDraft((d) => (d ? { ...d, effectiveUntil: e.target.value } : d))}
                />
              </FormField>
            </div>
            <FormField label="Notities" htmlFor="assignment-notes">
              <input
                id="assignment-notes"
                value={draft.notes}
                onChange={(e) => setDraft((d) => (d ? { ...d, notes: e.target.value } : d))}
                maxLength={2000}
              />
            </FormField>
          </form>
        </Modal>
      )}

      {deleteTarget && (
        <ConfirmDialog
          title="Klantkoppeling verwijderen"
          message={`Koppeling met "${deleteTarget.customerName}" verwijderen?`}
          confirmLabel="Verwijderen"
          destructive
          onConfirm={handleDelete}
          onCancel={() => setDeleteTarget(null)}
        />
      )}
    </section>
  )
}
