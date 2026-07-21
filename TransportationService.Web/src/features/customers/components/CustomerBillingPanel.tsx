import { useCallback, useEffect, useMemo, useState, type FormEvent } from 'react'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { DataTable, type Column } from '../../../components/ui/DataTable'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { useToast } from '../../../components/ui/toastContext'
import { describeApiError, getFieldError, type FieldErrors } from '../../../api/problemDetails'
import { useAuth } from '../../auth/authContextValue'
import {
  addPoNumber,
  deletePoNumber,
  getDieselSurcharge,
  getPoPolicy,
  saveDieselSurcharge,
  setPoPolicy,
  updatePoNumber,
} from '../api/customerBillingConfigApi'
import { parsePercent, validateSurchargeForm, type SurchargeFormErrors } from '../utils/billingConfig'
import {
  DIESEL_BASIS_LABELS,
  DIESEL_PRESENTATION_LABELS,
  DIESEL_ROUNDING_LABELS,
  PO_POLICY_LABELS,
  type CustomerDieselSurcharge,
  type CustomerPoNumber,
  type CustomerPoPolicy,
  type DieselSurchargeBasis,
  type DieselSurchargePresentation,
  type DieselSurchargeRounding,
  type PurchaseOrderPolicy,
  type SaveCustomerPoNumberInput,
} from '../types'
import './customerBillingPanel.css'

interface CustomerBillingPanelProps {
  customerId: string
}

/** Facturatie-instellingen van een klant: dieseltoeslag en PO-beleid met historiek. */
export function CustomerBillingPanel({ customerId }: CustomerBillingPanelProps) {
  return (
    <div className="customer-billing">
      <DieselSurchargeSection customerId={customerId} />
      <PoPolicySection customerId={customerId} />
    </div>
  )
}

// --- Dieseltoeslag ---

function DieselSurchargeSection({ customerId }: { customerId: string }) {
  const toast = useToast()
  const { hasPermission } = useAuth()
  const canManage = hasPermission('customers.manage_surcharge')

  const [loading, setLoading] = useState(true)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const [enabled, setEnabled] = useState(false)
  const [percent, setPercent] = useState('')
  const [basis, setBasis] = useState<DieselSurchargeBasis>('OrderAmount')
  const [presentation, setPresentation] = useState<DieselSurchargePresentation>('PerOrderLine')
  const [rounding, setRounding] = useState<DieselSurchargeRounding>('NearestCent')
  const [formulaDescription, setFormulaDescription] = useState('')
  const [effectiveFrom, setEffectiveFrom] = useState('')
  const [effectiveUntil, setEffectiveUntil] = useState('')
  const [errors, setErrors] = useState<SurchargeFormErrors>({})

  useEffect(() => {
    let mounted = true
    getDieselSurcharge(customerId)
      .then((data) => {
        if (!mounted) return
        setEnabled(data.enabled)
        setPercent(data.percent ? String(data.percent) : '')
        setBasis(data.basis)
        setPresentation(data.presentation)
        setRounding(data.rounding)
        setFormulaDescription(data.formulaDescription ?? '')
        setEffectiveFrom(data.effectiveFrom ?? '')
        setEffectiveUntil(data.effectiveUntil ?? '')
        setLoadError(null)
      })
      .catch(() => {
        if (mounted) setLoadError('De dieseltoeslag kon niet worden geladen.')
      })
      .finally(() => {
        if (mounted) setLoading(false)
      })
    return () => {
      mounted = false
    }
  }, [customerId])

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    const validation = validateSurchargeForm({ percent, effectiveFrom, effectiveUntil })
    setErrors(validation)
    if (validation.percent || validation.effectiveUntil) return

    const payload: CustomerDieselSurcharge = {
      enabled,
      percent: parsePercent(percent) ?? 0,
      basis,
      presentation,
      rounding,
      formulaDescription: formulaDescription.trim() || null,
      effectiveFrom: effectiveFrom || null,
      effectiveUntil: effectiveUntil || null,
    }
    setBusy(true)
    try {
      const saved = await saveDieselSurcharge(customerId, payload)
      setEnabled(saved.enabled)
      setPercent(saved.percent ? String(saved.percent) : '')
      toast.showSuccess('Dieseltoeslag opgeslagen.')
    } catch (err) {
      toast.showError(describeApiError(err, 'De dieseltoeslag kon niet worden opgeslagen.').message)
    } finally {
      setBusy(false)
    }
  }

  const disabled = !canManage || busy

  return (
    <section className="customer-billing-section">
      <h3>Dieseltoeslag</h3>
      {loading ? (
        <p className="customer-form-muted">Laden…</p>
      ) : loadError ? (
        <p className="customer-import-message customer-import-message-error" role="alert">
          {loadError}
        </p>
      ) : (
        <form className="customer-form" onSubmit={handleSubmit}>
          {!canManage && (
            <p className="customer-form-muted">Alleen-lezen — recht “dieseltoeslag beheren” vereist om te wijzigen.</p>
          )}
          <label className="customer-form-checkbox">
            <input type="checkbox" checked={enabled} onChange={(e) => setEnabled(e.target.checked)} disabled={disabled} />
            Actief
          </label>
          <div className="customer-billing-grid">
            <FormField label="Percentage (%)" htmlFor="ds-percent" error={errors.percent}>
              <input
                id="ds-percent"
                value={percent}
                onChange={(e) => setPercent(e.target.value)}
                inputMode="decimal"
                placeholder="bv. 3,5"
                disabled={disabled}
                aria-invalid={errors.percent ? 'true' : undefined}
              />
            </FormField>
            <FormField label="Basis" htmlFor="ds-basis">
              <select id="ds-basis" value={basis} onChange={(e) => setBasis(e.target.value as DieselSurchargeBasis)} disabled={disabled}>
                {Object.entries(DIESEL_BASIS_LABELS).map(([value, label]) => (
                  <option key={value} value={value}>
                    {label}
                  </option>
                ))}
              </select>
            </FormField>
            <FormField label="Weergave" htmlFor="ds-presentation">
              <select
                id="ds-presentation"
                value={presentation}
                onChange={(e) => setPresentation(e.target.value as DieselSurchargePresentation)}
                disabled={disabled}
              >
                {Object.entries(DIESEL_PRESENTATION_LABELS).map(([value, label]) => (
                  <option key={value} value={value}>
                    {label}
                  </option>
                ))}
              </select>
            </FormField>
            <FormField label="Afronding" htmlFor="ds-rounding">
              <select
                id="ds-rounding"
                value={rounding}
                onChange={(e) => setRounding(e.target.value as DieselSurchargeRounding)}
                disabled={disabled}
              >
                {Object.entries(DIESEL_ROUNDING_LABELS).map(([value, label]) => (
                  <option key={value} value={value}>
                    {label}
                  </option>
                ))}
              </select>
            </FormField>
            <FormField label="Geldig van" htmlFor="ds-from">
              <input id="ds-from" type="date" value={effectiveFrom} onChange={(e) => setEffectiveFrom(e.target.value)} disabled={disabled} />
            </FormField>
            <FormField label="Geldig tot" htmlFor="ds-until" error={errors.effectiveUntil}>
              <input
                id="ds-until"
                type="date"
                value={effectiveUntil}
                onChange={(e) => setEffectiveUntil(e.target.value)}
                disabled={disabled}
                aria-invalid={errors.effectiveUntil ? 'true' : undefined}
              />
            </FormField>
          </div>
          <FormField
            label="Formule-omschrijving"
            htmlFor="ds-formula"
            hint="Documentatie; de berekening gebeurt als percentage over de basis."
          >
            <textarea
              id="ds-formula"
              value={formulaDescription}
              onChange={(e) => setFormulaDescription(e.target.value)}
              rows={2}
              maxLength={1000}
              disabled={disabled}
            />
          </FormField>
          {canManage && (
            <div className="customer-form-actions">
              <Button type="submit" disabled={disabled}>
                {busy ? 'Opslaan…' : 'Opslaan'}
              </Button>
            </div>
          )}
        </form>
      )}
    </section>
  )
}

// --- PO-beleid ---

type PoDialogState = { mode: 'create' } | { mode: 'edit'; po: CustomerPoNumber } | null

function PoPolicySection({ customerId }: { customerId: string }) {
  const toast = useToast()
  const { hasPermission } = useAuth()
  const canManage = hasPermission('customers.manage_po')

  const [data, setData] = useState<CustomerPoPolicy | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [dialog, setDialog] = useState<PoDialogState>(null)
  const [removeTarget, setRemoveTarget] = useState<CustomerPoNumber | null>(null)

  const reload = useCallback(() => {
    getPoPolicy(customerId)
      .then((result) => {
        setData(result)
        setLoadError(null)
      })
      .catch(() => setLoadError('Het PO-beleid kon niet worden geladen.'))
  }, [customerId])

  useEffect(() => {
    reload()
  }, [reload])

  async function handlePolicyChange(policy: PurchaseOrderPolicy) {
    setBusy(true)
    try {
      const result = await setPoPolicy(customerId, policy)
      setData(result)
      toast.showSuccess('PO-beleid opgeslagen.')
    } catch (err) {
      toast.showError(describeApiError(err, 'Het PO-beleid kon niet worden opgeslagen.').message)
    } finally {
      setBusy(false)
    }
  }

  async function handleSave(input: SaveCustomerPoNumberInput): Promise<{ ok: boolean; error?: string; fieldErrors?: FieldErrors }> {
    if (!dialog) return { ok: false }
    setBusy(true)
    try {
      const result = dialog.mode === 'edit'
        ? await updatePoNumber(customerId, dialog.po.id, input)
        : await addPoNumber(customerId, input)
      setData(result)
      toast.showSuccess(dialog.mode === 'edit' ? 'PO-nummer bijgewerkt.' : 'PO-nummer toegevoegd.')
      setDialog(null)
      return { ok: true }
    } catch (err) {
      const described = describeApiError(err, 'Het PO-nummer kon niet worden opgeslagen.')
      return { ok: false, error: described.message, fieldErrors: described.fieldErrors }
    } finally {
      setBusy(false)
    }
  }

  const columns: Column<CustomerPoNumber>[] = [
    { key: 'poNumber', header: 'PO-nummer', render: (po) => po.poNumber },
    { key: 'validFrom', header: 'Geldig van', render: (po) => po.validFrom },
    { key: 'validUntil', header: 'Geldig tot', render: (po) => po.validUntil ?? '—' },
    {
      key: 'active',
      header: 'Actief nu',
      render: (po) => (po.isEffectiveToday ? <Badge tone="success">Actief nu</Badge> : '—'),
    },
    { key: 'notes', header: 'Notities', render: (po) => po.notes ?? '—' },
    ...(canManage
      ? [
          {
            key: 'actions',
            header: 'Acties',
            render: (po: CustomerPoNumber) => (
              <span className="customer-contact-actions">
                <Button variant="ghost" onClick={() => setDialog({ mode: 'edit', po })}>
                  Bewerken
                </Button>
                <Button variant="ghost" onClick={() => setRemoveTarget(po)}>
                  Verwijderen
                </Button>
              </span>
            ),
          },
        ]
      : []),
  ]

  return (
    <section className="customer-billing-section">
      <div className="page-header">
        <h3 style={{ margin: 0 }}>PO-beleid</h3>
        {canManage && (
          <Button variant="secondary" onClick={() => setDialog({ mode: 'create' })}>
            + PO-nummer toevoegen
          </Button>
        )}
      </div>

      {loadError ? (
        <p className="customer-import-message customer-import-message-error" role="alert">
          {loadError}
        </p>
      ) : (
        <>
          <FormField
            label="Beleid"
            htmlFor="po-policy"
            hint="Bij 'Verplicht' kan een factuur zonder PO-nummer niet worden verzonden."
          >
            <select
              id="po-policy"
              value={data?.policy ?? 'None'}
              onChange={(e) => void handlePolicyChange(e.target.value as PurchaseOrderPolicy)}
              disabled={!canManage || busy || data === null}
            >
              {Object.entries(PO_POLICY_LABELS).map(([value, label]) => (
                <option key={value} value={value}>
                  {label}
                </option>
              ))}
            </select>
          </FormField>

          <h4 className="customer-billing-subtitle">PO-historiek</h4>
          <DataTable
            columns={columns}
            rows={data?.history ?? []}
            rowKey={(po) => po.id}
            isLoading={data === null}
            emptyMessage="Nog geen PO-nummers geregistreerd."
          />
        </>
      )}

      {dialog && (
        <PoNumberDialog
          po={dialog.mode === 'edit' ? dialog.po : undefined}
          isSubmitting={busy}
          onClose={() => setDialog(null)}
          onSubmit={handleSave}
        />
      )}

      {removeTarget && (
        <ConfirmDialog
          title="PO-nummer verwijderen"
          message={`PO-nummer '${removeTarget.poNumber}' verwijderen?`}
          confirmLabel="Verwijderen"
          destructive
          busy={busy}
          onConfirm={async () => {
            setBusy(true)
            try {
              await deletePoNumber(customerId, removeTarget.id)
              toast.showSuccess('PO-nummer verwijderd.')
              setRemoveTarget(null)
              reload()
            } catch (err) {
              toast.showError(describeApiError(err, 'Het PO-nummer kon niet worden verwijderd.').message)
            } finally {
              setBusy(false)
            }
          }}
          onCancel={() => setRemoveTarget(null)}
        />
      )}
    </section>
  )
}

function PoNumberDialog({
  po,
  isSubmitting,
  onSubmit,
  onClose,
}: {
  po?: CustomerPoNumber
  isSubmitting: boolean
  onSubmit: (input: SaveCustomerPoNumberInput) => Promise<{ ok: boolean; error?: string; fieldErrors?: FieldErrors }>
  onClose: () => void
}) {
  const [poNumber, setPoNumber] = useState(po?.poNumber ?? '')
  const [validFrom, setValidFrom] = useState(po?.validFrom ?? new Date().toISOString().slice(0, 10))
  const [validUntil, setValidUntil] = useState(po?.validUntil ?? '')
  const [notes, setNotes] = useState(po?.notes ?? '')
  const [localErrors, setLocalErrors] = useState<{ poNumber?: string; validFrom?: string; validUntil?: string }>({})
  const [serverError, setServerError] = useState<string | null>(null)
  const [serverFieldErrors, setServerFieldErrors] = useState<FieldErrors>({})

  const title = useMemo(() => (po ? 'PO-nummer bewerken' : 'Nieuw PO-nummer'), [po])

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    const next: { poNumber?: string; validFrom?: string; validUntil?: string } = {}
    if (!poNumber.trim()) next.poNumber = 'Een PO-nummer is verplicht.'
    if (!validFrom) next.validFrom = 'Een begindatum is verplicht.'
    if (validFrom && validUntil && validUntil < validFrom) next.validUntil = 'De einddatum ligt vóór de begindatum.'
    setLocalErrors(next)
    if (next.poNumber || next.validFrom || next.validUntil) return

    setServerError(null)
    setServerFieldErrors({})
    const result = await onSubmit({
      poNumber: poNumber.trim(),
      validFrom,
      validUntil: validUntil || null,
      notes: notes.trim() || null,
    })
    if (!result.ok) {
      setServerError(result.error ?? null)
      setServerFieldErrors(result.fieldErrors ?? {})
    }
  }

  return (
    <Modal
      title={title}
      onClose={onClose}
      busy={isSubmitting}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={isSubmitting}>
            Annuleren
          </Button>
          <Button type="submit" form="po-number-form" disabled={isSubmitting}>
            {isSubmitting ? 'Opslaan…' : 'Opslaan'}
          </Button>
        </>
      }
    >
      <form id="po-number-form" onSubmit={handleSubmit} className="customer-form">
        {serverError && (
          <p className="customer-import-message customer-import-message-error" role="alert">
            {serverError}
          </p>
        )}
        <FormField label="PO-nummer" htmlFor="po-number" required error={localErrors.poNumber ?? getFieldError(serverFieldErrors, 'poNumber')}>
          <input
            id="po-number"
            value={poNumber}
            onChange={(e) => setPoNumber(e.target.value)}
            maxLength={100}
            aria-invalid={localErrors.poNumber ? 'true' : undefined}
          />
        </FormField>
        <FormField label="Geldig van" htmlFor="po-from" required error={localErrors.validFrom ?? getFieldError(serverFieldErrors, 'validFrom')}>
          <input
            id="po-from"
            type="date"
            value={validFrom}
            onChange={(e) => setValidFrom(e.target.value)}
            aria-invalid={localErrors.validFrom ? 'true' : undefined}
          />
        </FormField>
        <FormField label="Geldig tot" htmlFor="po-until" error={localErrors.validUntil ?? getFieldError(serverFieldErrors, 'validUntil')}>
          <input
            id="po-until"
            type="date"
            value={validUntil}
            onChange={(e) => setValidUntil(e.target.value)}
            aria-invalid={localErrors.validUntil ? 'true' : undefined}
          />
        </FormField>
        <FormField label="Notities" htmlFor="po-notes">
          <textarea id="po-notes" value={notes} onChange={(e) => setNotes(e.target.value)} rows={2} maxLength={500} />
        </FormField>
      </form>
    </Modal>
  )
}
