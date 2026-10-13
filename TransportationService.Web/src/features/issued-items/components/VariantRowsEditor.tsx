import { useId, useRef, useState, type KeyboardEvent } from 'react'
import { Button } from '../../../components/ui/Button'
import { useLocale } from '../../../i18n/localeContext'
import { formatQuantityWithUnit } from '../issuedItemUnits'
import {
  VARIANT_PRESETS,
  VARIANT_PRESET_HINTS,
  VARIANT_PRESET_LABELS,
  clothingSizeSuggestions,
  insertClothingSize,
  newVariantRow,
  splitQuickVariants,
  type VariantPreset,
  type VariantRow,
  type VariantRowIssue,
} from '../managementMode'

interface VariantRowsEditorProps {
  rows: VariantRow[]
  onChange: (rows: VariantRow[]) => void
  /** Which starting point the list was filled from; the host replaces the rows on a change. */
  preset: VariantPreset
  onPresetChange: (preset: VariantPreset) => void
  disabled?: boolean
  /** Validation outcome of the last submit attempt; rows with an issue get an inline message. */
  issues?: VariantRowIssue[]
  /** Catalogue unit code of the item, for the computed total. */
  unit: string | null
}

/** Which input of a freshly added row takes focus once it mounts. */
interface PendingFocus {
  key: string
  field: 'label' | 'stock'
}

/**
 * Inline, editable variants table for an item that is not saved yet (create flow, or an existing
 * item that switches to "Voorraad met varianten" for the first time). Rows are plain local state
 * and only become real variants — each with an auditable initial-stock movement — when the item
 * is saved. The list starts from a preset (clothing sizes S–XL by default, or an empty list for
 * "Zelf varianten bepalen"); every row stays individually editable and removable, and extra
 * sizes come in with one click, by "+ Variant toevoegen" or through quick-add ("S, M, L, XL").
 */
export function VariantRowsEditor({ rows, onChange, preset, onPresetChange, disabled = false, issues = [], unit }: VariantRowsEditorProps) {
  const { t } = useLocale()
  const presetGroup = useId()
  const [quickAdd, setQuickAdd] = useState('')
  // A row that was just added gets focus the moment it mounts, so typing continues without the
  // mouse (done in the callback ref — no effect, no extra render).
  const pendingFocus = useRef<PendingFocus | null>(null)

  function patch(key: string, change: Partial<VariantRow>) {
    onChange(rows.map((row) => (row.key === key ? { ...row, ...change } : row)))
  }

  function addRow() {
    const row = newVariantRow()
    pendingFocus.current = { key: row.key, field: 'label' }
    onChange([...rows, row])
  }

  function addSuggestedSize(label: string) {
    // The label is final, so the cursor goes straight to the stock of the new row.
    const row = newVariantRow(label)
    pendingFocus.current = { key: row.key, field: 'stock' }
    onChange(insertClothingSize(rows, row))
  }

  function addQuick() {
    const labels = splitQuickVariants(quickAdd, rows.map((row) => row.label))
    if (labels.length === 0) {
      setQuickAdd('')
      return
    }
    const added = labels.map((label) => newVariantRow(label))
    pendingFocus.current = { key: added[0].key, field: 'label' }
    onChange([...rows, ...added])
    setQuickAdd('')
  }

  function handleQuickKeyDown(event: KeyboardEvent<HTMLInputElement>) {
    if (event.key === 'Enter') {
      // Enter adds rows; it must never submit the surrounding item form.
      event.preventDefault()
      addQuick()
    }
  }

  function focusOnMount(field: PendingFocus['field'], key: string) {
    return (element: HTMLInputElement | null) => {
      const pending = pendingFocus.current
      if (element && pending && pending.key === key && pending.field === field) {
        pendingFocus.current = null
        element.focus()
      }
    }
  }

  const issueByKey = new Map(issues.map((issue) => [issue.key, issue.message]))
  const total = rows.reduce((sum, row) => sum + (Number(row.stock) || 0), 0)
  const suggestions = preset === 'clothing' ? clothingSizeSuggestions(rows) : []

  return (
    <div className="issued-items-variant-rows">
      <fieldset className="issued-items-preset">
        <legend className="issued-items-quick-add-label">{t('issuedItems.form.presetLabel')}</legend>
        <div className="issued-items-radio-row">
          {VARIANT_PRESETS.map((option) => (
            <label key={option} className="issued-items-radio">
              <input
                type="radio"
                name={presetGroup}
                value={option}
                checked={preset === option}
                disabled={disabled}
                onChange={() => onPresetChange(option)}
              />
              <span>{t(VARIANT_PRESET_LABELS[option])}</span>
            </label>
          ))}
        </div>
        <p className="issued-items-quick-add-hint">{t(VARIANT_PRESET_HINTS[preset])}</p>
      </fieldset>

      {rows.length === 0 ? (
        <p className="issued-items-section-hint">{t('issuedItems.form.variantsEmpty')}</p>
      ) : (
        <div className="issued-items-table-scroll">
        <table className="issued-items-table">
          <thead>
            <tr>
              <th>{t('issuedItems.form.colVariantName')}</th>
              <th className="issued-items-col-number">{t('issuedItems.form.colStock')}</th>
              <th className="issued-items-col-number">{t('issuedItems.form.colThreshold')}</th>
              <th aria-label={t('issuedItems.tab.colActions')} />
            </tr>
          </thead>
          <tbody>
            {rows.map((row, index) => {
              const issue = issueByKey.get(row.key)
              const position = index + 1
              return (
                <tr key={row.key}>
                  <td>
                    <input
                      ref={focusOnMount('label', row.key)}
                      aria-label={t('issuedItems.form.variantNameAria', { index: position })}
                      aria-invalid={issue ? 'true' : undefined}
                      placeholder={t('issuedItems.form.variantNamePlaceholder')}
                      value={row.label}
                      maxLength={150}
                      disabled={disabled}
                      onChange={(e) => patch(row.key, { label: e.target.value })}
                    />
                    {issue && (
                      <p className="ui-form-field-error" role="alert">
                        {t(issue)}
                      </p>
                    )}
                  </td>
                  <td className="issued-items-col-number">
                    <input
                      ref={focusOnMount('stock', row.key)}
                      aria-label={t('issuedItems.form.variantStockAria', { index: position })}
                      type="number"
                      min={0}
                      inputMode="numeric"
                      value={row.stock}
                      disabled={disabled}
                      onChange={(e) => patch(row.key, { stock: e.target.value })}
                    />
                  </td>
                  <td className="issued-items-col-number">
                    <input
                      aria-label={t('issuedItems.form.variantThresholdAria', { index: position })}
                      type="number"
                      min={0}
                      inputMode="numeric"
                      placeholder={t('issuedItems.form.thresholdPlaceholder')}
                      value={row.threshold}
                      disabled={disabled}
                      onChange={(e) => patch(row.key, { threshold: e.target.value })}
                    />
                  </td>
                  <td className="issued-items-row-actions">
                    <button
                      type="button"
                      className="issued-items-link issued-items-link-danger"
                      aria-label={t('issuedItems.form.variantRemoveAria', { label: row.label || String(position) })}
                      disabled={disabled}
                      onClick={() => onChange(rows.filter((other) => other.key !== row.key))}
                    >
                      {t('ui.actions.delete')}
                    </button>
                  </td>
                </tr>
              )
            })}
          </tbody>
        </table>
        </div>
      )}

      {suggestions.length > 0 && (
        <div className="issued-items-size-suggestions" role="group" aria-label={t('issuedItems.form.sizeSuggestionsLabel')}>
          <span className="issued-items-size-suggestions-label">{t('issuedItems.form.sizeSuggestionsLabel')}</span>
          {suggestions.map((size) => (
            <button
              key={size}
              type="button"
              className="issued-items-size-chip"
              aria-label={t('issuedItems.form.sizeSuggestionAria', { label: size })}
              disabled={disabled}
              onClick={() => addSuggestedSize(size)}
            >
              + {size}
            </button>
          ))}
        </div>
      )}

      <div className="issued-items-variant-tools">
        <Button variant="secondary" onClick={addRow} disabled={disabled}>
          {t('issuedItems.form.addVariant')}
        </Button>
        <div className="issued-items-quick-add">
          <label className="issued-items-quick-add-label" htmlFor="tpl-variant-quick-add">
            {t('issuedItems.form.quickAddLabel')}
          </label>
          <div className="issued-items-quick-add-controls">
            <input
              id="tpl-variant-quick-add"
              value={quickAdd}
              placeholder={t('issuedItems.form.quickAddPlaceholder')}
              disabled={disabled}
              onChange={(e) => setQuickAdd(e.target.value)}
              onKeyDown={handleQuickKeyDown}
            />
            <Button variant="ghost" onClick={addQuick} disabled={disabled || quickAdd.trim() === ''}>
              {t('issuedItems.form.quickAddAction')}
            </Button>
          </div>
          <p className="issued-items-quick-add-hint">{t('issuedItems.form.quickAddHint')}</p>
        </div>
      </div>

      {rows.length > 0 && (
        <p className="issued-items-computed-stock">
          {t('issuedItems.form.totalPending', { total: formatQuantityWithUnit(t, total, unit) })}
        </p>
      )}
    </div>
  )
}
