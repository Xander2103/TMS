import { useEffect, useState, type FormEvent } from 'react'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import { localizeApiError } from '../../../api/problemDetails'
import { useLocale } from '../../../i18n/localeContext'
import { euro } from '../../invoices/types'
import {
  createCostRateSet,
  deleteCostRateSet,
  listCostRateSets,
  updateCostRateSet,
} from '../api/tripCostingApi'
import type { CostRateSet, CostRateSetInput } from '../types'
import '../components/trip-costing.css'

interface RateField {
  key: keyof CostRateSetInput
  labelKey: string
  hintKey?: string
  step?: string
}

const RATE_FIELDS: RateField[] = [
  { key: 'fuelPricePerLitre', labelKey: 'tripCosting.rates.fields.fuelPricePerLitre', step: '0.001' },
  { key: 'defaultConsumptionLPer100Km', labelKey: 'tripCosting.rates.fields.defaultConsumptionLPer100Km', hintKey: 'tripCosting.rates.fields.defaultConsumptionLPer100KmHint', step: '0.1' },
  { key: 'vehicleCostPerKm', labelKey: 'tripCosting.rates.fields.vehicleCostPerKm', step: '0.01' },
  { key: 'vehicleCostPerHour', labelKey: 'tripCosting.rates.fields.vehicleCostPerHour', step: '0.01' },
  { key: 'driverCostPerHour', labelKey: 'tripCosting.rates.fields.driverCostPerHour', step: '0.01' },
  { key: 'employerCostMultiplier', labelKey: 'tripCosting.rates.fields.employerCostMultiplier', hintKey: 'tripCosting.rates.fields.employerCostMultiplierHint', step: '0.01' },
  { key: 'maintenanceCostPerKm', labelKey: 'tripCosting.rates.fields.maintenanceCostPerKm', step: '0.001' },
  { key: 'depreciationPerDay', labelKey: 'tripCosting.rates.fields.depreciationPerDay', step: '0.01' },
  { key: 'trailerCostPerDay', labelKey: 'tripCosting.rates.fields.trailerCostPerDay', step: '0.01' },
  { key: 'equipmentCostPerDay', labelKey: 'tripCosting.rates.fields.equipmentCostPerDay', step: '0.01' },
  { key: 'defaultTollPerTrip', labelKey: 'tripCosting.rates.fields.defaultTollPerTrip', step: '0.01' },
  { key: 'overtimeThresholdMinutesPerDay', labelKey: 'tripCosting.rates.fields.overtimeThresholdMinutesPerDay', hintKey: 'tripCosting.rates.fields.overtimeThresholdMinutesPerDayHint', step: '1' },
  { key: 'overtimeRateMultiplier', labelKey: 'tripCosting.rates.fields.overtimeRateMultiplier', hintKey: 'tripCosting.rates.fields.overtimeRateMultiplierHint', step: '0.01' },
  { key: 'waitingTimeCostPerHour', labelKey: 'tripCosting.rates.fields.waitingTimeCostPerHour', step: '0.01' },
  { key: 'co2KgPerLitreDiesel', labelKey: 'tripCosting.rates.fields.co2KgPerLitreDiesel', step: '0.001' },
  { key: 'co2KgPerLitreOther', labelKey: 'tripCosting.rates.fields.co2KgPerLitreOther', step: '0.001' },
]

const EMPTY_INPUT: CostRateSetInput = {
  effectiveFrom: '',
  name: null,
  fuelPricePerLitre: 0,
  defaultConsumptionLPer100Km: 0,
  vehicleCostPerKm: 0,
  vehicleCostPerHour: 0,
  driverCostPerHour: 0,
  employerCostMultiplier: 1.35,
  maintenanceCostPerKm: 0,
  depreciationPerDay: 0,
  trailerCostPerDay: 0,
  equipmentCostPerDay: 0,
  defaultTollPerTrip: 0,
  overtimeThresholdMinutesPerDay: 480,
  overtimeRateMultiplier: 1.5,
  waitingTimeCostPerHour: 0,
  co2KgPerLitreDiesel: 2.68,
  co2KgPerLitreOther: 2.31,
}

/**
 * Effective-dated cost rate cards. Historic trips keep their snapshots: a new card only
 * affects calculations from its effective date onwards.
 */
export function CostRatesPage() {
  const { t } = useLocale()
  const { hasPermission } = useAuth()
  const { showSuccess, showError } = useToast()
  const canManage = hasPermission('trip_costs.manage')

  const [sets, setSets] = useState<CostRateSet[] | null>(null)
  // Vertaalsleutel in state; vertaling gebeurt pas bij render.
  const [loadErrorKey, setLoadErrorKey] = useState<string | null>(null)
  const [reloadToken, setReloadToken] = useState(0)

  const [editorOpen, setEditorOpen] = useState(false)
  const [editing, setEditing] = useState<CostRateSet | null>(null)
  const [form, setForm] = useState<CostRateSetInput>(EMPTY_INPUT)
  const [busy, setBusy] = useState(false)
  const [deleteTarget, setDeleteTarget] = useState<CostRateSet | null>(null)

  useEffect(() => {
    let mounted = true
    listCostRateSets()
      .then((data) => {
        if (!mounted) return
        setSets(data)
        setLoadErrorKey(null)
      })
      .catch(() => {
        if (mounted) setLoadErrorKey('tripCosting.rates.loadFailed')
      })
    return () => {
      mounted = false
    }
  }, [reloadToken])

  function openCreate() {
    setEditing(null)
    setForm({ ...EMPTY_INPUT, effectiveFrom: new Date().toISOString().slice(0, 10) })
    setEditorOpen(true)
  }

  function openEdit(set: CostRateSet) {
    setEditing(set)
    const input: CostRateSetInput & { id?: string } = { ...set }
    delete input.id
    setForm(input)
    setEditorOpen(true)
  }

  async function submit(event: FormEvent) {
    event.preventDefault()
    if (!form.effectiveFrom) {
      showError(t('tripCosting.rates.effectiveFromRequired'))
      return
    }
    setBusy(true)
    try {
      if (editing) {
        await updateCostRateSet(editing.id, form)
        showSuccess(t('tripCosting.rates.toasts.updated'))
      } else {
        await createCostRateSet(form)
        showSuccess(t('tripCosting.rates.toasts.created'))
      }
      setEditorOpen(false)
      setReloadToken((token) => token + 1)
    } catch (err) {
      showError(localizeApiError(t, err, t('tripCosting.rates.saveFailed')))
    } finally {
      setBusy(false)
    }
  }

  function setNumber(key: keyof CostRateSetInput, raw: string) {
    const value = Number(raw.replace(',', '.'))
    setForm((current) => ({ ...current, [key]: Number.isNaN(value) ? 0 : value }))
  }

  if (loadErrorKey) return <p className="placeholder-text">{t(loadErrorKey)}</p>

  return (
    <div>
      <Breadcrumbs items={[{ label: t('tripCosting.rates.title') }]} />
      <PageHeader
        title={t('tripCosting.rates.title')}
        subtitle={t('tripCosting.rates.subtitle')}
        action={canManage && <Button onClick={openCreate}>{t('tripCosting.rates.newSet')}</Button>}
      />

      {sets === null && <p className="placeholder-text">{t('tripCosting.rates.loading')}</p>}
      {sets !== null && sets.length === 0 && (
        <p className="placeholder-text">{t('tripCosting.rates.empty')}</p>
      )}
      {sets !== null && sets.length > 0 && (
        <table className="tc-table">
          <thead>
            <tr>
              <th>{t('tripCosting.rates.table.effectiveFrom')}</th>
              <th>{t('tripCosting.rates.table.name')}</th>
              <th className="tc-num">{t('tripCosting.rates.table.fuel')}</th>
              <th className="tc-num">{t('tripCosting.rates.table.driver')}</th>
              <th className="tc-num">{t('tripCosting.rates.table.vehicleKm')}</th>
              <th className="tc-num">{t('tripCosting.rates.table.vehicleHour')}</th>
              <th className="tc-num">{t('tripCosting.rates.table.depreciation')}</th>
              {canManage && <th aria-label={t('tripCosting.rates.table.actions')} />}
            </tr>
          </thead>
          <tbody>
            {sets.map((set) => (
              <tr key={set.id}>
                <td>{set.effectiveFrom}</td>
                <td>{set.name ?? '—'}</td>
                <td className="tc-num">{set.fuelPricePerLitre.toLocaleString('nl-BE', { minimumFractionDigits: 3 })}</td>
                <td className="tc-num">{euro(set.driverCostPerHour)}</td>
                <td className="tc-num">{euro(set.vehicleCostPerKm)}</td>
                <td className="tc-num">{euro(set.vehicleCostPerHour)}</td>
                <td className="tc-num">{euro(set.depreciationPerDay)}</td>
                {canManage && (
                  <td className="tc-row-actions">
                    <button type="button" className="tc-link-button" onClick={() => openEdit(set)}>
                      {t('ui.actions.edit')}
                    </button>
                    <button type="button" className="tc-link-button tc-danger" onClick={() => setDeleteTarget(set)}>
                      {t('ui.actions.delete')}
                    </button>
                  </td>
                )}
              </tr>
            ))}
          </tbody>
        </table>
      )}

      {editorOpen && (
        <Modal
          title={editing
            ? t('tripCosting.rates.editor.editTitle', { date: editing.effectiveFrom })
            : t('tripCosting.rates.newSet')}
          onClose={() => setEditorOpen(false)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setEditorOpen(false)} disabled={busy}>
                {t('ui.actions.cancel')}
              </Button>
              <Button type="submit" form="cr-form" disabled={busy}>
                {busy ? t('tripCosting.rates.editor.busy') : t('ui.actions.save')}
              </Button>
            </>
          }
        >
          <form id="cr-form" className="tc-form" onSubmit={submit} noValidate>
            <div className="tc-form-row">
              <FormField label={t('tripCosting.rates.editor.effectiveFrom')} htmlFor="cr-effective" required hint={t('tripCosting.rates.editor.effectiveFromHint')}>
                <input
                  id="cr-effective"
                  type="date"
                  value={form.effectiveFrom}
                  onChange={(e) => setForm((current) => ({ ...current, effectiveFrom: e.target.value }))}
                  disabled={busy}
                />
              </FormField>
              <FormField label={t('tripCosting.rates.editor.name')} htmlFor="cr-name">
                <input
                  id="cr-name"
                  value={form.name ?? ''}
                  onChange={(e) => setForm((current) => ({ ...current, name: e.target.value || null }))}
                  maxLength={100}
                  disabled={busy}
                />
              </FormField>
            </div>
            <div className="tc-form-row">
              {RATE_FIELDS.map((field) => (
                <FormField key={field.key} label={t(field.labelKey)} htmlFor={`cr-${field.key}`} hint={field.hintKey ? t(field.hintKey) : undefined}>
                  <input
                    id={`cr-${field.key}`}
                    type="number"
                    step={field.step}
                    min="0"
                    value={String(form[field.key] ?? 0)}
                    onChange={(e) => setNumber(field.key, e.target.value)}
                    disabled={busy}
                  />
                </FormField>
              ))}
            </div>
          </form>
        </Modal>
      )}

      {deleteTarget && (
        <ConfirmDialog
          title={t('tripCosting.rates.deleteDialog.title')}
          message={t('tripCosting.rates.deleteDialog.message', { date: deleteTarget.effectiveFrom })}
          confirmLabel={t('ui.actions.delete')}
          destructive
          busy={busy}
          onConfirm={async () => {
            setBusy(true)
            try {
              await deleteCostRateSet(deleteTarget.id)
              showSuccess(t('tripCosting.rates.toasts.deleted'))
              setDeleteTarget(null)
              setReloadToken((token) => token + 1)
            } catch (err) {
              showError(localizeApiError(t, err, t('tripCosting.rates.deleteFailed')))
            } finally {
              setBusy(false)
            }
          }}
          onCancel={() => setDeleteTarget(null)}
        />
      )}
    </div>
  )
}
