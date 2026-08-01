import { useState, type FormEvent } from 'react'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import { describeApiError } from '../../../api/problemDetails'
import type { IssuedItemTemplate } from '../issuedItemsApi'
import { updateStockThresholds, type StockThresholdsInput } from '../inventoryApi'

interface ThresholdsForm {
  lowStockThreshold: string
  minimumStock: string
  targetStockLevel: string
  reorderQuantity: string
  allowNegativeStock: boolean
  negativeStockRequiresReason: boolean
}

function toForm(template: IssuedItemTemplate): ThresholdsForm {
  return {
    lowStockThreshold: template.lowStockThreshold != null ? String(template.lowStockThreshold) : '',
    minimumStock: template.minimumStock != null ? String(template.minimumStock) : '',
    targetStockLevel: template.targetStockLevel != null ? String(template.targetStockLevel) : '',
    reorderQuantity: template.reorderQuantity != null ? String(template.reorderQuantity) : '',
    allowNegativeStock: template.allowNegativeStock,
    negativeStockRequiresReason: template.negativeStockRequiresReason,
  }
}

function parseLevel(value: string): { value: number | null; valid: boolean } {
  if (value.trim() === '') return { value: null, valid: true }
  const parsed = Number(value)
  if (!Number.isFinite(parsed) || parsed < 0) return { value: null, valid: false }
  return { value: parsed, valid: true }
}

/**
 * Voorraadregels (drempels + negatieve-voorraadbeleid) van één sjabloon.
 * Alleen zichtbaar met `inventory.manage_thresholds` of `issued_items.manage_templates`.
 */
export function StockThresholdsCard({ template, onSaved }: { template: IssuedItemTemplate; onSaved: () => void }) {
  const { hasPermission } = useAuth()
  const { showSuccess } = useToast()
  const canEdit = hasPermission('inventory.manage_thresholds') || hasPermission('issued_items.manage_templates')

  const [form, setForm] = useState<ThresholdsForm>(() => toForm(template))
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)

  // Resync wanneer het sjabloon opnieuw geladen wordt (bv. na correctie of bewerken):
  // render-time state-adjustment in plaats van een effect (React "adjusting state on prop change").
  const [lastTemplate, setLastTemplate] = useState(template)
  if (template !== lastTemplate) {
    setLastTemplate(template)
    setForm(toForm(template))
  }

  if (!canEdit) return null

  function set<K extends keyof ThresholdsForm>(key: K, value: ThresholdsForm[K]) {
    setForm((f) => ({ ...f, [key]: value }))
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setError(null)

    const lowStockThreshold = parseLevel(form.lowStockThreshold)
    const minimumStock = parseLevel(form.minimumStock)
    const targetStockLevel = parseLevel(form.targetStockLevel)
    const reorderQuantity = parseLevel(form.reorderQuantity)
    if (![lowStockThreshold, minimumStock, targetStockLevel, reorderQuantity].every((p) => p.valid)) {
      setError('Alle drempels moeten lege of niet-negatieve getallen zijn.')
      return
    }
    if (
      minimumStock.value != null &&
      lowStockThreshold.value != null &&
      minimumStock.value > lowStockThreshold.value
    ) {
      setError('Het minimumniveau mag niet hoger zijn dan de waarschuwingsgrens.')
      return
    }

    const input: StockThresholdsInput = {
      lowStockThreshold: lowStockThreshold.value,
      minimumStock: minimumStock.value,
      targetStockLevel: targetStockLevel.value,
      reorderQuantity: reorderQuantity.value,
      allowNegativeStock: form.allowNegativeStock,
      negativeStockRequiresReason: form.negativeStockRequiresReason,
    }
    setSaving(true)
    try {
      await updateStockThresholds(template.id, input)
      showSuccess('Voorraadregels opgeslagen.')
      onSaved()
    } catch (err) {
      setError(describeApiError(err, 'De voorraadregels konden niet worden opgeslagen.').message)
    } finally {
      setSaving(false)
    }
  }

  return (
    <section className="issued-items-card">
      <h2>Voorraadregels</h2>
      <form className="issued-items-form" onSubmit={handleSubmit} noValidate>
        {error && (
          <div className="issued-items-form-error" role="alert">
            {error}
          </div>
        )}
        <div className="issued-items-form-row">
          <FormField label="Waarschuwingsgrens" htmlFor="thr-warning" hint="Onder deze stand krijgt het artikel status Lage voorraad.">
            <input
              id="thr-warning"
              type="number"
              min={0}
              value={form.lowStockThreshold}
              onChange={(e) => set('lowStockThreshold', e.target.value)}
              disabled={saving}
            />
          </FormField>
          <FormField label="Minimumniveau" htmlFor="thr-minimum" hint="Onder deze stand wordt de voorraad kritiek.">
            <input
              id="thr-minimum"
              type="number"
              min={0}
              value={form.minimumStock}
              onChange={(e) => set('minimumStock', e.target.value)}
              disabled={saving}
            />
          </FormField>
        </div>
        <div className="issued-items-form-row">
          <FormField label="Doelvoorraad" htmlFor="thr-target">
            <input
              id="thr-target"
              type="number"
              min={0}
              value={form.targetStockLevel}
              onChange={(e) => set('targetStockLevel', e.target.value)}
              disabled={saving}
            />
          </FormField>
          <FormField label="Bestelhoeveelheid" htmlFor="thr-reorder">
            <input
              id="thr-reorder"
              type="number"
              min={0}
              value={form.reorderQuantity}
              onChange={(e) => set('reorderQuantity', e.target.value)}
              disabled={saving}
            />
          </FormField>
        </div>
        <label className="issued-items-checkbox">
          <input
            type="checkbox"
            checked={form.allowNegativeStock}
            onChange={(e) => set('allowNegativeStock', e.target.checked)}
            disabled={saving}
          />
          <span>Negatieve voorraad toestaan</span>
        </label>
        <label className="issued-items-checkbox">
          <input
            type="checkbox"
            checked={form.negativeStockRequiresReason}
            onChange={(e) => set('negativeStockRequiresReason', e.target.checked)}
            disabled={saving || !form.allowNegativeStock}
          />
          <span>Reden verplicht bij negatieve voorraad</span>
        </label>
        <div>
          <Button type="submit" disabled={saving}>
            {saving ? 'Opslaan…' : 'Voorraadregels opslaan'}
          </Button>
        </div>
      </form>
    </section>
  )
}
