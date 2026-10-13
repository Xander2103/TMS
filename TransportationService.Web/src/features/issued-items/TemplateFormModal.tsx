import { useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { Button } from '../../components/ui/Button'
import { FormField } from '../../components/ui/FormField'
import { FormSection } from '../../components/ui/FormSection'
import { Modal } from '../../components/ui/Modal'
import { describeApiError } from '../../api/problemDetails'
import { useAuth } from '../auth/authContextValue'
import { useLocale } from '../../i18n/localeContext'
import { LookupSelect } from '../master-data/components/LookupSelect'
import {
  createIssuedItemTemplate,
  updateIssuedItemTemplate,
  type IssuedItemTemplate,
  type IssuedItemTemplateInput,
} from './issuedItemsApi'
import {
  DEFAULT_ISSUED_ITEM_UNIT,
  ISSUED_ITEM_UNIT_DESCRIPTIONS,
  ISSUED_ITEM_UNIT_LABELS,
  ISSUED_ITEM_UNITS,
  issuedItemUnitForForm,
  normalizeIssuedItemUnit,
} from './issuedItemUnits'
import { createVariant } from './inventoryApi'
import {
  DEFAULT_VARIANT_PRESET,
  flagsFromMode,
  modeFromFlags,
  parseJobFunctionCodes,
  parseNonNegative,
  presetRows,
  serializeJobFunctionCodes,
  suggestDefaultsFromSiblings,
  validateVariantRows,
  type ManagementMode,
  type SiblingSuggestion,
  type VariantPreset,
  type VariantRow,
  type VariantRowIssue,
} from './managementMode'
import { ManagementModeCards } from './components/ManagementModeCards'
import { VariantRowsEditor } from './components/VariantRowsEditor'
import { EligibilitySelect } from './components/EligibilitySelect'
import { TemplateVariantsEditor } from './TemplateVariantsEditor'
import './issued-items.css'

/** Outcome details the host needs beyond the saved item (inline variants are separate calls). */
export interface TemplateSaveResult {
  /** Variants that could not be created after the item itself was saved. */
  variantFailures: { label: string; message: string }[]
}

function emptyTemplateForm(): IssuedItemTemplateInput {
  return {
    name: '',
    // No implicit "Algemeen": a new item gets a real category (inline-creatable from the dropdown).
    category: '',
    categoryId: null,
    applicableJobFunctionCodes: null,
    defaultQuantity: 1,
    requiresSerialNumber: false,
    requiresReceivedDate: true,
    returnRequired: false,
    isActive: true,
    sortOrder: 0,
    description: null,
    unit: DEFAULT_ISSUED_ITEM_UNIT,
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
    // Legacy free-text units (pre-catalogue) land on "other" so the user re-picks consciously.
    unit: issuedItemUnitForForm(template.unit),
    notes: template.notes,
    stockTrackingEnabled: template.stockTrackingEnabled,
    variantsEnabled: template.variantsEnabled,
    allowNegativeStock: template.allowNegativeStock,
    lowStockThreshold: template.lowStockThreshold,
    // Round-tripped untouched: the detail page's "Voorraadregels" card owns this level.
    minimumStock: template.minimumStock,
    storageLocation: template.storageLocation,
    stock: template.stockTrackingEnabled && !template.variantsEnabled ? template.currentStock : null,
    stockCorrectionReason: null,
  }
}

interface TemplateFormModalProps {
  editing: IssuedItemTemplate | null
  onSaved: (template: IssuedItemTemplate, result?: TemplateSaveResult) => void
  onClose: () => void
  /**
   * The items already in the overview (create flow). Picking a category then suggests the
   * Beheerwijze and issue settings those items use — category-aware defaults without any
   * category name in code.
   */
  existingItems?: IssuedItemTemplate[]
}

/**
 * Create/edit dialog for one bedrijfsmiddel. The user answers four plain questions — what is it,
 * where does it belong, how do we track it, who is it for — and the form maps those onto the
 * unchanged template fields (`stockTrackingEnabled`/`variantsEnabled`, the function-code CSV,
 * thresholds, …). Low-frequency settings sit under a collapsed "Meer instellingen".
 */
export function TemplateFormModal({ editing, onSaved, onClose, existingItems }: TemplateFormModalProps) {
  const { t } = useLocale()
  const { hasPermission } = useAuth()
  const isEdit = editing !== null
  const canManageCategories = hasPermission('inventory.manage')

  const [form, setForm] = useState<IssuedItemTemplateInput>(editing ? templateToForm(editing) : emptyTemplateForm())
  const [formError, setFormError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  // Variants entered before the item exists (create, or first switch to variants while editing):
  // local rows until save, then one real variant each with an initial-stock movement. The list
  // starts from a preset (clothing sizes S–XL unless the user picks "Zelf varianten bepalen");
  // the preset is a convenience of this form only and never reaches the backend.
  const [preset, setPreset] = useState<VariantPreset>(DEFAULT_VARIANT_PRESET)
  const [rows, setRows] = useState<VariantRow[]>(() => presetRows(DEFAULT_VARIANT_PRESET))
  const [rowIssues, setRowIssues] = useState<VariantRowIssue[]>([])
  // Suggestions never override something the user chose explicitly.
  const [modeTouched, setModeTouched] = useState(false)
  const [issueTouched, setIssueTouched] = useState(false)
  const [suggestion, setSuggestion] = useState<SiblingSuggestion | null>(null)

  const mode = modeFromFlags(form)
  const selectedUnit = normalizeIssuedItemUnit(form.unit)
  // Existing variants pin the mode (backend: variants cannot be disabled while variants exist).
  const hasExistingVariants = isEdit && editing.variantsEnabled && editing.variantCount > 0
  // Variants of an already variant-enabled item are edited live (each change is saved at once).
  const liveVariants = isEdit && editing.variantsEnabled
  // Stock on an existing item goes through the ledger: a change needs a correction reason. The
  // cached count survives switching tracking off, so an item that is tracked AGAIN may also have
  // history — then the reason is offered but only the backend knows whether it is required.
  const trackedBefore = isEdit && editing.stockTrackingEnabled && !editing.variantsEnabled
  const stockChanged = mode === 'stock' && isEdit && form.stock != null && form.stock !== editing.currentStock
  const codes = parseJobFunctionCodes(form.applicableJobFunctionCodes)

  function set<K extends keyof IssuedItemTemplateInput>(key: K, value: IssuedItemTemplateInput[K]) {
    setForm((f) => ({ ...f, [key]: value }))
  }

  function setIssueFlag(key: 'requiresSerialNumber' | 'requiresReceivedDate' | 'returnRequired', value: boolean) {
    set(key, value)
    setIssueTouched(true)
  }

  function changeMode(next: ManagementMode) {
    setForm((f) => ({ ...f, ...flagsFromMode(next) }))
    setModeTouched(true)
    setSuggestion(null)
    setRowIssues([])
  }

  function changePreset(next: VariantPreset) {
    // A preset is a starting point: choosing one replaces the pending list with its rows.
    setPreset(next)
    setRows(presetRows(next))
    setRowIssues([])
  }

  function changeCategory(categoryId: string | null) {
    set('categoryId', categoryId)
    if (isEdit) return
    const suggested = suggestDefaultsFromSiblings(existingItems, categoryId)
    if (!suggested) {
      setSuggestion(null)
      return
    }
    setForm((f) => ({
      ...f,
      ...(modeTouched ? {} : flagsFromMode(suggested.mode)),
      ...(issueTouched
        ? {}
        : {
            requiresSerialNumber: suggested.requiresSerialNumber,
            requiresReceivedDate: suggested.requiresReceivedDate,
            returnRequired: suggested.returnRequired,
          }),
    }))
    setSuggestion(modeTouched ? null : suggested)
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setFormError(null)
    setRowIssues([])
    if (!form.name.trim()) {
      setFormError(t('issuedItems.form.nameRequired'))
      return
    }
    if (!form.categoryId && !form.category.trim()) {
      setFormError(t('issuedItems.form.categoryRequired'))
      return
    }
    if (trackedBefore && stockChanged && !form.stockCorrectionReason?.trim()) {
      setFormError(t('issuedItems.form.correctionReasonRequired'))
      return
    }
    const pendingRows = mode === 'variants' && !liveVariants ? rows : []
    const issues = validateVariantRows(pendingRows)
    if (issues.length > 0) {
      setRowIssues(issues)
      setFormError(t('issuedItems.form.variantRowsInvalid'))
      return
    }

    setSaving(true)
    try {
      const payload: IssuedItemTemplateInput = {
        ...form,
        ...flagsFromMode(mode),
        // Legacy CSVs ("CHAUF, PLAN") go out normalised; the set of codes never changes here.
        applicableJobFunctionCodes: serializeJobFunctionCodes(codes),
        // Template-level stock only exists for plain stock items; variants carry their own.
        stock: mode === 'stock' ? form.stock : null,
        stockCorrectionReason: mode === 'stock' ? form.stockCorrectionReason : null,
      }
      const saved = editing ? await updateIssuedItemTemplate(editing.id, payload) : await createIssuedItemTemplate(payload)

      // Each pending row becomes a real variant with an auditable initial-stock movement. The
      // item already exists at this point, so a failing row is reported instead of retried as
      // a whole (which would duplicate the item).
      const variantFailures: TemplateSaveResult['variantFailures'] = []
      for (const [index, row] of pendingRows.entries()) {
        try {
          await createVariant(saved.id, {
            values: [],
            isActive: true,
            sortOrder: index,
            initialStock: parseNonNegative(row.stock),
            label: row.label.trim(),
            lowStockThreshold: parseNonNegative(row.threshold),
          })
        } catch (err) {
          variantFailures.push({ label: row.label.trim(), message: describeApiError(err, t('issuedItems.variantsEditor.addFailed')).message })
        }
      }
      onSaved(saved, variantFailures.length > 0 ? { variantFailures } : undefined)
    } catch (err) {
      setFormError(describeApiError(err, t('issuedItems.form.saveFailed')).message)
    } finally {
      setSaving(false)
    }
  }

  const serialCheckbox = (
    <div className="form-span-all">
      <label className="issued-items-checkbox">
        <input
          type="checkbox"
          checked={form.requiresSerialNumber}
          onChange={(e) => setIssueFlag('requiresSerialNumber', e.target.checked)}
          disabled={saving}
        />
        <span>{t('issuedItems.form.requiresSerial')}</span>
      </label>
      <p className="issued-items-checkbox-hint">{t('issuedItems.form.requiresSerialHint')}</p>
    </div>
  )

  const storageField = (
    <FormField label={t('issuedItems.form.storage')} htmlFor="tpl-storage" hint={mode === 'variants' ? t('issuedItems.form.storageVariantsHint') : undefined}>
      <input
        id="tpl-storage"
        value={form.storageLocation ?? ''}
        placeholder={t('issuedItems.form.storagePlaceholder')}
        onChange={(e) => set('storageLocation', e.target.value || null)}
        disabled={saving}
        maxLength={150}
      />
    </FormField>
  )

  return (
    <Modal
      title={isEdit ? t('issuedItems.form.editTitle') : t('issuedItems.form.addTitle')}
      onClose={onClose}
      busy={saving}
      size="wide"
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
      <form id="template-form" className="issued-items-form issued-items-item-form" onSubmit={handleSubmit} noValidate>
        {formError && (
          <div className="issued-items-form-error" role="alert">
            {formError}
          </div>
        )}
        {isEdit && !form.isActive && <p className="issued-items-inactive-notice">{t('issuedItems.form.inactiveNotice')}</p>}

        {/* 1 — What is it, where does it belong */}
        <FormSection title={t('issuedItems.form.sectionBasics')} variant="flat" columns={2}>
          <FormField label={t('issuedItems.form.name')} htmlFor="tpl-name" required>
            <input
              id="tpl-name"
              value={form.name}
              placeholder={t('issuedItems.form.namePlaceholder')}
              onChange={(e) => set('name', e.target.value)}
              disabled={saving}
              maxLength={150}
            />
          </FormField>
          <FormField label={t('issuedItems.form.category')} htmlFor="tpl-cat" required hint={t('issuedItems.form.categoryHint')}>
            <LookupSelect
              id="tpl-cat"
              basePath="/api/issued-item-categories"
              viewPermission="issued_items.view"
              managePermission="inventory.manage"
              singular="masterData.singular.issued-item-categories"
              createFields="name"
              createLabel={t('issuedItems.form.newCategory')}
              createTitle={t('issuedItems.form.newCategoryTitle')}
              value={form.categoryId}
              onChange={changeCategory}
              placeholder={form.categoryId ? undefined : form.category || t('issuedItems.form.categoryPlaceholder')}
              disabled={saving}
            />
            {canManageCategories && (
              <Link className="issued-items-manage-link" to="/issued-items/categories">
                {t('issuedItems.form.manageCategories')}
              </Link>
            )}
          </FormField>
          <FormField label={t('issuedItems.form.description')} htmlFor="tpl-desc" className="form-span-all">
            <input id="tpl-desc" value={form.description ?? ''} onChange={(e) => set('description', e.target.value || null)} disabled={saving} maxLength={500} />
          </FormField>
        </FormSection>

        {/* 2 — How do we track it (drives everything below it) */}
        <FormSection title={t('issuedItems.form.sectionMode')} description={t('issuedItems.form.modeHint')} variant="flat" columns={1}>
          <ManagementModeCards
            value={mode}
            onChange={changeMode}
            disabled={saving}
            lockedReason={hasExistingVariants ? t('issuedItems.form.modeLocked') : null}
            suggestionHint={suggestion ? t('issuedItems.form.modeSuggested', { count: suggestion.basedOn }) : null}
          />

          {mode === 'stock' && (
            <div className="issued-items-mode-detail">
              <div className="issued-items-form-row-3">
                <FormField
                  label={trackedBefore ? t('issuedItems.form.currentStock') : t('issuedItems.form.initialStock')}
                  htmlFor="tpl-stock"
                  hint={trackedBefore ? t('issuedItems.form.currentStockHint') : t('issuedItems.form.initialStockHint')}
                >
                  <input
                    id="tpl-stock"
                    type="number"
                    inputMode="numeric"
                    value={form.stock ?? ''}
                    onChange={(e) => set('stock', e.target.value === '' ? null : Number(e.target.value) || 0)}
                    disabled={saving}
                  />
                </FormField>
                <FormField label={t('issuedItems.form.minimum')} htmlFor="tpl-low" hint={t('issuedItems.form.minimumHint')}>
                  <input
                    id="tpl-low"
                    type="number"
                    min={0}
                    inputMode="numeric"
                    value={form.lowStockThreshold ?? ''}
                    onChange={(e) => set('lowStockThreshold', parseNonNegative(e.target.value))}
                    disabled={saving}
                  />
                </FormField>
                {storageField}
              </div>
              {stockChanged && (
                <FormField
                  label={t('issuedItems.form.correctionReason')}
                  htmlFor="tpl-stock-reason"
                  required={trackedBefore}
                  hint={trackedBefore ? t('issuedItems.form.correctionReasonHint') : t('issuedItems.form.correctionReasonOptionalHint')}
                >
                  <input
                    id="tpl-stock-reason"
                    value={form.stockCorrectionReason ?? ''}
                    onChange={(e) => set('stockCorrectionReason', e.target.value || null)}
                    disabled={saving}
                    maxLength={300}
                  />
                </FormField>
              )}
            </div>
          )}

          {mode === 'variants' && (
            <div className="issued-items-mode-detail">
              <p className="issued-items-mode-detail-title">{t('issuedItems.form.variantsTitle')}</p>
              <p className="issued-items-section-hint">{liveVariants ? t('issuedItems.form.variantsLive') : t('issuedItems.form.variantsIntro')}</p>
              {liveVariants ? (
                <TemplateVariantsEditor templateId={editing.id} unit={form.unit} />
              ) : (
                <VariantRowsEditor rows={rows} onChange={setRows} preset={preset} onPresetChange={changePreset} disabled={saving} issues={rowIssues} unit={form.unit} />
              )}
              {storageField}
            </div>
          )}

          {mode === 'individual' && isEdit && editing.stockTrackingEnabled && (
            <p className="issued-items-section-hint">{t('issuedItems.form.modeLeavingStock')}</p>
          )}
        </FormSection>

        {/* 3 — What happens at issue time */}
        <FormSection title={t('issuedItems.form.sectionIssue')} description={t('issuedItems.form.issueHint')} variant="flat" columns={1}>
          <div className="issued-items-checkbox-list">
            {/* Individually tracked items lead with the identifier; it is what sets them apart. */}
            {mode === 'individual' && serialCheckbox}
            <label className="issued-items-checkbox">
              <input type="checkbox" checked={form.requiresReceivedDate} onChange={(e) => setIssueFlag('requiresReceivedDate', e.target.checked)} disabled={saving} />
              <span>{t('issuedItems.form.requiresReceivedDate')}</span>
            </label>
            <label className="issued-items-checkbox">
              <input type="checkbox" checked={form.returnRequired} onChange={(e) => setIssueFlag('returnRequired', e.target.checked)} disabled={saving} />
              <span>{t('issuedItems.form.returnRequired')}</span>
            </label>
            {mode === 'stock' && serialCheckbox}
            {/* Sizes/versions rarely carry a serial number: the option lives under "Meer instellingen". */}
            {mode === 'variants' && form.requiresSerialNumber && (
              <p className="issued-items-section-hint">{t('issuedItems.form.requiresSerialElsewhere')}</p>
            )}
          </div>
        </FormSection>

        {/* 4 — Who is it for */}
        <FormSection title={t('issuedItems.form.sectionEligibility')} variant="flat" columns={1}>
          <EligibilitySelect codes={codes} onChange={(next) => set('applicableJobFunctionCodes', serializeJobFunctionCodes(next))} disabled={saving} />
        </FormSection>

        {/* 5 — Everything else, folded away */}
        <FormSection title={t('issuedItems.form.sectionAdvanced')} variant="flat" columns={2} collapsible defaultOpen={isEdit && !editing.isActive}>
          <FormField label={t('issuedItems.form.unit')} htmlFor="tpl-unit" hint={t(ISSUED_ITEM_UNIT_DESCRIPTIONS[selectedUnit])}>
            <select id="tpl-unit" value={selectedUnit} onChange={(e) => set('unit', normalizeIssuedItemUnit(e.target.value))} disabled={saving}>
              {ISSUED_ITEM_UNITS.map((unit) => (
                <option key={unit} value={unit} title={t(ISSUED_ITEM_UNIT_DESCRIPTIONS[unit])}>
                  {t(ISSUED_ITEM_UNIT_LABELS[unit])}
                </option>
              ))}
            </select>
          </FormField>
          <FormField label={t('issuedItems.form.defaultQty')} htmlFor="tpl-qty">
            <input id="tpl-qty" type="number" min={1} inputMode="numeric" value={form.defaultQuantity} onChange={(e) => set('defaultQuantity', Number(e.target.value) || 1)} disabled={saving} />
          </FormField>
          {mode === 'variants' && (
            <FormField label={t('issuedItems.form.defaultVariantThreshold')} htmlFor="tpl-low" hint={t('issuedItems.form.defaultVariantThresholdHint')}>
              <input
                id="tpl-low"
                type="number"
                min={0}
                inputMode="numeric"
                value={form.lowStockThreshold ?? ''}
                onChange={(e) => set('lowStockThreshold', parseNonNegative(e.target.value))}
                disabled={saving}
              />
            </FormField>
          )}
          <FormField label={t('issuedItems.form.sortOrder')} htmlFor="tpl-sort" hint={t('issuedItems.form.sortOrderHint')}>
            <input id="tpl-sort" type="number" inputMode="numeric" value={form.sortOrder} onChange={(e) => set('sortOrder', Number(e.target.value) || 0)} disabled={saving} />
          </FormField>
          {mode !== 'individual' && (
            <div className="form-span-all">
              <label className="issued-items-checkbox">
                <input type="checkbox" checked={form.allowNegativeStock} onChange={(e) => set('allowNegativeStock', e.target.checked)} disabled={saving} />
                <span>{t('issuedItems.form.allowNegative')}</span>
              </label>
              <p className="issued-items-checkbox-hint">{t('issuedItems.form.allowNegativeHint')}</p>
            </div>
          )}
          {mode === 'variants' && serialCheckbox}
          <FormField label={t('issuedItems.form.notes')} htmlFor="tpl-notes" className="form-span-all">
            <textarea id="tpl-notes" rows={2} value={form.notes ?? ''} onChange={(e) => set('notes', e.target.value || null)} disabled={saving} maxLength={1000} />
          </FormField>
          {isEdit && (
            <label className="issued-items-checkbox form-span-all">
              <input type="checkbox" checked={form.isActive} onChange={(e) => set('isActive', e.target.checked)} disabled={saving} />
              <span>{t('issuedItems.form.active')}</span>
            </label>
          )}
        </FormSection>
      </form>
    </Modal>
  )
}
