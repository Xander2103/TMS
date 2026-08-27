import { useCallback, useEffect, useState } from 'react'
import { Button } from '../../../components/ui/Button'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import { useLocale } from '../../../i18n/localeContext'
import { describeApiError } from '../../../api/problemDetails'
import {
  getCustomerPricingConfig,
  listUnitTypeSettings,
  saveCustomerPricingConfig,
  type CustomerPricingConfig,
  type CustomerUnitInput,
  type UnitTypeSettings,
} from '../../tarification/api/pricingApi'

interface CustomerUnitsPanelProps {
  customerId: string
}

/**
 * Customer units (spec §3): which global units this customer commonly uses, with a customer
 * label, external EDI/Excel codes, favourite flag and sort order. The global unit is never
 * duplicated — these rows only configure how the customer uses it.
 */
export function CustomerUnitsPanel({ customerId }: CustomerUnitsPanelProps) {
  const { hasPermission } = useAuth()
  const { showError, showSuccess } = useToast()
  const { t } = useLocale()
  const canView = hasPermission('tariffs.view') || hasPermission('tariffs.manage')
  const canManage = hasPermission('tariffs.manage')

  const [config, setConfig] = useState<CustomerPricingConfig | null>(null)
  const [units, setUnits] = useState<UnitTypeSettings[]>([])
  const [loadError, setLoadError] = useState<string | null>(null)
  const [addUnitId, setAddUnitId] = useState('')

  const reload = useCallback(() => {
    if (!canView) return
    Promise.all([getCustomerPricingConfig(customerId), listUnitTypeSettings().catch(() => [] as UnitTypeSettings[])])
      .then(([configData, unitData]) => {
        setConfig(configData)
        setUnits(unitData)
        setLoadError(null)
      })
      .catch(() => setLoadError(t('customers.units.loadFailed')))
  }, [customerId, canView, t])

  useEffect(() => {
    reload()
  }, [reload])

  if (!canView) return null
  if (loadError) return <p className="placeholder-text">{loadError}</p>
  if (!config) return <p className="placeholder-text">{t('customers.units.loading')}</p>

  const configured = config.preferredUnits
  const configuredIds = new Set(configured.map((u) => u.unitTypeId))
  const available = units.filter((u) => u.isActive && !configuredIds.has(u.id))

  async function save(nextUnits: CustomerUnitInput[], message?: string) {
    if (!config) return
    try {
      const saved = await saveCustomerPricingConfig(customerId, {
        units: nextUnits,
        optionPrices: config.serviceOptions.map((o) => ({ serviceOptionId: o.serviceOptionId, value: o.customerValue })),
      })
      setConfig(saved)
      if (message) showSuccess(message)
    } catch (err) {
      showError(describeApiError(err, t('customers.units.saveFailed')).message)
    }
  }

  const asInputs = (): CustomerUnitInput[] =>
    configured.map((u) => ({
      unitTypeId: u.unitTypeId,
      sortOrder: u.sortOrder,
      customerLabel: u.customerLabel,
      ediCode: u.ediCode,
      excelCode: u.excelCode,
      isFavourite: u.isFavourite,
    }))

  function updateUnit(unitTypeId: string, patch: Partial<CustomerUnitInput>, message?: string) {
    void save(asInputs().map((u) => (u.unitTypeId === unitTypeId ? { ...u, ...patch } : u)), message)
  }

  function move(unitTypeId: string, delta: -1 | 1) {
    const ordered = asInputs().sort((a, b) => a.sortOrder - b.sortOrder)
    const index = ordered.findIndex((u) => u.unitTypeId === unitTypeId)
    const target = index + delta
    if (index < 0 || target < 0 || target >= ordered.length) return
    const swapped = [...ordered]
    ;[swapped[index], swapped[target]] = [swapped[target], swapped[index]]
    void save(swapped.map((u, i) => ({ ...u, sortOrder: i })))
  }

  function addUnit() {
    if (!addUnitId) return
    void save(
      [...asInputs(), {
        unitTypeId: addUnitId,
        sortOrder: configured.length,
        customerLabel: null,
        ediCode: null,
        excelCode: null,
        isFavourite: true,
      }],
      t('customers.units.unitAdded'),
    )
    setAddUnitId('')
  }

  return (
    <section className="customer-panel">
      <div className="customer-panel-header">
        <h3>{t('customers.units.title')}</h3>
      </div>
      <p className="customer-form-muted">{t('customers.units.explanation')}</p>

      {configured.length === 0 && <p className="placeholder-text">{t('customers.units.empty')}</p>}
      {configured.length > 0 && (
        <table className="issued-items-table">
          <thead>
            <tr>
              <th>{t('customers.units.columnUnit')}</th>
              <th>{t('customers.units.columnCustomerLabel')}</th>
              <th>{t('customers.units.columnEdiCode')}</th>
              <th>{t('customers.units.columnExcelCode')}</th>
              <th>{t('customers.units.columnFavourite')}</th>
              {canManage && <th>{t('customers.units.columnOrder')}</th>}
              {canManage && <th aria-label={t('customers.units.actionsAria')} />}
            </tr>
          </thead>
          <tbody>
            {configured.map((unit) => (
              <tr key={unit.unitTypeId}>
                <td>{unit.name}</td>
                <td>
                  <input
                    aria-label={t('customers.units.customerLabelAria', { name: unit.name })}
                    defaultValue={unit.customerLabel ?? ''}
                    placeholder={unit.name}
                    maxLength={150}
                    disabled={!canManage}
                    onBlur={(e) => {
                      const value = e.target.value.trim() === '' ? null : e.target.value.trim()
                      if (value !== unit.customerLabel) updateUnit(unit.unitTypeId, { customerLabel: value })
                    }}
                  />
                </td>
                <td>
                  <input
                    aria-label={t('customers.units.ediCodeAria', { name: unit.name })}
                    defaultValue={unit.ediCode ?? ''}
                    maxLength={50}
                    disabled={!canManage}
                    onBlur={(e) => {
                      const value = e.target.value.trim() === '' ? null : e.target.value.trim()
                      if (value !== unit.ediCode) updateUnit(unit.unitTypeId, { ediCode: value })
                    }}
                  />
                </td>
                <td>
                  <input
                    aria-label={t('customers.units.excelCodeAria', { name: unit.name })}
                    defaultValue={unit.excelCode ?? ''}
                    maxLength={50}
                    disabled={!canManage}
                    onBlur={(e) => {
                      const value = e.target.value.trim() === '' ? null : e.target.value.trim()
                      if (value !== unit.excelCode) updateUnit(unit.unitTypeId, { excelCode: value })
                    }}
                  />
                </td>
                <td>
                  <button
                    type="button"
                    className="issued-items-link"
                    aria-label={t('customers.units.favouriteAria', { name: unit.name })}
                    aria-pressed={unit.isFavourite}
                    disabled={!canManage}
                    onClick={() => updateUnit(unit.unitTypeId, { isFavourite: !unit.isFavourite })}
                  >
                    {unit.isFavourite ? '★' : '☆'}
                  </button>
                </td>
                {canManage && (
                  <td>
                    <button
                      type="button"
                      className="issued-items-link"
                      aria-label={t('customers.units.moveUpAria', { name: unit.name })}
                      onClick={() => move(unit.unitTypeId, -1)}
                    >
                      ↑
                    </button>
                    <button
                      type="button"
                      className="issued-items-link"
                      aria-label={t('customers.units.moveDownAria', { name: unit.name })}
                      onClick={() => move(unit.unitTypeId, 1)}
                    >
                      ↓
                    </button>
                  </td>
                )}
                {canManage && (
                  <td className="issued-items-row-actions">
                    <button
                      type="button"
                      className="issued-items-link issued-items-link-danger"
                      onClick={() =>
                        void save(asInputs().filter((u) => u.unitTypeId !== unit.unitTypeId), t('customers.units.unitRemoved'))
                      }
                    >
                      {t('ui.actions.delete')}
                    </button>
                  </td>
                )}
              </tr>
            ))}
          </tbody>
        </table>
      )}

      {canManage && available.length > 0 && (
        <div className="customer-panel-header">
          <select aria-label={t('customers.units.addUnitAria')} value={addUnitId} onChange={(e) => setAddUnitId(e.target.value)}>
            <option value="">{t('customers.units.chooseUnit')}</option>
            {available.map((unit) => (
              <option key={unit.id} value={unit.id}>
                {unit.name}
              </option>
            ))}
          </select>
          <Button variant="secondary" onClick={addUnit} disabled={!addUnitId}>
            {t('customers.units.addUnit')}
          </Button>
        </div>
      )}
    </section>
  )
}
