import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import { localizeApiError } from '../../../api/problemDetails'
import { useLocale, type TranslateFn } from '../../../i18n/localeContext'
import { SURCHARGE_KIND_LABELS, type SurchargeKind } from '../types'
import { formatServiceValue } from '../serviceValueFormat'
import {
  createServiceOption,
  deleteServiceOption,
  listServiceOptions,
  listUnitTypeSettings,
  SCAN_QUANTITY_SOURCES,
  SERVICE_QUANTITY_SOURCE_LABELS,
  updateServiceOption,
  type ServiceOption,
  type ServiceQuantitySource,
  type ServiceTimeCondition,
  type UnitTypeSettings,
} from '../api/pricingApi'
import { listWarehouses } from '../../warehousing/api/warehousingApi'
import type { Warehouse } from '../../warehousing/types'
import { listSalesCategories, type SalesCategory } from '../../accounting/api/accountingApi'

interface TimeConditionDraft {
  kind: ServiceTimeCondition['kind']
  stopScope: ServiceTimeCondition['stopScope']
  timeOfDay: string
  priority: string
  allowStacking: boolean
}

interface OptionDraft {
  option: ServiceOption | null
  code: string
  name: string
  kind: SurchargeKind
  defaultValue: string
  description: string
  invoiceDescription: string
  selectableInOrders: boolean
  isActive: boolean
  unitTypeId: string
  autoApply: boolean
  onlyForAdr: boolean
  warehouseIds: string[]
  timeConditions: TimeConditionDraft[]
  salesCategoryId: string
  quantitySource: ServiceQuantitySource
}

/** Vertaalsleutels — renderen als t(TIME_CONDITION_KIND_LABELS[kind]). */
const TIME_CONDITION_KIND_LABELS: Record<ServiceTimeCondition['kind'], string> = {
  StopTimeBefore: 'tarification.services.timeConditionKind.StopTimeBefore',
  StopTimeAfter: 'tarification.services.timeConditionKind.StopTimeAfter',
  AppointmentRequired: 'tarification.services.timeConditionKind.AppointmentRequired',
  Weekend: 'tarification.services.timeConditionKind.Weekend',
  Holiday: 'tarification.services.timeConditionKind.Holiday',
}

/** Vertaalsleutels — renderen als t(TIME_CONDITION_SCOPE_LABELS[scope]). */
const TIME_CONDITION_SCOPE_LABELS: Record<ServiceTimeCondition['stopScope'], string> = {
  Any: 'tarification.services.timeConditionScope.Any',
  Loading: 'tarification.services.timeConditionScope.Loading',
  Unloading: 'tarification.services.timeConditionScope.Unloading',
}

/** "Lossen vóór 10:00 (prioriteit 1)" — badge/summary text of a configured time condition. */
function timeConditionSummary(t: TranslateFn, condition: ServiceTimeCondition): string {
  const scope = t(TIME_CONDITION_SCOPE_LABELS[condition.stopScope])
  const time = condition.timeOfDay?.slice(0, 5)
  const core =
    condition.kind === 'StopTimeBefore'
      ? t('tarification.services.summaryBefore', { scope, time: time ?? '?' })
      : condition.kind === 'StopTimeAfter'
        ? t('tarification.services.summaryAfter', { scope, time: time ?? '?' })
        : condition.kind === 'AppointmentRequired'
          ? t('tarification.services.summaryAppointment', { scope })
          : condition.kind === 'Holiday'
            ? t('tarification.services.summaryHoliday', { scope })
            : t('tarification.services.summaryWeekend', { scope })
  const extras = [
    condition.priority !== 0 ? t('tarification.services.summaryPriority', { priority: condition.priority }) : null,
    condition.allowStacking ? t('tarification.services.summaryStacks') : null,
  ].filter(Boolean)
  return extras.length > 0 ? `${core} (${extras.join(', ')})` : core
}

/** Vertaalsleutels per berekeningswijze voor het waardelabel. */
const VALUE_LABEL_BY_KIND: Partial<Record<SurchargeKind, string>> = {
  Percent: 'tarification.services.valueLabel.Percent',
  PerHour: 'tarification.services.valueLabel.PerHour',
  PerStop: 'tarification.services.valueLabel.PerStop',
  PerUnit: 'tarification.services.valueLabel.PerUnit',
  PerOrderLine: 'tarification.services.valueLabel.PerOrderLine',
  PerKg: 'tarification.services.valueLabel.PerKg',
  PerM3: 'tarification.services.valueLabel.PerM3',
  PerLdm: 'tarification.services.valueLabel.PerLdm',
  PerDay: 'tarification.services.valueLabel.PerDay',
  PerPalletDay: 'tarification.services.valueLabel.PerPalletDay',
  PerKm: 'tarification.services.valueLabel.PerKm',
}

/**
 * Global admin configuration of services & surcharges (spec §5/6): the defaults every
 * transport order starts from. Customer-specific overrides live on the customer's
 * "Tarieven & toeslagen" tab; nothing here is hardcoded in the order form.
 */
export function ServiceOptionsEditor() {
  const { t } = useLocale()
  const { hasPermission } = useAuth()
  const { showSuccess, showError } = useToast()
  const canView = hasPermission('tariffs.view') || hasPermission('tariffs.manage')
  const canManage = hasPermission('tariffs.manage')

  const [options, setOptions] = useState<ServiceOption[] | null>(null)
  const [units, setUnits] = useState<UnitTypeSettings[]>([])
  const [warehouses, setWarehouses] = useState<Warehouse[]>([])
  const [salesCategories, setSalesCategories] = useState<SalesCategory[]>([])
  const [loadErrorKey, setLoadErrorKey] = useState<string | null>(null)
  const [draft, setDraft] = useState<OptionDraft | null>(null)
  const [draftError, setDraftError] = useState<string | null>(null)
  const [deleteTarget, setDeleteTarget] = useState<ServiceOption | null>(null)
  const [busy, setBusy] = useState(false)

  const reload = useCallback(() => {
    if (!canView) return
    listServiceOptions(true)
      .then((data) => {
        setOptions(data)
        setLoadErrorKey(null)
      })
      .catch(() => setLoadErrorKey('tarification.services.loadError'))
    listUnitTypeSettings()
      .then(setUnits)
      .catch(() => {})
    // Warehouses feed the optional warehouse condition; unavailable (no permission) is fine.
    listWarehouses()
      .then(setWarehouses)
      .catch(() => {})
    // Sales codes feed the optional verkoopcategorie; unavailable is fine (field stays empty).
    listSalesCategories()
      .then(setSalesCategories)
      .catch(() => {})
  }, [canView])

  useEffect(() => {
    reload()
  }, [reload])

  if (!canView) return <p className="placeholder-text">{t('tarification.services.noViewPermission')}</p>
  if (loadErrorKey) return <p className="placeholder-text">{t(loadErrorKey)}</p>
  if (options === null) return <p className="placeholder-text">{t('tarification.services.loading')}</p>

  function openDraft(option: ServiceOption | null) {
    setDraftError(null)
    setDraft(
      option
        ? {
            option,
            code: option.code,
            name: option.name,
            kind: option.kind,
            defaultValue: String(option.defaultValue),
            description: option.description ?? '',
            invoiceDescription: option.invoiceDescription ?? '',
            selectableInOrders: option.selectableInOrders,
            isActive: option.isActive,
            unitTypeId: option.unitTypeId ?? '',
            autoApply: option.autoApply,
            onlyForAdr: option.onlyForAdr,
            warehouseIds: option.warehouseIds ?? [],
            salesCategoryId: option.salesCategoryId ?? '',
            quantitySource: option.quantitySource ?? 'Ordered',
            timeConditions: (option.timeConditions ?? []).map((c) => ({
              kind: c.kind,
              stopScope: c.stopScope,
              timeOfDay: c.timeOfDay?.slice(0, 5) ?? '',
              priority: String(c.priority),
              allowStacking: c.allowStacking,
            })),
          }
        : {
            option: null,
            code: '',
            name: '',
            kind: 'Fixed',
            defaultValue: '0',
            description: '',
            invoiceDescription: '',
            selectableInOrders: true,
            isActive: true,
            unitTypeId: '',
            autoApply: false,
            onlyForAdr: false,
            warehouseIds: [],
            timeConditions: [],
            salesCategoryId: '',
            quantitySource: 'Ordered',
          },
    )
  }

  async function submit(event: FormEvent) {
    event.preventDefault()
    if (!draft) return
    setBusy(true)
    try {
      const input = {
        code: draft.code.trim(),
        name: draft.name.trim(),
        kind: draft.kind,
        defaultValue: Number(draft.defaultValue) || 0,
        isActive: draft.isActive,
        sortOrder: draft.option?.sortOrder ?? options?.length ?? 0,
        description: draft.description.trim() || null,
        invoiceDescription: draft.invoiceDescription.trim() || null,
        selectableInOrders: draft.selectableInOrders,
        unitTypeId: draft.kind === 'PerUnit' ? draft.unitTypeId || null : null,
        autoApply: draft.autoApply,
        onlyForAdr: draft.onlyForAdr,
        warehouseIds: draft.warehouseIds,
        salesCategoryId: draft.salesCategoryId || null,
        quantitySource: draft.quantitySource,
        timeConditions: draft.timeConditions.map((c) => ({
          kind: c.kind,
          stopScope: c.stopScope,
          timeOfDay: c.kind === 'StopTimeBefore' || c.kind === 'StopTimeAfter' ? c.timeOfDay || null : null,
          priority: Number(c.priority) || 0,
          allowStacking: c.allowStacking,
        })),
      }
      if (draft.option) {
        await updateServiceOption(draft.option.id, input)
        showSuccess(t('tarification.services.updated'))
      } else {
        await createServiceOption(input)
        showSuccess(t('tarification.services.added'))
      }
      setDraft(null)
      reload()
    } catch (err) {
      setDraftError(localizeApiError(t, err, t('tarification.services.saveError')))
    } finally {
      setBusy(false)
    }
  }

  const valueLabel = t(draft ? (VALUE_LABEL_BY_KIND[draft.kind] ?? 'tarification.services.valueLabel.default') : 'tarification.services.valueLabel.default')
  const activeUnits = units.filter((u) => u.isActive)

  return (
    <div>
      {canManage && (
        <div className="tof-documents-toolbar">
          <Button onClick={() => openDraft(null)}>{t('tarification.services.addService')}</Button>
        </div>
      )}
      <table className="issued-items-table">
        <thead>
          <tr>
            <th>{t('tarification.common.name')}</th>
            <th>{t('tarification.services.colKind')}</th>
            <th>{t('tarification.services.colDefault')}</th>
            <th>{t('tarification.services.colInOrders')}</th>
            <th>{t('tarification.common.status')}</th>
            {canManage && <th aria-label={t('tarification.common.actions')} />}
          </tr>
        </thead>
        <tbody>
          {options.map((option) => (
            <tr key={option.id}>
              <td>
                {option.name}
                {option.description && <div className="customer-form-muted">{option.description}</div>}
              </td>
              <td>{t(SURCHARGE_KIND_LABELS[option.kind])}</td>
              <td>
                {formatServiceValue(option.kind, option.defaultValue, option.unitTypeName, t)}
                {option.autoApply && <Badge tone="info">{t('tarification.services.badgeAuto')}</Badge>}
                {option.quantitySource && option.quantitySource !== 'Ordered' && (
                  <Badge tone="info">{t(SERVICE_QUANTITY_SOURCE_LABELS[option.quantitySource])}</Badge>
                )}
                {option.onlyForAdr && <Badge tone="warning">{t('tarification.services.badgeAdr')}</Badge>}
                {(option.warehouseNames?.length ?? 0) > 0 && (
                  <Badge tone="warning">{t('tarification.services.badgeWarehouse', { names: option.warehouseNames!.join(', ') })}</Badge>
                )}
                {(option.timeConditions ?? []).map((condition, index) => (
                  <Badge key={index} tone="info">
                    {timeConditionSummary(t, condition)}
                  </Badge>
                ))}
              </td>
              <td>{option.selectableInOrders ? t('tarification.common.yes') : t('tarification.common.no')}</td>
              <td>
                <Badge tone={option.isActive ? 'success' : 'neutral'}>
                  {option.isActive ? t('tarification.common.active') : t('tarification.common.inactive')}
                </Badge>
              </td>
              {canManage && (
                <td className="issued-items-row-actions">
                  <button type="button" className="issued-items-link" onClick={() => openDraft(option)}>
                    {t('ui.actions.edit')}
                  </button>
                  <button type="button" className="issued-items-link issued-items-link-danger" onClick={() => setDeleteTarget(option)}>
                    {t('ui.actions.delete')}
                  </button>
                </td>
              )}
            </tr>
          ))}
        </tbody>
      </table>

      {draft && (
        <Modal
          title={draft.option ? t('tarification.services.editTitle', { name: draft.option.name }) : t('tarification.services.addTitle')}
          onClose={() => setDraft(null)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setDraft(null)} disabled={busy}>
                {t('ui.actions.cancel')}
              </Button>
              <Button type="submit" form="service-option-form" disabled={busy}>
                {t('ui.actions.save')}
              </Button>
            </>
          }
        >
          <form id="service-option-form" className="issued-items-form" onSubmit={submit} noValidate>
            {draftError && (
              <div className="issued-items-form-error" role="alert">
                {draftError}
              </div>
            )}
            <div className="issued-items-form-row">
              <FormField label={t('tarification.unitMaster.codeLabel')} htmlFor="opt-code" required hint={t('tarification.services.codeHint')}>
                <input id="opt-code" value={draft.code} onChange={(e) => setDraft((d) => (d ? { ...d, code: e.target.value } : d))} maxLength={50} />
              </FormField>
              <FormField label={t('tarification.common.name')} htmlFor="opt-name" required>
                <input id="opt-name" value={draft.name} onChange={(e) => setDraft((d) => (d ? { ...d, name: e.target.value } : d))} maxLength={200} />
              </FormField>
            </div>
            <div className="issued-items-form-row">
              <FormField label={t('tarification.services.colKind')} htmlFor="opt-kind" hint={t('tarification.services.kindHint')}>
                <select id="opt-kind" value={draft.kind} onChange={(e) => setDraft((d) => (d ? { ...d, kind: e.target.value as SurchargeKind } : d))}>
                  {Object.entries(SURCHARGE_KIND_LABELS).map(([value, labelKey]) => (
                    <option key={value} value={value}>
                      {t(labelKey)}
                    </option>
                  ))}
                </select>
              </FormField>
              <FormField label={valueLabel} htmlFor="opt-value">
                <input id="opt-value" type="number" step="0.01" value={draft.defaultValue} onChange={(e) => setDraft((d) => (d ? { ...d, defaultValue: e.target.value } : d))} />
              </FormField>
            </div>
            <FormField
              label={t('tarification.services.quantitySourceLabel')}
              htmlFor="opt-quantity-source"
              hint={t('tarification.services.quantitySourceHint')}
            >
              <select
                id="opt-quantity-source"
                value={draft.quantitySource}
                onChange={(e) => setDraft((d) => (d ? { ...d, quantitySource: e.target.value as ServiceQuantitySource } : d))}
              >
                {Object.entries(SERVICE_QUANTITY_SOURCE_LABELS).map(([value, labelKey]) => (
                  <option key={value} value={value}>
                    {t(labelKey)}
                  </option>
                ))}
              </select>
            </FormField>
            {draft.kind === 'PerUnit' && (
              <FormField
                label={t('tarification.common.unit')}
                htmlFor="opt-unit"
                required={!SCAN_QUANTITY_SOURCES.includes(draft.quantitySource)}
                hint={
                  SCAN_QUANTITY_SOURCES.includes(draft.quantitySource)
                    ? t('tarification.services.unitOptionalHint')
                    : t('tarification.services.unitRequiredHint')
                }
              >
                <select id="opt-unit" value={draft.unitTypeId} onChange={(e) => setDraft((d) => (d ? { ...d, unitTypeId: e.target.value } : d))}>
                  <option value="">{t('tarification.grid.chooseUnit')}</option>
                  {activeUnits.map((unit) => (
                    <option key={unit.id} value={unit.id}>
                      {unit.name}
                    </option>
                  ))}
                </select>
              </FormField>
            )}
            <FormField label={t('tarification.services.descLabel')} htmlFor="opt-desc">
              <input id="opt-desc" value={draft.description} onChange={(e) => setDraft((d) => (d ? { ...d, description: e.target.value } : d))} maxLength={1000} />
            </FormField>
            <FormField label={t('tarification.services.invoiceDescLabel')} htmlFor="opt-invoice" hint={t('tarification.services.invoiceDescHint')}>
              <input id="opt-invoice" value={draft.invoiceDescription} onChange={(e) => setDraft((d) => (d ? { ...d, invoiceDescription: e.target.value } : d))} maxLength={300} />
            </FormField>
            <FormField label={t('tarification.services.salesCatLabel')} htmlFor="opt-sales-cat" hint={t('tarification.services.salesCatHint')}>
              <select id="opt-sales-cat" value={draft.salesCategoryId} onChange={(e) => setDraft((d) => (d ? { ...d, salesCategoryId: e.target.value } : d))}>
                <option value="">{t('tarification.services.salesCatDefault')}</option>
                {salesCategories.map((c) => (
                  <option key={c.id} value={c.id}>{c.name}</option>
                ))}
              </select>
            </FormField>
            <label className="tof-checkbox">
              <input type="checkbox" checked={draft.selectableInOrders} onChange={(e) => setDraft((d) => (d ? { ...d, selectableInOrders: e.target.checked } : d))} />
              {t('tarification.services.selectable')}
            </label>
            <label className="tof-checkbox">
              <input type="checkbox" checked={draft.autoApply} onChange={(e) => setDraft((d) => (d ? { ...d, autoApply: e.target.checked } : d))} />
              {t('tarification.services.autoApply')}
            </label>
            <label className="tof-checkbox">
              <input type="checkbox" checked={draft.onlyForAdr} onChange={(e) => setDraft((d) => (d ? { ...d, onlyForAdr: e.target.checked } : d))} />
              {t('tarification.services.badgeAdr')}
            </label>
            {warehouses.length > 0 && (
              <FormField
                label={t('tarification.services.warehousesLabel')}
                hint={t('tarification.services.warehousesHint')}
              >
                <div>
                  {warehouses.filter((w) => w.isActive || draft.warehouseIds.includes(w.id)).map((warehouse) => (
                    <label key={warehouse.id} className="tof-checkbox">
                      <input
                        type="checkbox"
                        checked={draft.warehouseIds.includes(warehouse.id)}
                        onChange={(e) =>
                          setDraft((d) =>
                            d
                              ? {
                                  ...d,
                                  warehouseIds: e.target.checked
                                    ? [...d.warehouseIds, warehouse.id]
                                    : d.warehouseIds.filter((id) => id !== warehouse.id),
                                }
                              : d,
                          )
                        }
                      />
                      {warehouse.name}
                    </label>
                  ))}
                </div>
              </FormField>
            )}
            <FormField
              label={t('tarification.services.timeCondLabel')}
              hint={t('tarification.services.timeCondHint')}
            >
              <div>
                {draft.timeConditions.map((condition, index) => (
                  <div key={index} className="issued-items-form-row" data-testid="time-condition-row">
                    <select
                      aria-label={t('tarification.services.ariaCondition')}
                      value={condition.kind}
                      onChange={(e) =>
                        setDraft((d) => {
                          if (!d) return d
                          const next = [...d.timeConditions]
                          next[index] = { ...next[index], kind: e.target.value as TimeConditionDraft['kind'] }
                          return { ...d, timeConditions: next }
                        })
                      }
                    >
                      {Object.entries(TIME_CONDITION_KIND_LABELS).map(([value, labelKey]) => (
                        <option key={value} value={value}>
                          {t(labelKey)}
                        </option>
                      ))}
                    </select>
                    <select
                      aria-label={t('tarification.services.ariaStopType')}
                      value={condition.stopScope}
                      onChange={(e) =>
                        setDraft((d) => {
                          if (!d) return d
                          const next = [...d.timeConditions]
                          next[index] = { ...next[index], stopScope: e.target.value as TimeConditionDraft['stopScope'] }
                          return { ...d, timeConditions: next }
                        })
                      }
                    >
                      {Object.entries(TIME_CONDITION_SCOPE_LABELS).map(([value, labelKey]) => (
                        <option key={value} value={value}>
                          {t(labelKey)}
                        </option>
                      ))}
                    </select>
                    {(condition.kind === 'StopTimeBefore' || condition.kind === 'StopTimeAfter') && (
                      <input
                        aria-label={t('tarification.services.ariaHour')}
                        type="time"
                        value={condition.timeOfDay}
                        onChange={(e) =>
                          setDraft((d) => {
                            if (!d) return d
                            const next = [...d.timeConditions]
                            next[index] = { ...next[index], timeOfDay: e.target.value }
                            return { ...d, timeConditions: next }
                          })
                        }
                      />
                    )}
                    <input
                      aria-label={t('tarification.services.ariaPriority')}
                      type="number"
                      value={condition.priority}
                      onChange={(e) =>
                        setDraft((d) => {
                          if (!d) return d
                          const next = [...d.timeConditions]
                          next[index] = { ...next[index], priority: e.target.value }
                          return { ...d, timeConditions: next }
                        })
                      }
                      style={{ width: 80 }}
                    />
                    <label className="tof-checkbox">
                      <input
                        type="checkbox"
                        checked={condition.allowStacking}
                        onChange={(e) =>
                          setDraft((d) => {
                            if (!d) return d
                            const next = [...d.timeConditions]
                            next[index] = { ...next[index], allowStacking: e.target.checked }
                            return { ...d, timeConditions: next }
                          })
                        }
                      />
                      {t('tarification.services.stacksCheckbox')}
                    </label>
                    <button
                      type="button"
                      className="issued-items-link issued-items-link-danger"
                      onClick={() =>
                        setDraft((d) =>
                          d ? { ...d, timeConditions: d.timeConditions.filter((_, i) => i !== index) } : d,
                        )
                      }
                    >
                      {t('ui.actions.delete')}
                    </button>
                  </div>
                ))}
                <Button
                  variant="secondary"
                  onClick={() =>
                    setDraft((d) =>
                      d
                        ? {
                            ...d,
                            timeConditions: [
                              ...d.timeConditions,
                              { kind: 'StopTimeBefore', stopScope: 'Unloading', timeOfDay: '', priority: '0', allowStacking: false },
                            ],
                          }
                        : d,
                    )
                  }
                >
                  {t('tarification.services.addTimeCondition')}
                </Button>
              </div>
            </FormField>
            <label className="tof-checkbox">
              <input type="checkbox" checked={draft.isActive} onChange={(e) => setDraft((d) => (d ? { ...d, isActive: e.target.checked } : d))} />
              {t('tarification.common.active')}
            </label>
          </form>
        </Modal>
      )}

      {deleteTarget && (
        <ConfirmDialog
          title={t('tarification.services.deleteTitle')}
          message={t('tarification.services.deleteMessage', { name: deleteTarget.name })}
          confirmLabel={t('ui.actions.delete')}
          destructive
          onConfirm={async () => {
            const target = deleteTarget
            setDeleteTarget(null)
            try {
              await deleteServiceOption(target.id)
              showSuccess(t('tarification.services.deleted'))
              reload()
            } catch (err) {
              showError(localizeApiError(t, err, t('tarification.services.deleteError')))
            }
          }}
          onCancel={() => setDeleteTarget(null)}
        />
      )}
    </div>
  )
}
