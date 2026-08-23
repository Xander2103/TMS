import { useState } from 'react'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { useLocale } from '../../../i18n/localeContext'
import type { IssuedItemTemplate } from '../../issued-items/issuedItemsApi'
import { getTemplateDetail, type IssuedItemVariant } from '../../issued-items/inventoryApi'
import type { PreparedIssuedItem } from '../utils/preparedFollowUp'

interface PreparedIssuedItemsEditorProps {
  value: PreparedIssuedItem[]
  onChange: (next: PreparedIssuedItem[]) => void
  templates: IssuedItemTemplate[]
  isLoading?: boolean
}

function newKey(): string {
  return crypto.randomUUID()
}

/**
 * Create-mode company-asset editor: prepares issuances into client-side state. The records
 * are created after the employee exists (see preparedFollowUp), so a failed employee create
 * never consumes stock. Variant-enabled templates require a variant choice; the available
 * stock per variant is shown as a preview.
 */
export function PreparedIssuedItemsEditor({ value, onChange, templates, isLoading }: PreparedIssuedItemsEditorProps) {
  const { t } = useLocale()
  const today = new Date().toISOString().slice(0, 10)
  const [variantsByTemplate, setVariantsByTemplate] = useState<Record<string, IssuedItemVariant[]>>({})

  async function addFromTemplate(templateId: string) {
    const template = templates.find((candidate) => candidate.id === templateId)
    if (!template) return
    const item: PreparedIssuedItem = {
      key: newKey(),
      templateId: template.id,
      name: template.name,
      category: template.category,
      quantity: template.defaultQuantity || 1,
      serialNumber: '',
      issuedDate: today,
      notes: '',
      returnRequired: template.returnRequired,
      requiresSerialNumber: template.requiresSerialNumber,
      variantsEnabled: template.variantsEnabled,
      variantId: null,
      variantLabel: '',
    }
    onChange([...value, item])
    if (template.variantsEnabled && !variantsByTemplate[template.id]) {
      try {
        const detail = await getTemplateDetail(template.id)
        setVariantsByTemplate((map) => ({ ...map, [template.id]: detail.variants.filter((v) => v.isActive) }))
      } catch {
        /* variant list unavailable; the backend validates the final issuance */
      }
    }
  }

  function patch(key: string, changes: Partial<PreparedIssuedItem>) {
    onChange(value.map((item) => (item.key === key ? { ...item, ...changes } : item)))
  }

  function remove(key: string) {
    onChange(value.filter((item) => item.key !== key))
  }

  return (
    <div className="prepared-editor">
      <p className="ui-form-section-description">
        {t('employees.create.preparedItemsIntro')}
      </p>

      {value.length === 0 && <p className="placeholder-text">{t('employees.create.preparedItemsEmpty')}</p>}

      {value.map((item, index) => {
        const variants = item.templateId ? variantsByTemplate[item.templateId] ?? [] : []
        const selectedVariant = variants.find((v) => v.id === item.variantId) ?? null
        return (
          <div key={item.key} className="prepared-editor-row">
            <div className="prepared-editor-file">
              <strong>{item.name || t('employees.create.preparedItemFallbackName', { index: index + 1 })}</strong>
              {item.category && <span className="customer-form-muted"> · {item.category}</span>}
              {item.returnRequired && <span className="customer-form-muted"> · {t('employees.create.preparedItemReturnRequired')}</span>}
            </div>
            {item.variantsEnabled && (
              <FormField label={t('employees.create.preparedItemVariant')} htmlFor={`pi-variant-${item.key}`} required>
                <select
                  id={`pi-variant-${item.key}`}
                  value={item.variantId ?? ''}
                  onChange={(e) => {
                    const variant = variants.find((v) => v.id === e.target.value) ?? null
                    patch(item.key, { variantId: variant?.id ?? null, variantLabel: variant?.label ?? '' })
                  }}
                >
                  <option value="">{t('employees.create.preparedItemVariantPlaceholder')}</option>
                  {variants.map((variant) => (
                    <option key={variant.id} value={variant.id}>
                      {t('employees.create.preparedItemVariantOption', { label: variant.label, stock: variant.currentStock })}
                    </option>
                  ))}
                </select>
              </FormField>
            )}
            {selectedVariant && selectedVariant.currentStock < item.quantity && (
              <p className="issued-items-stock-preview issued-items-stock-preview-warning" role="status">
                {t('employees.create.preparedItemStockWarning', { quantity: item.quantity, stock: selectedVariant.currentStock })}
              </p>
            )}
            <FormField label={t('employees.create.preparedItemQuantity')} htmlFor={`pi-qty-${item.key}`}>
              <input
                id={`pi-qty-${item.key}`}
                type="number"
                min={1}
                value={item.quantity}
                onChange={(e) => patch(item.key, { quantity: Math.max(1, Number(e.target.value) || 1) })}
              />
            </FormField>
            {item.requiresSerialNumber && (
              <FormField label={t('employees.create.preparedItemSerialNumber')} htmlFor={`pi-serial-${item.key}`} required>
                <input
                  id={`pi-serial-${item.key}`}
                  value={item.serialNumber}
                  onChange={(e) => patch(item.key, { serialNumber: e.target.value })}
                  maxLength={100}
                />
              </FormField>
            )}
            <FormField label={t('employees.create.preparedItemIssuedDate')} htmlFor={`pi-date-${item.key}`}>
              <input
                id={`pi-date-${item.key}`}
                type="date"
                value={item.issuedDate}
                onChange={(e) => patch(item.key, { issuedDate: e.target.value })}
              />
            </FormField>
            <FormField label={t('employees.create.preparedItemNotes')} htmlFor={`pi-notes-${item.key}`}>
              <input
                id={`pi-notes-${item.key}`}
                value={item.notes}
                onChange={(e) => patch(item.key, { notes: e.target.value })}
                maxLength={500}
              />
            </FormField>
            <div className="prepared-editor-actions">
              <Button variant="ghost" onClick={() => remove(item.key)} aria-label={t('employees.create.preparedItemRemove', { name: item.name })}>
                {t('employees.form.remove')}
              </Button>
            </div>
          </div>
        )
      })}

      <FormField label={t('employees.create.preparedItemAdd')} htmlFor="pi-add-template" hint={t('employees.create.preparedItemAddHint')}>
        <select
          id="pi-add-template"
          value=""
          disabled={isLoading || templates.length === 0}
          onChange={(e) => {
            if (e.target.value) void addFromTemplate(e.target.value)
            e.target.value = ''
          }}
        >
          <option value="">
            {isLoading
              ? t('employees.create.preparedItemTemplatesLoading')
              : templates.length === 0
                ? t('employees.create.preparedItemTemplatesEmpty')
                : t('employees.create.preparedItemTemplatePlaceholder')}
          </option>
          {templates.map((template) => (
            <option key={template.id} value={template.id}>
              {template.name} ({template.category})
            </option>
          ))}
        </select>
      </FormField>
    </div>
  )
}
