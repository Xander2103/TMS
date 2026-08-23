import { useEffect, useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { Button } from '../../components/ui/Button'
import { FormField } from '../../components/ui/FormField'
import { Modal } from '../../components/ui/Modal'
import { describeApiError } from '../../api/problemDetails'
import { useAuth } from '../auth/authContextValue'
import { useLocale } from '../../i18n/localeContext'
import { LookupSelect } from '../master-data/components/LookupSelect'
import {
  createIssuedItemTemplate,
  listInventoryUnitOptions,
  updateIssuedItemTemplate,
  type IssuedItemTemplate,
  type IssuedItemTemplateInput,
  type InventoryUnitOption,
} from './issuedItemsApi'
import { createVariant } from './inventoryApi'
import { TemplateVariantsEditor } from './TemplateVariantsEditor'
import './issued-items.css'

interface PendingVariant {
  label: string
  stock: string
  threshold: string
}

function emptyTemplateForm(): IssuedItemTemplateInput {
  return {
    name: '',
    category: 'Algemeen',
    categoryId: null,
    applicableJobFunctionCodes: null,
    defaultQuantity: 1,
    requiresSerialNumber: false,
    requiresReceivedDate: true,
    returnRequired: false,
    isActive: true,
    sortOrder: 0,
    description: null,
    unit: null,
    notes: null,
    stockTrackingEnabled: false,
    variantsEnabled: false,
    allowNegativeStock: false,
    lowStockThreshold: null,
    minimumStock: null,
    storageLocation: null,
    stock: null,
    stockCorrectionReason: null,
  }
}

function templateToForm(template: IssuedItemTemplate): IssuedItemTemplateInput {
  return {
    name: template.name,
    category: template.category,
    categoryId: template.categoryId,
    applicableJobFunctionCodes: template.applicableJobFunctionCodes,
    defaultQuantity: template.defaultQuantity,
    requiresSerialNumber: template.requiresSerialNumber,
    requiresReceivedDate: template.requiresReceivedDate,
    returnRequired: template.returnRequired,
    isActive: template.isActive,
    sortOrder: template.sortOrder,
    description: template.description,
    unit: template.unit,
    notes: template.notes,
    stockTrackingEnabled: template.stockTrackingEnabled,
    variantsEnabled: template.variantsEnabled,
    allowNegativeStock: template.allowNegativeStock,
    lowStockThreshold: template.lowStockThreshold,
    minimumStock: template.minimumStock,
    storageLocation: template.storageLocation,
    stock: template.stockTrackingEnabled && !template.variantsEnabled ? template.currentStock : null,
    stockCorrectionReason: null,
  }
}

interface TemplateFormModalProps {
  editing: IssuedItemTemplate | null
  onSaved: (template: IssuedItemTemplate) => void
  onClose: () => void
}

/**
 * Create/edit modal for an issued-item template, including stock configuration. Stock- and
 * variant-related inputs appear only when their toggle is on; enabling variants requires
 * stock tracking (mirrors the backend rule).
 */
export function TemplateFormModal({ editing, onSaved, onClose }: TemplateFormModalProps) {
  const { t } = useLocale()
  const { hasPermission } = useAuth()
  const [form, setForm] = useState<IssuedItemTemplateInput>(editing ? templateToForm(editing) : emptyTemplateForm())
  const [formError, setFormError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const canManageCategories = hasPermission('inventory.manage')
  const canManageUnits = hasPermission('unit_types.manage') || hasPermission('tariffs.manage')
  const [unitOptions, setUnitOptions] = useState<InventoryUnitOption[]>([])
  // Create mode: variants entered here are created (with their initial-stock movements)
  // right after the template itself.
  const [pendingVariants, setPendingVariants] = useState<PendingVariant[]>([])
  const [pendingDraft, setPendingDraft] = useState<PendingVariant>({ label: '', stock: '', threshold: '' })

  useEffect(() => {
    let mounted = true
    listInventoryUnitOptions()
      .then((data) => {
        if (mounted) setUnitOptions(data)
      })
      .catch(() => {})
    return () => {
      mounted = false
    }
  }, [])
  // Reference value for detecting a stock change on an existing template (ledger correction).
  const loadedStock = editing && editing.stockTrackingEnabled && !editing.variantsEnabled ? editing.currentStock : null
  const stockChanged = form.stockTrackingEnabled && !form.variantsEnabled
    && form.stock != null && form.stock !== (loadedStock ?? 0) && loadedStock !== null

  function set<K extends keyof IssuedItemTemplateInput>(key: K, value: IssuedItemTemplateInput[K]) {
    setForm((f) => ({ ...f, [key]: value }))
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setFormError(null)
    if (!form.name.trim() || (!form.categoryId && !form.category.trim())) {
      setFormError(t('issuedItems.form.nameCategoryRequired'))
      return
    }
    if (stockChanged && editing && !form.stockCorrectionReason?.trim()) {
      setFormError(t('issuedItems.form.correctionReasonRequired'))
      return
    }
    setSaving(true)
    try {
      const saved = editing
        ? await updateIssuedItemTemplate(editing.id, form)
        : await createIssuedItemTemplate(form)
      if (!editing && form.variantsEnabled) {
        // Each row becomes a real variant with an auditable initial-stock movement.
        for (const [index, pending] of pendingVariants.entries()) {
          await createVariant(saved.id, {
            values: [],
            isActive: true,
            sortOrder: index,
            initialStock: pending.stock.trim() === '' ? null : Math.max(0, Number(pending.stock) || 0),
            label: pending.label.trim(),
            lowStockThreshold: pending.threshold.trim() === '' ? null : Math.max(0, Number(pending.threshold) || 0),
          })
        }
      }
      onSaved(saved)
    } catch (err) {
      setFormError(describeApiError(err, t('issuedItems.form.saveFailed')).message)
    } finally {
      setSaving(false)
    }
  }

  function addPendingVariant() {
    const label = pendingDraft.label.trim()
    if (!label) {
      setFormError(t('issuedItems.form.variantNameRequired'))
      return
    }

    setFormError(null)
    setPendingVariants((rows) => [...rows, { ...pendingDraft, label }])
    setPendingDraft({ label: '', stock: '', threshold: '' })
  }

  return (
    <Modal
      title={editing ? t('issuedItems.form.editTitle') : t('issuedItems.form.addTitle')}
      onClose={onClose}
      busy={saving}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={saving}>
            {t('ui.actions.cancel')}
          </Button>
          <Button type="submit" form="template-form" disabled={saving}>
            {saving ? t('issuedItems.form.saving') : t('ui.actions.save')}
          </Button>
        </>
      }
    >
      <form id="template-form" className="issued-items-form" onSubmit={handleSubmit} noValidate>
        {formError && (
          <div className="issued-items-form-error" role="alert">
            {formError}
          </div>
        )}
        <div className="issued-items-form-row">
          <FormField label={t('issuedItems.form.name')} htmlFor="tpl-name" required>
            <input id="tpl-name" value={form.name} onChange={(e) => set('name', e.target.value)} disabled={saving} maxLength={150} />
          </FormField>
          <FormField label={t('issuedItems.form.category')} htmlFor="tpl-cat" required>
            <LookupSelect
              id="tpl-cat"
              basePath="/api/issued-item-categories"
              viewPermission="issued_items.view"
              managePermission="inventory.manage"
              singular="categorie"
              value={form.categoryId}
              onChange={(value) => set('categoryId', value)}
              placeholder={form.categoryId ? undefined : form.category || t('issuedItems.form.categoryPlaceholder')}
              disabled={saving}
            />
            {canManageCategories && (
              <Link className="issued-items-manage-link" to="/master-data/issued-item-categories">
                {t('issuedItems.form.manageCategories')}
              </Link>
            )}
          </FormField>
        </div>
        <FormField label={t('issuedItems.form.description')} htmlFor="tpl-desc">
          <input id="tpl-desc" value={form.description ?? ''} onChange={(e) => set('description', e.target.value || null)} disabled={saving} maxLength={500} />
        </FormField>
        <FormField
          label={t('issuedItems.form.jobFunctions')}
          htmlFor="tpl-functions"
          hint={t('issuedItems.form.jobFunctionsHint')}
        >
          <input
            id="tpl-functions"
            value={form.applicableJobFunctionCodes ?? ''}
            onChange={(e) => set('applicableJobFunctionCodes', e.target.value || null)}
            disabled={saving}
            maxLength={300}
          />
        </FormField>
        <div className="issued-items-form-row">
          <FormField label={t('issuedItems.form.defaultQty')} htmlFor="tpl-qty">
            <input id="tpl-qty" type="number" min={1} value={form.defaultQuantity} onChange={(e) => set('defaultQuantity', Number(e.target.value) || 1)} disabled={saving} />
          </FormField>
          <FormField
            label={t('issuedItems.form.sortOrder')}
            htmlFor="tpl-sort"
            hint={t('issuedItems.form.sortOrderHint')}
          >
            <input id="tpl-sort" type="number" value={form.sortOrder} onChange={(e) => set('sortOrder', Number(e.target.value) || 0)} disabled={saving} />
          </FormField>
        </div>
        <label className="issued-items-checkbox">
          <input type="checkbox" checked={form.requiresSerialNumber} onChange={(e) => set('requiresSerialNumber', e.target.checked)} disabled={saving} />
          <span>{t('issuedItems.form.requiresSerial')}</span>
        </label>
        <label className="issued-items-checkbox">
          <input type="checkbox" checked={form.requiresReceivedDate} onChange={(e) => set('requiresReceivedDate', e.target.checked)} disabled={saving} />
          <span>{t('issuedItems.form.requiresReceivedDate')}</span>
        </label>
        <label className="issued-items-checkbox">
          <input type="checkbox" checked={form.returnRequired} onChange={(e) => set('returnRequired', e.target.checked)} disabled={saving} />
          <span>{t('issuedItems.form.returnRequired')}</span>
        </label>
        <label className="issued-items-checkbox">
          <input type="checkbox" checked={form.isActive} onChange={(e) => set('isActive', e.target.checked)} disabled={saving} />
          <span>{t('issuedItems.form.active')}</span>
        </label>

        <label className="issued-items-checkbox issued-items-checkbox-strong">
          <input
            type="checkbox"
            checked={form.stockTrackingEnabled}
            onChange={(e) => {
              const on = e.target.checked
              setForm((f) => ({ ...f, stockTrackingEnabled: on, variantsEnabled: on ? f.variantsEnabled : false }))
            }}
            disabled={saving}
          />
          <span>{t('issuedItems.form.stockTracking')}</span>
        </label>
        {form.stockTrackingEnabled && (
          <div className="issued-items-stock-config">
            <label className="issued-items-checkbox">
              <input type="checkbox" checked={form.variantsEnabled} onChange={(e) => set('variantsEnabled', e.target.checked)} disabled={saving} />
              <span>{t('issuedItems.form.variantsEnabled')}</span>
            </label>
            {form.variantsEnabled && editing?.variantsEnabled && (
              <TemplateVariantsEditor templateId={editing.id} unit={form.unit} />
            )}
            {form.variantsEnabled && editing && !editing.variantsEnabled && (
              <p className="customer-form-muted">
                {t('issuedItems.form.variantsAfterSave')}
              </p>
            )}
            {form.variantsEnabled && !editing && (
              <div className="issued-items-stock-config">
                {pendingVariants.length > 0 && (
                  <table className="issued-items-table">
                    <thead>
                      <tr>
                        <th>{t('issuedItems.form.colVariantName')}</th>
                        <th>{t('issuedItems.form.colStock')}</th>
                        <th>{t('issuedItems.form.colThreshold')}</th>
                        <th aria-label={t('issuedItems.tab.colActions')} />
                      </tr>
                    </thead>
                    <tbody>
                      {pendingVariants.map((variant, index) => (
                        <tr key={index}>
                          <td>{variant.label}</td>
                          <td>{variant.stock || '0'}</td>
                          <td>{variant.threshold || '—'}</td>
                          <td className="issued-items-row-actions">
                            <button
                              type="button"
                              className="issued-items-link issued-items-link-danger"
                              onClick={() => setPendingVariants((rows) => rows.filter((_, i) => i !== index))}
                            >
                              {t('ui.actions.delete')}
                            </button>
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                )}
                <p className="issued-items-computed-stock">
                  {t('issuedItems.form.totalPending', {
                    total: pendingVariants.reduce((sum, v) => sum + (Number(v.stock) || 0), 0),
                  })}
                </p>
                <div className="issued-items-form-row">
                  <FormField label={t('issuedItems.form.variantName')} htmlFor="tpl-variant-label" hint={t('issuedItems.form.variantNameHint')}>
                    <input
                      id="tpl-variant-label"
                      value={pendingDraft.label}
                      maxLength={150}
                      disabled={saving}
                      onChange={(e) => setPendingDraft((d) => ({ ...d, label: e.target.value }))}
                    />
                  </FormField>
                  <FormField label={t('issuedItems.form.stock')} htmlFor="tpl-variant-stock">
                    <input
                      id="tpl-variant-stock"
                      type="number"
                      min={0}
                      value={pendingDraft.stock}
                      disabled={saving}
                      onChange={(e) => setPendingDraft((d) => ({ ...d, stock: e.target.value }))}
                    />
                  </FormField>
                  <FormField label={t('issuedItems.form.threshold')} htmlFor="tpl-variant-threshold" hint={t('issuedItems.form.thresholdHint')}>
                    <input
                      id="tpl-variant-threshold"
                      type="number"
                      min={0}
                      value={pendingDraft.threshold}
                      disabled={saving}
                      onChange={(e) => setPendingDraft((d) => ({ ...d, threshold: e.target.value }))}
                    />
                  </FormField>
                  <Button variant="secondary" onClick={addPendingVariant} disabled={saving}>
                    {t('issuedItems.form.addVariant')}
                  </Button>
                </div>
              </div>
            )}
            <label className="issued-items-checkbox">
              <input type="checkbox" checked={form.allowNegativeStock} onChange={(e) => set('allowNegativeStock', e.target.checked)} disabled={saving} />
              <span>{t('issuedItems.form.allowNegative')}</span>
            </label>
            {!form.variantsEnabled && (
              <FormField
                label={t('issuedItems.form.stock')}
                htmlFor="tpl-stock"
                hint={t('issuedItems.form.stockHint')}
              >
                <input
                  id="tpl-stock"
                  type="number"
                  value={form.stock ?? ''}
                  onChange={(e) => set('stock', e.target.value === '' ? null : Number(e.target.value) || 0)}
                  disabled={saving}
                />
              </FormField>
            )}
            {stockChanged && (
              <FormField label={t('issuedItems.form.correctionReason')} htmlFor="tpl-stock-reason" required hint={t('issuedItems.form.correctionReasonHint')}>
                <input
                  id="tpl-stock-reason"
                  value={form.stockCorrectionReason ?? ''}
                  onChange={(e) => set('stockCorrectionReason', e.target.value || null)}
                  disabled={saving}
                  maxLength={300}
                />
              </FormField>
            )}
            <div className="issued-items-form-row">
              <FormField
                label={t('issuedItems.form.lowThreshold')}
                htmlFor="tpl-low"
                hint={t('issuedItems.form.lowThresholdHint')}
              >
                <input
                  id="tpl-low"
                  type="number"
                  min={0}
                  value={form.lowStockThreshold ?? ''}
                  onChange={(e) => set('lowStockThreshold', e.target.value === '' ? null : Math.max(0, Number(e.target.value) || 0))}
                  disabled={saving}
                />
              </FormField>
            </div>
            <div className="issued-items-form-row">
              <FormField label={t('issuedItems.form.unit')} htmlFor="tpl-unit" hint={t('issuedItems.form.unitHint')}>
                {/* Managed dropdown (spec: no free text); a legacy free-text value stays selectable. */}
                <select id="tpl-unit" value={form.unit ?? ''} onChange={(e) => set('unit', e.target.value || null)} disabled={saving}>
                  <option value="">{t('issuedItems.form.unitPlaceholder')}</option>
                  {form.unit && !unitOptions.some((u) => u.name === form.unit) && (
                    <option value={form.unit}>{t('issuedItems.form.legacyUnit', { value: form.unit })}</option>
                  )}
                  {unitOptions.map((unit) => (
                    <option key={unit.id} value={unit.name}>
                      {unit.name}
                      {unit.symbol ? ` (${unit.symbol})` : ''}
                    </option>
                  ))}
                </select>
                {canManageUnits && (
                  <Link className="issued-items-manage-link" to="/master-data/eenheden">
                    {t('issuedItems.form.manageUnits')}
                  </Link>
                )}
              </FormField>
              <FormField label={t('issuedItems.form.storage')} htmlFor="tpl-storage">
                <input id="tpl-storage" value={form.storageLocation ?? ''} onChange={(e) => set('storageLocation', e.target.value || null)} disabled={saving} maxLength={150} />
              </FormField>
            </div>
          </div>
        )}
        <FormField label={t('issuedItems.form.notes')} htmlFor="tpl-notes">
          <textarea id="tpl-notes" rows={2} value={form.notes ?? ''} onChange={(e) => set('notes', e.target.value || null)} disabled={saving} maxLength={1000} />
        </FormField>
      </form>
    </Modal>
  )
}
