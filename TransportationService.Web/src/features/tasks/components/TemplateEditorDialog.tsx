import { useState } from 'react'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { useLocale } from '../../../i18n/localeContext'
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
  const { t } = useLocale()
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
      setError(t('tasks.templateEditor.nameRequired'))
      return
    }
    const validItems = items.filter((item) => item.title.trim().length > 0)
    if (validItems.length === 0) {
      setError(t('tasks.templateEditor.itemsRequired'))
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
      title={initial ? t('tasks.templateEditor.editTitle') : t('tasks.templateEditor.newTitle')}
      onClose={onClose}
      busy={busy}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            {t('ui.actions.cancel')}
          </Button>
          <Button onClick={submit} disabled={busy}>
            {busy ? t('ui.actions.busy') : t('ui.actions.save')}
          </Button>
        </>
      }
    >
      <FormField label={t('tasks.templateEditor.name')} htmlFor="template-name" required error={error}>
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
      <FormField label={t('tasks.templateEditor.description')} htmlFor="template-description">
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
        {t('tasks.templateEditor.active')}
      </label>

      <div className="task-template-items">
        {items.map((item, index) => (
          <div key={item.key} className="task-template-item">
            <div className="task-template-item-head">
              <strong>{t('tasks.templateEditor.itemHeading', { number: index + 1 })}</strong>
              <div className="task-template-item-actions">
                <Button
                  variant="ghost"
                  onClick={() => moveItem(index, -1)}
                  disabled={busy || index === 0}
                  aria-label={t('tasks.templateEditor.moveUp', { number: index + 1 })}
                >
                  ↑
                </Button>
                <Button
                  variant="ghost"
                  onClick={() => moveItem(index, 1)}
                  disabled={busy || index === items.length - 1}
                  aria-label={t('tasks.templateEditor.moveDown', { number: index + 1 })}
                >
                  ↓
                </Button>
                <Button
                  variant="ghost"
                  onClick={() => setItems((current) => current.filter((_, i) => i !== index))}
                  disabled={busy || items.length === 1}
                  aria-label={t('tasks.templateEditor.removeItem', { number: index + 1 })}
                >
                  ✕
                </Button>
              </div>
            </div>
            <div className="task-template-item-grid">
              <FormField label={t('tasks.templateEditor.itemTitle')} htmlFor={`item-title-${item.key}`} required>
                <input
                  id={`item-title-${item.key}`}
                  type="text"
                  value={item.title}
                  onChange={(event) => patchItem(index, { title: event.target.value })}
                  disabled={busy}
                />
              </FormField>
              <FormField label={t('tasks.templateEditor.category')} htmlFor={`item-category-${item.key}`}>
                <select
                  id={`item-category-${item.key}`}
                  value={item.categoryId ?? ''}
                  onChange={(event) => patchItem(index, { categoryId: event.target.value || null })}
                  disabled={busy}
                >
                  <option value="">{t('tasks.templateEditor.noneOption')}</option>
                  {categories.map((category) => (
                    <option key={category.id} value={category.id}>
                      {category.name}
                    </option>
                  ))}
                </select>
              </FormField>
              <FormField label={t('tasks.templateEditor.priority')} htmlFor={`item-priority-${item.key}`}>
                <select
                  id={`item-priority-${item.key}`}
                  value={item.priority}
                  onChange={(event) => patchItem(index, { priority: event.target.value as TaskPriority })}
                  disabled={busy}
                >
                  {Object.entries(TASK_PRIORITY_LABELS).map(([value, label]) => (
                    <option key={value} value={value}>
                      {t(label)}
                    </option>
                  ))}
                </select>
              </FormField>
              <FormField label={t('tasks.templateEditor.dueInDays')} htmlFor={`item-due-${item.key}`}>
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
            <FormField label={t('tasks.templateEditor.description')} htmlFor={`item-description-${item.key}`}>
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
                {t('tasks.templateEditor.requiresReview')}
              </label>
              <label className="task-check-label">
                <input
                  type="checkbox"
                  checked={item.requiresCompletionNote}
                  onChange={(event) => patchItem(index, { requiresCompletionNote: event.target.checked })}
                  disabled={busy}
                />
                {t('tasks.templateEditor.requiresNote')}
              </label>
              <label className="task-check-label">
                <input
                  type="checkbox"
                  checked={item.requiresEvidence}
                  onChange={(event) => patchItem(index, { requiresEvidence: event.target.checked })}
                  disabled={busy}
                />
                {t('tasks.templateEditor.requiresEvidence')}
              </label>
            </div>
          </div>
        ))}
      </div>
      <Button variant="secondary" onClick={() => setItems((current) => [...current, emptyItem()])} disabled={busy}>
        {t('tasks.templateEditor.addItem')}
      </Button>
    </Modal>
  )
}
