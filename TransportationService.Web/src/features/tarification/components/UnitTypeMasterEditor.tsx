import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import { localizeApiError } from '../../../api/problemDetails'
import { useLocale } from '../../../i18n/localeContext'
import {
  DIMENSION_BEHAVIOR_LABELS,
  UNIT_CATEGORY_LABELS,
  createUnitTypeMaster,
  listUnitTypeMaster,
  updateUnitTypeMaster,
  type UnitCategory,
  type UnitDimensionBehavior,
  type UnitTypeMaster,
} from '../api/pricingApi'
import { suggestUnitCode } from '../unitCodeSuggestion'

interface UnitDraft {
  unit: UnitTypeMaster | null
  code: string
  codeTouched: boolean
  name: string
  category: UnitCategory
  decimals: string
  symbol: string
  dimensionBehavior: UnitDimensionBehavior
  defaultLengthCm: string
  defaultWidthCm: string
  defaultHeightCm: string
  defaultWeightKg: string
  maxWeightKg: string
  defaultVolumeM3: string
  defaultLoadingMeters: string
  defaultPalletPlaces: string
  allowForOrderEntry: boolean
  allowForPricing: boolean
  allowForInventory: boolean
  isActive: boolean
  sortOrder: string
}

const dims = (unit: UnitTypeMaster): string => {
  if (unit.defaultLengthCm === null && unit.defaultWidthCm === null && unit.defaultHeightCm === null) return '—'
  const part = (v: number | null) => (v === null ? '?' : `${v}`)
  const base = `${part(unit.defaultLengthCm)} × ${part(unit.defaultWidthCm)}`
  return unit.defaultHeightCm === null ? `${base} cm` : `${base} × ${unit.defaultHeightCm} cm`
}

/**
 * Stamgegevens → Eenheden: full unit master data incl. physical defaults. Units are
 * configuration, never code — admins add company-specific units here without development.
 */
export function UnitTypeMasterEditor() {
  const { t } = useLocale()
  const { hasPermission } = useAuth()
  const { showSuccess } = useToast()
  const canView =
    hasPermission('unit_types.view') || hasPermission('unit_types.manage')
    || hasPermission('tariffs.view') || hasPermission('tariffs.manage')
  const canManage = hasPermission('unit_types.manage') || hasPermission('tariffs.manage')

  const [units, setUnits] = useState<UnitTypeMaster[] | null>(null)
  const [loadErrorKey, setLoadErrorKey] = useState<string | null>(null)
  const [draft, setDraft] = useState<UnitDraft | null>(null)
  const [draftError, setDraftError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const reload = useCallback(() => {
    if (!canView) return
    listUnitTypeMaster()
      .then((data) => {
        setUnits(data)
        setLoadErrorKey(null)
      })
      .catch(() => setLoadErrorKey('tarification.unitMaster.loadError'))
  }, [canView])

  useEffect(() => {
    reload()
  }, [reload])

  if (!canView) return <p className="placeholder-text">{t('tarification.unitMaster.noViewPermission')}</p>
  if (loadErrorKey) return <p className="placeholder-text">{t(loadErrorKey)}</p>
  if (units === null) return <p className="placeholder-text">{t('tarification.unitMaster.loading')}</p>

  function openDraft(unit: UnitTypeMaster | null) {
    setDraftError(null)
    setDraft(
      unit
        ? {
            unit,
            code: unit.code,
            codeTouched: true, // never re-suggest for an existing unit
            name: unit.name,
            category: unit.category,
            decimals: String(unit.decimals),
            symbol: unit.symbol ?? '',
            dimensionBehavior: unit.dimensionBehavior,
            defaultLengthCm: unit.defaultLengthCm !== null ? String(unit.defaultLengthCm) : '',
            defaultWidthCm: unit.defaultWidthCm !== null ? String(unit.defaultWidthCm) : '',
            defaultHeightCm: unit.defaultHeightCm !== null ? String(unit.defaultHeightCm) : '',
            defaultWeightKg: unit.defaultWeightKg !== null ? String(unit.defaultWeightKg) : '',
            maxWeightKg: unit.maxWeightKg !== null ? String(unit.maxWeightKg) : '',
            defaultVolumeM3: unit.defaultVolumeM3 !== null ? String(unit.defaultVolumeM3) : '',
            defaultLoadingMeters: unit.defaultLoadingMeters !== null ? String(unit.defaultLoadingMeters) : '',
            defaultPalletPlaces: unit.defaultPalletPlaces !== null ? String(unit.defaultPalletPlaces) : '',
            allowForOrderEntry: unit.allowForOrderEntry,
            allowForPricing: unit.allowForPricing,
            allowForInventory: unit.allowForInventory,
            isActive: unit.isActive,
            sortOrder: String(unit.sortOrder),
          }
        : {
            unit: null,
            code: '',
            codeTouched: false,
            name: '',
            category: 'Packaging',
            decimals: '0',
            symbol: '',
            dimensionBehavior: 'Variable',
            defaultLengthCm: '',
            defaultWidthCm: '',
            defaultHeightCm: '',
            defaultWeightKg: '',
            maxWeightKg: '',
            defaultVolumeM3: '',
            defaultLoadingMeters: '',
            defaultPalletPlaces: '',
            allowForOrderEntry: true,
            allowForPricing: true,
            allowForInventory: false,
            isActive: true,
            sortOrder: String((units ?? []).length),
          },
    )
  }

  async function submitDraft(event: FormEvent) {
    event.preventDefault()
    if (!draft) return
    const num = (raw: string) => (raw.trim() === '' ? null : Number(raw))
    setBusy(true)
    try {
      const input = {
        code: draft.code.trim().toUpperCase(),
        name: draft.name.trim(),
        description: null,
        isActive: draft.isActive,
        sortOrder: Number(draft.sortOrder) || 0,
        allowForOrderEntry: draft.allowForOrderEntry,
        allowForPricing: draft.allowForPricing,
        allowForInventory: draft.allowForInventory,
        category: draft.category,
        decimals: Number(draft.decimals) || 0,
        symbol: draft.symbol.trim() || null,
        dimensionBehavior: draft.dimensionBehavior,
        defaultLengthCm: num(draft.defaultLengthCm),
        defaultWidthCm: num(draft.defaultWidthCm),
        defaultHeightCm: num(draft.defaultHeightCm),
        defaultWeightKg: num(draft.defaultWeightKg),
        maxWeightKg: num(draft.maxWeightKg),
        defaultVolumeM3: num(draft.defaultVolumeM3),
        defaultLoadingMeters: num(draft.defaultLoadingMeters),
        defaultPalletPlaces: num(draft.defaultPalletPlaces),
      }
      if (draft.unit) {
        await updateUnitTypeMaster(draft.unit.id, input)
        showSuccess(t('tarification.unitMaster.updated'))
      } else {
        await createUnitTypeMaster(input)
        showSuccess(t('tarification.unitMaster.added'))
      }
      setDraft(null)
      reload()
    } catch (err) {
      setDraftError(localizeApiError(t, err, t('tarification.unitMaster.saveError')))
    } finally {
      setBusy(false)
    }
  }

  return (
    <section>
      <div className="customer-panel-header">
        <h3>{t('tarification.unitMaster.title')}</h3>
        {canManage && <Button onClick={() => openDraft(null)}>{t('tarification.unitMaster.addUnit')}</Button>}
      </div>
      <p className="customer-form-muted">{t('tarification.unitMaster.intro')}</p>
      <table className="issued-items-table">
        <thead>
          <tr>
            <th>{t('tarification.unitMaster.colCode')}</th>
            <th>{t('tarification.common.name')}</th>
            <th>{t('tarification.unitMaster.colCategory')}</th>
            <th>{t('tarification.unitMaster.colDimensions')}</th>
            <th>{t('tarification.unitMaster.colBehavior')}</th>
            <th>{t('tarification.unitMaster.colOrder')}</th>
            <th>{t('tarification.unitMaster.colTariff')}</th>
            <th>{t('tarification.common.active')}</th>
            {canManage && <th aria-label={t('tarification.common.actions')} />}
          </tr>
        </thead>
        <tbody>
          {units.map((unit) => (
            <tr key={unit.id}>
              <td>{unit.code}</td>
              <td>
                {unit.name}
                {unit.symbol && <span className="customer-form-muted"> ({unit.symbol})</span>}
              </td>
              <td>{t(UNIT_CATEGORY_LABELS[unit.category])}</td>
              <td>{dims(unit)}</td>
              <td>{t(DIMENSION_BEHAVIOR_LABELS[unit.dimensionBehavior])}</td>
              <td>{unit.allowForOrderEntry ? t('tarification.common.yes') : t('tarification.common.no')}</td>
              <td>{unit.allowForPricing ? t('tarification.common.yes') : t('tarification.common.no')}</td>
              <td>{unit.isActive ? t('tarification.common.yes') : <Badge tone="neutral">{t('tarification.common.inactive')}</Badge>}</td>
              {canManage && (
                <td className="issued-items-row-actions">
                  <button type="button" className="issued-items-link" onClick={() => openDraft(unit)}>
                    {t('ui.actions.edit')}
                  </button>
                </td>
              )}
            </tr>
          ))}
        </tbody>
      </table>

      {draft && (
        <Modal
          title={draft.unit ? t('tarification.unitMaster.editTitle', { name: draft.unit.name }) : t('tarification.unitMaster.addTitle')}
          onClose={() => setDraft(null)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setDraft(null)} disabled={busy}>
                {t('ui.actions.cancel')}
              </Button>
              <Button type="submit" form="unit-master-form" disabled={busy}>
                {t('ui.actions.save')}
              </Button>
            </>
          }
        >
          <form id="unit-master-form" className="issued-items-form" onSubmit={submitDraft} noValidate>
            {draftError && (
              <div className="issued-items-form-error" role="alert">
                {draftError}
              </div>
            )}
            <div className="issued-items-form-row">
              <FormField label={t('tarification.common.name')} htmlFor="um-name" required>
                <input
                  id="um-name"
                  value={draft.name}
                  maxLength={150}
                  onChange={(e) =>
                    setDraft((d) =>
                      d
                        ? {
                            ...d,
                            name: e.target.value,
                            // Suggestion only while creating and while the code was not touched.
                            code: !d.unit && !d.codeTouched ? suggestUnitCode(e.target.value) : d.code,
                          }
                        : d,
                    )
                  }
                />
              </FormField>
              <FormField label={t('tarification.unitMaster.codeLabel')} htmlFor="um-code" required hint={t('tarification.unitMaster.codeHint')}>
                <input
                  id="um-code"
                  value={draft.code}
                  maxLength={20}
                  onChange={(e) => setDraft((d) => (d ? { ...d, code: e.target.value.toUpperCase(), codeTouched: true } : d))}
                />
              </FormField>
            </div>
            <div className="issued-items-form-row">
              <FormField label={t('tarification.unitMaster.colCategory')} htmlFor="um-category">
                <select id="um-category" value={draft.category} onChange={(e) => setDraft((d) => (d ? { ...d, category: e.target.value as UnitCategory } : d))}>
                  {Object.entries(UNIT_CATEGORY_LABELS).map(([value, labelKey]) => (
                    <option key={value} value={value}>
                      {t(labelKey)}
                    </option>
                  ))}
                </select>
              </FormField>
              <FormField label={t('tarification.unitMaster.symbolLabel')} htmlFor="um-symbol" hint={t('tarification.unitMaster.symbolHint')}>
                <input id="um-symbol" value={draft.symbol} maxLength={20} onChange={(e) => setDraft((d) => (d ? { ...d, symbol: e.target.value } : d))} />
              </FormField>
              <FormField label={t('tarification.unitMaster.decimalsLabel')} htmlFor="um-decimals">
                <input id="um-decimals" type="number" min={0} max={4} value={draft.decimals} onChange={(e) => setDraft((d) => (d ? { ...d, decimals: e.target.value } : d))} />
              </FormField>
            </div>
            <div className="issued-items-form-row">
              <FormField label={t('tarification.unitMaster.behaviorLabel')} htmlFor="um-behavior" hint={t('tarification.unitMaster.behaviorHint')}>
                <select
                  id="um-behavior"
                  value={draft.dimensionBehavior}
                  onChange={(e) => setDraft((d) => (d ? { ...d, dimensionBehavior: e.target.value as UnitDimensionBehavior } : d))}
                >
                  {Object.entries(DIMENSION_BEHAVIOR_LABELS).map(([value, labelKey]) => (
                    <option key={value} value={value}>
                      {t(labelKey)}
                    </option>
                  ))}
                </select>
              </FormField>
              <FormField label={t('tarification.unitMaster.sortLabel')} htmlFor="um-sort">
                <input id="um-sort" type="number" value={draft.sortOrder} onChange={(e) => setDraft((d) => (d ? { ...d, sortOrder: e.target.value } : d))} />
              </FormField>
            </div>
            <div className="issued-items-form-row">
              <FormField label={t('tarification.unitMaster.lengthLabel')} htmlFor="um-length">
                <input id="um-length" type="number" step="0.1" value={draft.defaultLengthCm} onChange={(e) => setDraft((d) => (d ? { ...d, defaultLengthCm: e.target.value } : d))} />
              </FormField>
              <FormField label={t('tarification.unitMaster.widthLabel')} htmlFor="um-width">
                <input id="um-width" type="number" step="0.1" value={draft.defaultWidthCm} onChange={(e) => setDraft((d) => (d ? { ...d, defaultWidthCm: e.target.value } : d))} />
              </FormField>
              <FormField label={t('tarification.unitMaster.heightLabel')} htmlFor="um-height">
                <input id="um-height" type="number" step="0.1" value={draft.defaultHeightCm} onChange={(e) => setDraft((d) => (d ? { ...d, defaultHeightCm: e.target.value } : d))} />
              </FormField>
            </div>
            <div className="issued-items-form-row">
              <FormField label={t('tarification.unitMaster.weightLabel')} htmlFor="um-weight">
                <input id="um-weight" type="number" step="0.1" value={draft.defaultWeightKg} onChange={(e) => setDraft((d) => (d ? { ...d, defaultWeightKg: e.target.value } : d))} />
              </FormField>
              <FormField label={t('tarification.unitMaster.maxWeightLabel')} htmlFor="um-maxweight">
                <input id="um-maxweight" type="number" step="0.1" value={draft.maxWeightKg} onChange={(e) => setDraft((d) => (d ? { ...d, maxWeightKg: e.target.value } : d))} />
              </FormField>
              <FormField label={t('tarification.unitMaster.volumeLabel')} htmlFor="um-volume">
                <input id="um-volume" type="number" step="0.001" value={draft.defaultVolumeM3} onChange={(e) => setDraft((d) => (d ? { ...d, defaultVolumeM3: e.target.value } : d))} />
              </FormField>
            </div>
            <div className="issued-items-form-row">
              <FormField label={t('tarification.unitMaster.ldmLabel')} htmlFor="um-ldm">
                <input id="um-ldm" type="number" step="0.01" value={draft.defaultLoadingMeters} onChange={(e) => setDraft((d) => (d ? { ...d, defaultLoadingMeters: e.target.value } : d))} />
              </FormField>
              <FormField label={t('tarification.unitMaster.placesLabel')} htmlFor="um-places">
                <input id="um-places" type="number" step="0.5" value={draft.defaultPalletPlaces} onChange={(e) => setDraft((d) => (d ? { ...d, defaultPalletPlaces: e.target.value } : d))} />
              </FormField>
            </div>
            <div className="issued-items-form-row">
              <label className="tof-checkbox">
                <input type="checkbox" checked={draft.allowForOrderEntry} onChange={(e) => setDraft((d) => (d ? { ...d, allowForOrderEntry: e.target.checked } : d))} />
                {t('tarification.unitMaster.allowOrder')}
              </label>
              <label className="tof-checkbox">
                <input type="checkbox" checked={draft.allowForPricing} onChange={(e) => setDraft((d) => (d ? { ...d, allowForPricing: e.target.checked } : d))} />
                {t('tarification.unitMaster.allowPricing')}
              </label>
              <label className="tof-checkbox">
                <input type="checkbox" checked={draft.allowForInventory} onChange={(e) => setDraft((d) => (d ? { ...d, allowForInventory: e.target.checked } : d))} />
                {t('tarification.unitMaster.allowInventory')}
              </label>
              <label className="tof-checkbox">
                <input type="checkbox" checked={draft.isActive} onChange={(e) => setDraft((d) => (d ? { ...d, isActive: e.target.checked } : d))} />
                {t('tarification.common.active')}
              </label>
            </div>
          </form>
        </Modal>
      )}
    </section>
  )
}
