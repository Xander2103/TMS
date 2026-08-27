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
import { useLocale } from '../../../i18n/localeContext'
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
  DIESEL_BASIS_LABEL_KEYS,
  DIESEL_PRESENTATION_LABEL_KEYS,
  DIESEL_ROUNDING_LABEL_KEYS,
  PO_POLICY_LABEL_KEYS,
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
  const { t } = useLocale()
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
        if (mounted) setLoadError(t('customers.billing.dieselLoadFailed'))
      })
      .finally(() => {
        if (mounted) setLoading(false)
      })
    return () => {
      mounted = false
    }
  }, [customerId, t])

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    // validateSurchargeForm levert vertaalsleutels; hier vertaald voor weergave.
    const validationKeys = validateSurchargeForm({ percent, effectiveFrom, effectiveUntil })
    const validation: SurchargeFormErrors = {
      percent: validationKeys.percent ? t(validationKeys.percent) : undefined,
      effectiveUntil: validationKeys.effectiveUntil ? t(validationKeys.effectiveUntil) : undefined,
    }
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
      toast.showSuccess(t('customers.billing.dieselSaved'))
    } catch (err) {
      toast.showError(describeApiError(err, t('customers.billing.dieselSaveFailed')).message)
    } finally {
      setBusy(false)
    }
  }

  const disabled = !canManage || busy

  return (
    <section className="customer-billing-section">
      <h3>{t('customers.billing.dieselTitle')}</h3>
      {loading ? (
        <p className="customer-form-muted">{t('customers.common.loading')}</p>
      ) : loadError ? (
        <p className="customer-import-message customer-import-message-error" role="alert">
          {loadError}
        </p>
      ) : (
        <form className="customer-form" onSubmit={handleSubmit}>
          {!canManage && (
            <p className="customer-form-muted">{t('customers.billing.readOnlyHint')}</p>
          )}
          <label className="customer-form-checkbox">
            <input type="checkbox" checked={enabled} onChange={(e) => setEnabled(e.target.checked)} disabled={disabled} />
            {t('ui.statusBadges.active')}
          </label>
          <div className="customer-billing-grid">
            <FormField label={t('customers.billing.percentField')} htmlFor="ds-percent" error={errors.percent}>
              <input
                id="ds-percent"
                value={percent}
                onChange={(e) => setPercent(e.target.value)}
                inputMode="decimal"
                placeholder={t('customers.billing.percentPlaceholder')}
                disabled={disabled}
                aria-invalid={errors.percent ? 'true' : undefined}
              />
            </FormField>
            <FormField label={t('customers.billing.basisField')} htmlFor="ds-basis">
              <select id="ds-basis" value={basis} onChange={(e) => setBasis(e.target.value as DieselSurchargeBasis)} disabled={disabled}>
                {Object.entries(DIESEL_BASIS_LABEL_KEYS).map(([value, labelKey]) => (
                  <option key={value} value={value}>
                    {t(labelKey)}
                  </option>
                ))}
              </select>
            </FormField>
            <FormField label={t('customers.billing.presentationField')} htmlFor="ds-presentation">
              <select
                id="ds-presentation"
                value={presentation}
                onChange={(e) => setPresentation(e.target.value as DieselSurchargePresentation)}
                disabled={disabled}
              >
                {Object.entries(DIESEL_PRESENTATION_LABEL_KEYS).map(([value, labelKey]) => (
                  <option key={value} value={value}>
                    {t(labelKey)}
                  </option>
                ))}
              </select>
            </FormField>
            <FormField label={t('customers.billing.roundingField')} htmlFor="ds-rounding">
              <select
                id="ds-rounding"
                value={rounding}
                onChange={(e) => setRounding(e.target.value as DieselSurchargeRounding)}
                disabled={disabled}
              >
                {Object.entries(DIESEL_ROUNDING_LABEL_KEYS).map(([value, labelKey]) => (
                  <option key={value} value={value}>
                    {t(labelKey)}
                  </option>
                ))}
              </select>
            </FormField>
            <FormField label={t('customers.billing.validFrom')} htmlFor="ds-from">
              <input id="ds-from" type="date" value={effectiveFrom} onChange={(e) => setEffectiveFrom(e.target.value)} disabled={disabled} />
            </FormField>
            <FormField label={t('customers.billing.validUntil')} htmlFor="ds-until" error={errors.effectiveUntil}>
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
            label={t('customers.billing.formulaField')}
            htmlFor="ds-formula"
            hint={t('customers.billing.formulaHint')}
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
                {busy ? t('customers.common.savingEllipsis') : t('ui.actions.save')}
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
  const { t } = useLocale()
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
      .catch(() => setLoadError(t('customers.billing.poLoadFailed')))
  }, [customerId, t])

  useEffect(() => {
    reload()
  }, [reload])

  async function handlePolicyChange(policy: PurchaseOrderPolicy) {
    setBusy(true)
    try {
      const result = await setPoPolicy(customerId, policy)
      setData(result)
      toast.showSuccess(t('customers.billing.poPolicySaved'))
    } catch (err) {
      toast.showError(describeApiError(err, t('customers.billing.poPolicySaveFailed')).message)
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
      toast.showSuccess(dialog.mode === 'edit' ? t('customers.billing.poUpdated') : t('customers.billing.poAdded'))
      setDialog(null)
      return { ok: true }
    } catch (err) {
      const described = describeApiError(err, t('customers.billing.poSaveFailed'))
      return { ok: false, error: described.message, fieldErrors: described.fieldErrors }
    } finally {
      setBusy(false)
    }
  }

  const columns: Column<CustomerPoNumber>[] = [
    { key: 'poNumber', header: t('customers.billing.poNumberColumn'), render: (po) => po.poNumber },
    { key: 'validFrom', header: t('customers.billing.validFrom'), render: (po) => po.validFrom },
    { key: 'validUntil', header: t('customers.billing.validUntil'), render: (po) => po.validUntil ?? '—' },
    {
      key: 'active',
      header: t('customers.billing.activeNow'),
      render: (po) => (po.isEffectiveToday ? <Badge tone="success">{t('customers.billing.activeNow')}</Badge> : '—'),
    },
    { key: 'notes', header: t('customers.billing.notesColumn'), render: (po) => po.notes ?? '—' },
    ...(canManage
      ? [
          {
            key: 'actions',
            header: t('customers.billing.actionsColumn'),
            render: (po: CustomerPoNumber) => (
              <span className="customer-contact-actions">
                <Button variant="ghost" onClick={() => setDialog({ mode: 'edit', po })}>
                  {t('ui.actions.edit')}
                </Button>
                <Button variant="ghost" onClick={() => setRemoveTarget(po)}>
                  {t('ui.actions.delete')}
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
        <h3 style={{ margin: 0 }}>{t('customers.billing.poTitle')}</h3>
        {canManage && (
          <Button variant="secondary" onClick={() => setDialog({ mode: 'create' })}>
            {t('customers.billing.addPoNumber')}
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
            label={t('customers.billing.policyField')}
            htmlFor="po-policy"
            hint={t('customers.billing.policyHint')}
          >
            <select
              id="po-policy"
              value={data?.policy ?? 'None'}
              onChange={(e) => void handlePolicyChange(e.target.value as PurchaseOrderPolicy)}
              disabled={!canManage || busy || data === null}
            >
              {Object.entries(PO_POLICY_LABEL_KEYS).map(([value, labelKey]) => (
                <option key={value} value={value}>
                  {t(labelKey)}
                </option>
              ))}
            </select>
          </FormField>

          <h4 className="customer-billing-subtitle">{t('customers.billing.poHistoryTitle')}</h4>
          <DataTable
            columns={columns}
            rows={data?.history ?? []}
            rowKey={(po) => po.id}
            isLoading={data === null}
            emptyMessage={t('customers.billing.poEmpty')}
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
          title={t('customers.billing.poRemoveTitle')}
          message={t('customers.billing.poRemoveMessage', { number: removeTarget.poNumber })}
          confirmLabel={t('ui.actions.delete')}
          destructive
          busy={busy}
          onConfirm={async () => {
            setBusy(true)
            try {
              await deletePoNumber(customerId, removeTarget.id)
              toast.showSuccess(t('customers.billing.poRemoved'))
              setRemoveTarget(null)
              reload()
            } catch (err) {
              toast.showError(describeApiError(err, t('customers.billing.poRemoveFailed')).message)
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
  const { t } = useLocale()
  const [poNumber, setPoNumber] = useState(po?.poNumber ?? '')
  const [validFrom, setValidFrom] = useState(po?.validFrom ?? new Date().toISOString().slice(0, 10))
  const [validUntil, setValidUntil] = useState(po?.validUntil ?? '')
  const [notes, setNotes] = useState(po?.notes ?? '')
  const [localErrors, setLocalErrors] = useState<{ poNumber?: string; validFrom?: string; validUntil?: string }>({})
  const [serverError, setServerError] = useState<string | null>(null)
  const [serverFieldErrors, setServerFieldErrors] = useState<FieldErrors>({})

  const title = useMemo(() => (po ? t('customers.billing.poEditTitle') : t('customers.billing.poNewTitle')), [po, t])

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    const next: { poNumber?: string; validFrom?: string; validUntil?: string } = {}
    if (!poNumber.trim()) next.poNumber = t('customers.billing.poNumberRequired')
    if (!validFrom) next.validFrom = t('customers.billing.startDateRequired')
    if (validFrom && validUntil && validUntil < validFrom) next.validUntil = t('customers.billing.endBeforeStart')
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
            {t('ui.actions.cancel')}
          </Button>
          <Button type="submit" form="po-number-form" disabled={isSubmitting}>
            {isSubmitting ? t('customers.common.savingEllipsis') : t('ui.actions.save')}
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
        <FormField label={t('customers.billing.poNumberColumn')} htmlFor="po-number" required error={localErrors.poNumber ?? getFieldError(serverFieldErrors, 'poNumber')}>
          <input
            id="po-number"
            value={poNumber}
            onChange={(e) => setPoNumber(e.target.value)}
            maxLength={100}
            aria-invalid={localErrors.poNumber ? 'true' : undefined}
          />
        </FormField>
        <FormField label={t('customers.billing.validFrom')} htmlFor="po-from" required error={localErrors.validFrom ?? getFieldError(serverFieldErrors, 'validFrom')}>
          <input
            id="po-from"
            type="date"
            value={validFrom}
            onChange={(e) => setValidFrom(e.target.value)}
            aria-invalid={localErrors.validFrom ? 'true' : undefined}
          />
        </FormField>
        <FormField label={t('customers.billing.validUntil')} htmlFor="po-until" error={localErrors.validUntil ?? getFieldError(serverFieldErrors, 'validUntil')}>
          <input
            id="po-until"
            type="date"
            value={validUntil}
            onChange={(e) => setValidUntil(e.target.value)}
            aria-invalid={localErrors.validUntil ? 'true' : undefined}
          />
        </FormField>
        <FormField label={t('customers.billing.notesColumn')} htmlFor="po-notes">
          <textarea id="po-notes" value={notes} onChange={(e) => setNotes(e.target.value)} rows={2} maxLength={500} />
        </FormField>
      </form>
    </Modal>
  )
}
