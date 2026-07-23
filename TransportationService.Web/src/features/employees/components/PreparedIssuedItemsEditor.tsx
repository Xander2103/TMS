import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import type { IssuedItemTemplate } from '../../issued-items/issuedItemsApi'
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
 * are created after the employee exists (see preparedFollowUp). Stock movements are a later
 * wave — a note flags that inventory/availability is not yet wired, but the controls that
 * exist today (template, quantity, serial, date, notes) are fully functional.
 */
export function PreparedIssuedItemsEditor({ value, onChange, templates, isLoading }: PreparedIssuedItemsEditorProps) {
  const today = new Date().toISOString().slice(0, 10)

  function addFromTemplate(templateId: string) {
    const template = templates.find((t) => t.id === templateId)
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
    }
    onChange([...value, item])
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
        Bereid bedrijfsmiddelen voor; ze worden toegewezen zodra de medewerker is aangemaakt.
        Voorraadbeheer en beschikbaarheidscontrole volgen in een latere fase.
      </p>

      {value.length === 0 && <p className="placeholder-text">Nog geen bedrijfsmiddelen voorbereid.</p>}

      {value.map((item, index) => (
        <div key={item.key} className="prepared-editor-row">
          <div className="prepared-editor-file">
            <strong>{item.name || `Bedrijfsmiddel ${index + 1}`}</strong>
            {item.category && <span className="customer-form-muted"> · {item.category}</span>}
            {item.returnRequired && <span className="customer-form-muted"> · terug te bezorgen</span>}
          </div>
          <FormField label="Aantal" htmlFor={`pi-qty-${item.key}`}>
            <input
              id={`pi-qty-${item.key}`}
              type="number"
              min={1}
              value={item.quantity}
              onChange={(e) => patch(item.key, { quantity: Math.max(1, Number(e.target.value) || 1) })}
            />
          </FormField>
          {item.requiresSerialNumber && (
            <FormField label="Serienummer" htmlFor={`pi-serial-${item.key}`} required>
              <input
                id={`pi-serial-${item.key}`}
                value={item.serialNumber}
                onChange={(e) => patch(item.key, { serialNumber: e.target.value })}
                maxLength={100}
              />
            </FormField>
          )}
          <FormField label="Uitgiftedatum" htmlFor={`pi-date-${item.key}`}>
            <input
              id={`pi-date-${item.key}`}
              type="date"
              value={item.issuedDate}
              onChange={(e) => patch(item.key, { issuedDate: e.target.value })}
            />
          </FormField>
          <FormField label="Notities" htmlFor={`pi-notes-${item.key}`}>
            <input
              id={`pi-notes-${item.key}`}
              value={item.notes}
              onChange={(e) => patch(item.key, { notes: e.target.value })}
              maxLength={500}
            />
          </FormField>
          <div className="prepared-editor-actions">
            <Button variant="ghost" onClick={() => remove(item.key)} aria-label={`Bedrijfsmiddel ${item.name} verwijderen`}>
              Verwijderen
            </Button>
          </div>
        </div>
      ))}

      <FormField label="Bedrijfsmiddel toevoegen" htmlFor="pi-add-template" hint="Kies een sjabloon om voor te bereiden.">
        <select
          id="pi-add-template"
          value=""
          disabled={isLoading || templates.length === 0}
          onChange={(e) => {
            if (e.target.value) addFromTemplate(e.target.value)
            e.target.value = ''
          }}
        >
          <option value="">
            {isLoading ? 'Sjablonen laden…' : templates.length === 0 ? 'Geen sjablonen beschikbaar' : '— Kies sjabloon —'}
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
