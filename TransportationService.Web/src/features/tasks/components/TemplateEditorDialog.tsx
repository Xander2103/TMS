import { useState } from 'react'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import type { LookupOption } from '../../master-data/types'
import {
  TASK_PRIORITY_LABELS,
  type SaveTaskTemplateInput,
  type SaveTaskTemplateItemInput,
  type TaskPriority,
  type TaskTemplate,
} from '../api/types'
import './tasks.css'

interface TemplateEditorDialogProps {
  initial: TaskTemplate | null
  categories: LookupOption[]
  busy: boolean
  onSubmit: (input: SaveTaskTemplateInput) => void
  onClose: () => void
}

interface DraftItem extends SaveTaskTemplateItemInput {
  /** Local key so React state survives reordering. */
  key: string
}

let draftKeySeed = 0
const nextKey = () => `item-${++draftKeySeed}`

function emptyItem(): DraftItem {
  return {
    key: nextKey(),
    title: '',
    description: null,
    categoryId: null,
    priority: 'Normal',
    dueInDays: null,
    requiresReview: false,
    requiresCompletionNote: false,
    requiresEvidence: false,
  }
}

/**
 * Template editor: name/description/active + the ordered item list. Item order in the list
 * is the sortOrder (moving an item up/down rewrites the order on save).
 */
export function TemplateEditorDialog({ initial, categories, busy, onSubmit, onClose }: TemplateEditorDialogProps) {
  const [name, setName] = useState(initial?.name ?? '')
  const [description, setDescription] = useState(initial?.description ?? '')
  const [isActive, setIsActive] = useState(initial?.isActive ?? true)
  const [items, setItems] = useState<DraftItem[]>(
    initial
      ? [...initial.items]
          .sort((a, b) => a.sortOrder - b.sortOrder)
          .map((item) => ({
            key: nextKey(),
            title: item.title,
            description: item.description,
            categoryId: item.categoryId,
            priority: item.priority,
            dueInDays: item.dueInDays,
            requiresReview: item.requiresReview,
            requiresCompletionNote: item.requiresCompletionNote,
            requiresEvidence: item.requiresEvidence,
          }))
      : [emptyItem()],
  )
  const [error, setError] = useState<string | undefined>()

  function patchItem(index: number, patch: Partial<DraftItem>) {
    setItems((current) => current.map((item, i) => (i === index ? { ...item, ...patch } : item)))
  }

  function moveItem(index: number, delta: -1 | 1) {
    setItems((current) => {
      const target = index + delta
      if (target < 0 || target >= current.length) return current
      const next = [...current]
      ;[next[index], next[target]] = [next[target], next[index]]
      return next
    })
  }

  function submit() {
    if (name.trim().length === 0) {
      setError('Een naam is verplicht.')
      return
    }
    const validItems = items.filter((item) => item.title.trim().length > 0)
    if (validItems.length === 0) {
      setError('Voeg minstens één taak met een titel toe.')
      return
    }
    onSubmit({
      name: name.trim(),
      description: description.trim() || null,
      isActive,
      sortOrder: initial?.sortOrder ?? 0,
      items: validItems.map((item) => ({
        title: item.title.trim(),
        description: item.description,
        categoryId: item.categoryId,
        priority: item.priority,
        dueInDays: item.dueInDays,
        requiresReview: item.requiresReview,
        requiresCompletionNote: item.requiresCompletionNote,
        requiresEvidence: item.requiresEvidence,
      })),
    })
  }

  return (
    <Modal
      title={initial ? 'Sjabloon bewerken' : 'Nieuw sjabloon'}
      onClose={onClose}
      busy={busy}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Annuleren
          </Button>
          <Button onClick={submit} disabled={busy}>
            {busy ? 'Bezig...' : 'Opslaan'}
          </Button>
        </>
      }
    >
      <FormField label="Naam" htmlFor="template-name" required error={error}>
        <input
          id="template-name"
          type="text"
          value={name}
          onChange={(event) => {
            setName(event.target.value)
            if (error) setError(undefined)
          }}
          disabled={busy}
        />
      </FormField>
      <FormField label="Beschrijving" htmlFor="template-description">
        <textarea
          id="template-description"
          rows={2}
          value={description}
          onChange={(event) => setDescription(event.target.value)}
          disabled={busy}
        />
      </FormField>
      <label className="task-check-label">
        <input type="checkbox" checked={isActive} onChange={(event) => setIsActive(event.target.checked)} disabled={busy} />
        Actief
      </label>

      <div className="task-template-items">
        {items.map((item, index) => (
          <div key={item.key} className="task-template-item">
            <div className="task-template-item-head">
              <strong>Taak {index + 1}</strong>
              <div className="task-template-item-actions">
                <Button variant="ghost" onClick={() => moveItem(index, -1)} disabled={busy || index === 0} aria-label={`Taak ${index + 1} omhoog`}>
                  ↑
                </Button>
                <Button
                  variant="ghost"
                  onClick={() => moveItem(index, 1)}
                  disabled={busy || index === items.length - 1}
                  aria-label={`Taak ${index + 1} omlaag`}
                >
                  ↓
                </Button>
                <Button
                  variant="ghost"
                  onClick={() => setItems((current) => current.filter((_, i) => i !== index))}
                  disabled={busy || items.length === 1}
                  aria-label={`Taak ${index + 1} verwijderen`}
                >
                  ✕
                </Button>
              </div>
            </div>
            <div className="task-template-item-grid">
              <FormField label="Titel" htmlFor={`item-title-${item.key}`} required>
                <input
                  id={`item-title-${item.key}`}
                  type="text"
                  value={item.title}
                  onChange={(event) => patchItem(index, { title: event.target.value })}
                  disabled={busy}
                />
              </FormField>
              <FormField label="Categorie" htmlFor={`item-category-${item.key}`}>
                <select
                  id={`item-category-${item.key}`}
                  value={item.categoryId ?? ''}
                  onChange={(event) => patchItem(index, { categoryId: event.target.value || null })}
                  disabled={busy}
                >
                  <option value="">— Geen —</option>
                  {categories.map((category) => (
                    <option key={category.id} value={category.id}>
                      {category.name}
                    </option>
                  ))}
                </select>
              </FormField>
              <FormField label="Prioriteit" htmlFor={`item-priority-${item.key}`}>
                <select
                  id={`item-priority-${item.key}`}
                  value={item.priority}
                  onChange={(event) => patchItem(index, { priority: event.target.value as TaskPriority })}
                  disabled={busy}
                >
                  {Object.entries(TASK_PRIORITY_LABELS).map(([value, label]) => (
                    <option key={value} value={value}>
                      {label}
                    </option>
                  ))}
                </select>
              </FormField>
              <FormField label="Deadline (dagen na start)" htmlFor={`item-due-${item.key}`}>
                <input
                  id={`item-due-${item.key}`}
                  type="number"
                  min={0}
                  value={item.dueInDays ?? ''}
                  onChange={(event) =>
                    patchItem(index, { dueInDays: event.target.value === '' ? null : Number(event.target.value) })
                  }
                  disabled={busy}
                />
              </FormField>
            </div>
            <FormField label="Beschrijving" htmlFor={`item-description-${item.key}`}>
              <textarea
                id={`item-description-${item.key}`}
                rows={2}
                value={item.description ?? ''}
                onChange={(event) => patchItem(index, { description: event.target.value || null })}
                disabled={busy}
              />
            </FormField>
            <div className="task-template-checks">
              <label className="task-check-label">
                <input
                  type="checkbox"
                  checked={item.requiresReview}
                  onChange={(event) => patchItem(index, { requiresReview: event.target.checked })}
                  disabled={busy}
                />
                Controle vereist
              </label>
              <label className="task-check-label">
                <input
                  type="checkbox"
                  checked={item.requiresCompletionNote}
                  onChange={(event) => patchItem(index, { requiresCompletionNote: event.target.checked })}
                  disabled={busy}
                />
                Notitie verplicht
              </label>
              <label className="task-check-label">
                <input
                  type="checkbox"
                  checked={item.requiresEvidence}
                  onChange={(event) => patchItem(index, { requiresEvidence: event.target.checked })}
                  disabled={busy}
                />
                Bewijs vereist
              </label>
            </div>
          </div>
        ))}
      </div>
      <Button variant="secondary" onClick={() => setItems((current) => [...current, emptyItem()])} disabled={busy}>
        + Taak toevoegen
      </Button>
    </Modal>
  )
}
