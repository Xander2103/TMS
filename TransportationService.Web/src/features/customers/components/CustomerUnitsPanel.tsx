import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { DataTable, type Column } from '../../../components/ui/DataTable'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { SearchableSelect } from '../../../components/ui/SearchableSelect'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import { useLocale } from '../../../i18n/localeContext'
import { describeApiError } from '../../../api/problemDetails'
import {
  getCustomerPricingConfig,
  listUnitTypeSettings,
  saveCustomerPricingConfig,
  type CustomerPreferredUnit,
  type CustomerPricingConfig,
  type CustomerUnitInput,
  type UnitTypeSettings,
} from '../../tarification/api/pricingApi'

interface CustomerUnitsPanelProps {
  customerId: string
}

/** Modal state for one unit mapping; null unitTypeId while the user still has to pick one. */
interface MappingDraft {
  /** The row being edited, or null for "+ Eenheid koppelen". */
  existing: CustomerPreferredUnit | null
  unitTypeId: string | null
  customerLabel: string
  ediCode: string
  excelCode: string
  isFavourite: boolean
}

/**
 * Customer units (spec §3): which global units this customer commonly uses, with a customer
 * label, external EDI/Excel codes, favourite flag and sort order. The global unit is never
 * duplicated — these rows only configure how the customer uses it. Editing is modal-based
 * with explicit save (consistent with the rest of the tariffs tab — no blur-autosave).
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
  const [draft, setDraft] = useState<MappingDraft | null>(null)
  const [draftError, setDraftError] = useState<string | null>(null)
  const [deleteTarget, setDeleteTarget] = useState<CustomerPreferredUnit | null>(null)
  const [busy, setBusy] = useState(false)

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
  const availableForAdd = units.filter((u) => u.isActive && !configuredIds.has(u.id))

  const asInputs = (): CustomerUnitInput[] =>
    configured.map((u) => ({
      unitTypeId: u.unitTypeId,
      sortOrder: u.sortOrder,
      customerLabel: u.customerLabel,
      ediCode: u.ediCode,
      excelCode: u.excelCode,
      isFavourite: u.isFavourite,
    }))

  async function save(nextUnits: CustomerUnitInput[], message: string): Promise<boolean> {
    if (!config) return false
    setBusy(true)
    try {
      const saved = await saveCustomerPricingConfig(customerId, {
        units: nextUnits,
        // Option rows ABSENT from the request are left untouched server-side. Echoing them back
        // with only `value` (as this panel used to) silently wiped disabled/window/auto-apply
        // overrides — so a units save must never mention option prices at all.
        optionPrices: [],
      })
      setConfig(saved)
      showSuccess(message)
      return true
    } catch (err) {
      const message2 = describeApiError(err, t('customers.units.saveFailed')).message
      if (draft) setDraftError(message2)
      else showError(message2)
      return false
    } finally {
      setBusy(false)
    }
  }

  function openDraft(row: CustomerPreferredUnit | null) {
    setDraftError(null)
    setDraft(
      row
        ? {
            existing: row,
            unitTypeId: row.unitTypeId,
            customerLabel: row.customerLabel ?? '',
            ediCode: row.ediCode ?? '',
            excelCode: row.excelCode ?? '',
            isFavourite: row.isFavourite,
          }
        : { existing: null, unitTypeId: null, customerLabel: '', ediCode: '', excelCode: '', isFavourite: true },
    )
  }

  async function submitDraft(event: FormEvent) {
    event.preventDefault()
    if (!draft) return
    if (!draft.unitTypeId) {
      setDraftError(t('customers.units.chooseUnitError'))
      return
    }
    const row: CustomerUnitInput = {
      unitTypeId: draft.unitTypeId,
      sortOrder: draft.existing?.sortOrder ?? configured.length,
      customerLabel: draft.customerLabel.trim() || null,
      ediCode: draft.ediCode.trim() || null,
      excelCode: draft.excelCode.trim() || null,
      isFavourite: draft.isFavourite,
    }
    const next = draft.existing
      ? asInputs().map((u) => (u.unitTypeId === draft.existing!.unitTypeId ? row : u))
      : [...asInputs(), row]
    const ok = await save(next, draft.existing ? t('customers.units.unitUpdated') : t('customers.units.unitAdded'))
    if (ok) setDraft(null)
  }

  async function handleDelete() {
    if (!deleteTarget) return
    const target = deleteTarget
    setDeleteTarget(null)
    await save(
      asInputs().filter((u) => u.unitTypeId !== target.unitTypeId),
      t('customers.units.unitRemoved'),
    )
  }

  function move(unitTypeId: string, delta: -1 | 1) {
    const ordered = asInputs().sort((a, b) => a.sortOrder - b.sortOrder)
    const index = ordered.findIndex((u) => u.unitTypeId === unitTypeId)
    const target = index + delta
    if (index < 0 || target < 0 || target >= ordered.length) return
    const swapped = [...ordered]
    ;[swapped[index], swapped[target]] = [swapped[target], swapped[index]]
    void save(swapped.map((u, i) => ({ ...u, sortOrder: i })), t('customers.units.orderSaved'))
  }

  const columns: Column<CustomerPreferredUnit>[] = [
    { key: 'unit', header: t('customers.units.columnUnit'), render: (row) => row.name },
    { key: 'label', header: t('customers.units.columnCustomerLabel'), render: (row) => row.customerLabel ?? '—' },
    {
      key: 'edi',
      header: t('customers.units.columnEdiCode'),
      render: (row) => (row.ediCode ? <code>{row.ediCode}</code> : '—'),
    },
    {
      key: 'excel',
      header: t('customers.units.columnExcelCode'),
      render: (row) => (row.excelCode ? <code>{row.excelCode}</code> : '—'),
    },
    {
      key: 'favourite',
      header: t('customers.units.columnFavourite'),
      render: (row) =>
        row.isFavourite ? <Badge tone="info">★ {t('customers.units.favouriteBadge')}</Badge> : '—',
    },
    ...(canManage
      ? [
          {
            key: 'order',
            header: t('customers.units.columnOrder'),
            render: (row: CustomerPreferredUnit) => (
              <span className="issued-items-row-actions">
                <button
                  type="button"
                  className="issued-items-link"
                  aria-label={t('customers.units.moveUpAria', { name: row.name })}
                  disabled={busy}
                  onClick={() => move(row.unitTypeId, -1)}
                >
                  ↑
                </button>
                <button
                  type="button"
                  className="issued-items-link"
                  aria-label={t('customers.units.moveDownAria', { name: row.name })}
                  disabled={busy}
                  onClick={() => move(row.unitTypeId, 1)}
                >
                  ↓
                </button>
              </span>
            ),
          },
          {
            key: 'actions',
            header: <span aria-label={t('customers.units.actionsAria')} />,
            align: 'right' as const,
            render: (row: CustomerPreferredUnit) => (
              <span className="issued-items-row-actions">
                <button type="button" className="issued-items-link" onClick={() => openDraft(row)}>
                  {t('ui.actions.edit')}
                </button>
                <button
                  type="button"
                  className="issued-items-link issued-items-link-danger"
                  onClick={() => setDeleteTarget(row)}
                >
                  {t('ui.actions.delete')}
                </button>
              </span>
            ),
          },
        ]
      : []),
  ]

  return (
    <section className="customer-panel">
      {/* Technical import config — deliberately collapsed at the BOTTOM of the tariffs tab so it
          never visually dominates the actual price agreements above it. */}
      <details className="customer-units-details">
        <summary>
          <h3>{t('customers.units.title')}</h3>
        </summary>
        <p className="customer-form-muted">{t('customers.units.explanation')}</p>

        {canManage && (
          <div className="customer-panel-header customer-pricing-section-actions">
            <Button variant="secondary" onClick={() => openDraft(null)} disabled={availableForAdd.length === 0}>
              {t('customers.units.addMapping')}
            </Button>
          </div>
        )}
        <DataTable columns={columns} rows={configured} rowKey={(row) => row.unitTypeId} emptyMessage={t('customers.units.empty')} />
      </details>

      {draft && (
        <Modal
          title={draft.existing ? t('customers.units.editMappingTitle', { name: draft.existing.name }) : t('customers.units.newMappingTitle')}
          onClose={() => setDraft(null)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setDraft(null)} disabled={busy}>
                {t('ui.actions.cancel')}
              </Button>
              <Button type="submit" form="unit-mapping-form" disabled={busy}>
                {t('ui.actions.save')}
              </Button>
            </>
          }
        >
          <form id="unit-mapping-form" className="issued-items-form" onSubmit={submitDraft} noValidate>
            {draftError && (
              <div className="issued-items-form-error" role="alert">
                {draftError}
              </div>
            )}
            <FormField label={t('customers.units.unitField')} htmlFor="um-unit" required>
              {draft.existing ? (
                // The unit IS the row's identity — never re-targetable while editing.
                <input id="um-unit" value={draft.existing.name} disabled />
              ) : (
                <SearchableSelect
                  id="um-unit"
                  value={draft.unitTypeId}
                  onChange={(value) => setDraft((d) => (d ? { ...d, unitTypeId: value } : d))}
                  options={availableForAdd.map((unit) => ({ value: unit.id, label: unit.name }))}
                  placeholder={t('customers.units.chooseUnit')}
                />
              )}
            </FormField>
            <div className="issued-items-form-row">
              <FormField label={t('customers.units.columnCustomerLabel')} htmlFor="um-label" hint={t('customers.units.customerLabelHint')}>
                <input
                  id="um-label"
                  value={draft.customerLabel}
                  maxLength={150}
                  onChange={(e) => setDraft((d) => (d ? { ...d, customerLabel: e.target.value } : d))}
                />
              </FormField>
            </div>
            <div className="issued-items-form-row">
              <FormField label={t('customers.units.columnEdiCode')} htmlFor="um-edi" hint={t('customers.units.ediCodeHint')}>
                <input
                  id="um-edi"
                  value={draft.ediCode}
                  maxLength={50}
                  onChange={(e) => setDraft((d) => (d ? { ...d, ediCode: e.target.value } : d))}
                />
              </FormField>
              <FormField label={t('customers.units.columnExcelCode')} htmlFor="um-excel" hint={t('customers.units.excelCodeHint')}>
                <input
                  id="um-excel"
                  value={draft.excelCode}
                  maxLength={50}
                  onChange={(e) => setDraft((d) => (d ? { ...d, excelCode: e.target.value } : d))}
                />
              </FormField>
            </div>
            <label className="tof-checkbox">
              <input
                type="checkbox"
                checked={draft.isFavourite}
                onChange={(e) => setDraft((d) => (d ? { ...d, isFavourite: e.target.checked } : d))}
              />
              {t('customers.units.favouriteField')}
            </label>
          </form>
        </Modal>
      )}

      {deleteTarget && (
        <ConfirmDialog
          title={t('customers.units.deleteTitle')}
          message={t('customers.units.deleteMessage', { name: deleteTarget.name })}
          confirmLabel={t('ui.actions.delete')}
          destructive
          onConfirm={() => void handleDelete()}
          onCancel={() => setDeleteTarget(null)}
        />
      )}
    </section>
  )
}
