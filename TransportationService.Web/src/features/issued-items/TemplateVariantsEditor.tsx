import { useCallback, useEffect, useState } from 'react'
import { Button } from '../../components/ui/Button'
import { FormField } from '../../components/ui/FormField'
import { describeApiError } from '../../api/problemDetails'
import { useLocale } from '../../i18n/localeContext'
import {
  createVariant,
  correctStock,
  getTemplateDetail,
  updateVariant,
  type IssuedItemVariant,
} from './inventoryApi'

interface TemplateVariantsEditorProps {
  templateId: string
  unit: string | null
}

interface NewVariantDraft {
  label: string
  stock: string
  threshold: string
}

const emptyDraft: NewVariantDraft = { label: '', stock: '', threshold: '' }

/**
 * Direct variant editing inside the template form (spec: the Varianten checkbox alone is not
 * enough). Variant stock is the source of truth — every change here flows through the stock
 * ledger (initial stock on create, an auditable correction on adjustment); the template total
 * is only the computed sum.
 */
export function TemplateVariantsEditor({ templateId, unit }: TemplateVariantsEditorProps) {
  const { t } = useLocale()
  const [variants, setVariants] = useState<IssuedItemVariant[] | null>(null)
  const [hasAttributes, setHasAttributes] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [draft, setDraft] = useState<NewVariantDraft>(emptyDraft)
  const [stockEdits, setStockEdits] = useState<Record<string, string>>({})
  const [busy, setBusy] = useState(false)

  const reload = useCallback(() => {
    getTemplateDetail(templateId)
      .then((detail) => {
        setVariants(detail.variants)
        setHasAttributes(detail.attributes.length > 0)
        setStockEdits({})
      })
      .catch(() => setError('issuedItems.variantsEditor.loadFailed'))
  }, [templateId])

  useEffect(() => {
    reload()
  }, [reload])

  // `error` bevat ofwel een vertaalsleutel (load) ofwel een reeds vertaalde/serverfout.
  const errorText = error ? (error.startsWith('issuedItems.') ? t(error) : error) : null

  if (error) return <p className="placeholder-text">{errorText}</p>
  if (variants === null) return <p className="placeholder-text">{t('issuedItems.variantsEditor.loading')}</p>

  const total = variants.filter((v) => v.isActive).reduce((sum, v) => sum + v.currentStock, 0)

  async function run(action: () => Promise<unknown>, fallback: string) {
    setBusy(true)
    setError(null)
    try {
      await action()
      reload()
    } catch (err) {
      setError(describeApiError(err, fallback).message)
    } finally {
      setBusy(false)
    }
  }

  function addVariant() {
    const label = draft.label.trim()
    if (!label) {
      setError(t('issuedItems.form.variantNameRequired'))
      return
    }

    void run(
      () =>
        createVariant(templateId, {
          values: [],
          isActive: true,
          sortOrder: variants?.length ?? 0,
          initialStock: draft.stock.trim() === '' ? null : Math.max(0, Number(draft.stock) || 0),
          label,
          lowStockThreshold: draft.threshold.trim() === '' ? null : Math.max(0, Number(draft.threshold) || 0),
        }).then(() => setDraft(emptyDraft)),
      t('issuedItems.variantsEditor.addFailed'),
    )
  }

  function saveVariant(variant: IssuedItemVariant, patch: { label?: string; isActive?: boolean; threshold?: number | null }) {
    void run(
      () =>
        updateVariant(templateId, variant.id, {
          values: variant.values.map((v) => ({
            attributeDefinitionId: v.attributeDefinitionId,
            attributeOptionId: v.attributeOptionId,
            customValue: v.attributeOptionId ? null : v.value,
          })),
          isActive: patch.isActive ?? variant.isActive,
          sortOrder: variant.sortOrder,
          initialStock: null,
          label: patch.label ?? variant.label,
          lowStockThreshold: patch.threshold === undefined ? variant.lowStockThreshold : patch.threshold,
        }),
      t('issuedItems.variantsEditor.saveFailed'),
    )
  }

  function adjustStock(variant: IssuedItemVariant) {
    const raw = stockEdits[variant.id]
    if (raw === undefined || raw.trim() === '' || Number(raw) === variant.currentStock) return
    void run(
      () =>
        correctStock(templateId, {
          variantId: variant.id,
          newQuantity: Math.trunc(Number(raw) || 0),
          // Vaste (Nederlandse) grootboekreden — dit is opgeslagen data, geen UI-tekst.
          reason: 'Voorraadaanpassing via sjabloonformulier',
        }),
      t('issuedItems.variantsEditor.correctFailed'),
    )
  }

  return (
    <div className="issued-items-stock-config">
      {error && (
        <div className="issued-items-form-error" role="alert">
          {errorText}
        </div>
      )}

      {variants.length > 0 && (
        <table className="issued-items-table">
          <thead>
            <tr>
              <th>{t('issuedItems.variantsEditor.colName')}</th>
              <th>{t('issuedItems.variantsEditor.colStock')}</th>
              <th>{t('issuedItems.variantsEditor.colThreshold')}</th>
              <th>{t('issuedItems.variantsEditor.colStatus')}</th>
              <th aria-label={t('issuedItems.tab.colActions')} />
            </tr>
          </thead>
          <tbody>
            {variants.map((variant) => (
              <tr key={variant.id}>
                <td>
                  {variant.values.length === 0 ? (
                    <input
                      aria-label={t('issuedItems.variantsEditor.nameAria', { label: variant.label })}
                      defaultValue={variant.label}
                      maxLength={150}
                      disabled={busy}
                      onBlur={(e) => {
                        const label = e.target.value.trim()
                        if (label && label !== variant.label) saveVariant(variant, { label })
                      }}
                    />
                  ) : (
                    variant.label
                  )}
                </td>
                <td>
                  {/* Adjustments go through the ledger as auditable corrections. */}
                  <input
                    aria-label={t('issuedItems.variantsEditor.stockAria', { label: variant.label })}
                    type="number"
                    defaultValue={variant.currentStock}
                    disabled={busy}
                    onChange={(e) => setStockEdits((edits) => ({ ...edits, [variant.id]: e.target.value }))}
                    onBlur={() => adjustStock(variant)}
                  />
                </td>
                <td>
                  <input
                    aria-label={t('issuedItems.variantsEditor.thresholdAria', { label: variant.label })}
                    type="number"
                    min={0}
                    defaultValue={variant.lowStockThreshold ?? ''}
                    placeholder={t('issuedItems.variantsEditor.thresholdPlaceholder')}
                    disabled={busy}
                    onBlur={(e) => {
                      const threshold = e.target.value.trim() === '' ? null : Math.max(0, Number(e.target.value) || 0)
                      if (threshold !== variant.lowStockThreshold) saveVariant(variant, { threshold })
                    }}
                  />
                </td>
                <td>{variant.isActive ? t('issuedItems.variantsEditor.statusActive') : t('issuedItems.variantsEditor.statusArchived')}</td>
                <td className="issued-items-row-actions">
                  <button
                    type="button"
                    className="issued-items-link"
                    disabled={busy}
                    onClick={() => saveVariant(variant, { isActive: !variant.isActive })}
                  >
                    {variant.isActive ? t('issuedItems.variantsEditor.archive') : t('issuedItems.variantsEditor.activate')}
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      <p className="issued-items-computed-stock">
        {t('issuedItems.variantsEditor.total', { total: `${total}${unit ? ` ${unit}` : ''}` })}
      </p>

      {hasAttributes ? (
        <p className="customer-form-muted">
          {t('issuedItems.variantsEditor.hasAttributes')}
        </p>
      ) : (
        <div className="issued-items-form-row">
          <FormField label={t('issuedItems.form.variantName')} htmlFor="tpl-new-variant-label" hint={t('issuedItems.form.variantNameHint')}>
            <input
              id="tpl-new-variant-label"
              value={draft.label}
              maxLength={150}
              disabled={busy}
              onChange={(e) => setDraft((d) => ({ ...d, label: e.target.value }))}
            />
          </FormField>
          <FormField label={t('issuedItems.form.stock')} htmlFor="tpl-new-variant-stock">
            <input
              id="tpl-new-variant-stock"
              type="number"
              min={0}
              value={draft.stock}
              disabled={busy}
              onChange={(e) => setDraft((d) => ({ ...d, stock: e.target.value }))}
            />
          </FormField>
          <FormField label={t('issuedItems.form.threshold')} htmlFor="tpl-new-variant-threshold" hint={t('issuedItems.form.thresholdHint')}>
            <input
              id="tpl-new-variant-threshold"
              type="number"
              min={0}
              value={draft.threshold}
              disabled={busy}
              onChange={(e) => setDraft((d) => ({ ...d, threshold: e.target.value }))}
            />
          </FormField>
          <Button variant="secondary" onClick={addVariant} disabled={busy}>
            {t('issuedItems.form.addVariant')}
          </Button>
        </div>
      )}
    </div>
  )
}
