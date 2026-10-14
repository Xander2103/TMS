import { useState } from 'react'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { useLocale } from '../../../i18n/localeContext'
import type { DossierSearchFilters } from '../pages/dossierSearchParams'
import {
  activePreset,
  applyPreset,
  DOSSIER_PRESETS,
  filterPatchOf,
  matchesPatch,
  MAX_VIEWS,
  newViewId,
  presetPatch,
  type DossierPresetId,
  type PresetContext,
  type SavedDossierView,
} from '../pages/dossierViews'

interface DossierViewsBarProps {
  filters: DossierSearchFilters
  context: PresetContext
  savedViews: SavedDossierView[]
  onApply: (next: DossierSearchFilters) => void
  onSaveView: (view: SavedDossierView) => void
  onRemoveView: (id: string) => void
}

/**
 * B11 "Weergave:" — the preset chips every planner needs (my open work, unplanned this week,
 * not priced, confirmed but not invoiced, open incident), the planner's own saved views, and
 * "+ Opslaan als weergave" for a filter set that is not a preset yet. A chip is pressed when the
 * current filters are exactly its set; refining a view simply un-presses it — the filter chips
 * below the bar keep explaining what is listed.
 */
export function DossierViewsBar({ filters, context, savedViews, onApply, onSaveView, onRemoveView }: DossierViewsBarProps) {
  const { t } = useLocale()
  const active = activePreset(filters, context)
  const activeSaved = savedViews.find((view) => matchesPatch(filters, view.filters)) ?? null
  const currentPatch = filterPatchOf(filters)
  const canSave = Object.keys(currentPatch).length > 0 && active === null && activeSaved === null && savedViews.length < MAX_VIEWS && Boolean(context.userId)
  const [saving, setSaving] = useState(false)
  const [name, setName] = useState('')
  const [error, setError] = useState<string | null>(null)

  function submitSave() {
    const trimmed = name.trim()
    if (!trimmed) {
      setError(t('dossiers.views.nameRequired'))
      return
    }
    onSaveView({ id: newViewId(), name: trimmed.slice(0, 60), filters: currentPatch })
    setSaving(false)
    setName('')
    setError(null)
  }

  return (
    <div className="dossier-views" role="group" aria-label={t('dossiers.views.label')}>
      <span className="dossier-views-label">{t('dossiers.views.label')}</span>
      <ul className="dossier-views-list">
        {DOSSIER_PRESETS.map((id: DossierPresetId) => {
          const patch = presetPatch(id, context)
          if (!patch) return null
          return (
            <li key={id}>
              <button
                type="button"
                className="dossier-view-chip"
                aria-pressed={active === id}
                onClick={() => onApply(applyPreset(filters, patch))}
              >
                {t(`dossiers.views.presets.${id}`)}
              </button>
            </li>
          )
        })}
        {savedViews.map((view) => (
          <li key={view.id} className="dossier-view-saved">
            <button
              type="button"
              className="dossier-view-chip"
              aria-pressed={activeSaved?.id === view.id}
              onClick={() => onApply(applyPreset(filters, view.filters))}
            >
              {view.name}
            </button>
            <button
              type="button"
              className="dossier-view-remove"
              aria-label={t('dossiers.views.remove', { name: view.name })}
              onClick={() => onRemoveView(view.id)}
            >
              ✕
            </button>
          </li>
        ))}
      </ul>
      {canSave && (
        <button type="button" className="link-button dossier-views-save" onClick={() => setSaving(true)}>
          {t('dossiers.views.save')}
        </button>
      )}

      {saving && (
        <Modal
          title={t('dossiers.views.saveTitle')}
          onClose={() => setSaving(false)}
          footer={
            <>
              <Button variant="secondary" onClick={() => setSaving(false)}>{t('ui.actions.cancel')}</Button>
              <Button onClick={submitSave}>{t('ui.actions.save')}</Button>
            </>
          }
        >
          <p className="dossier-views-save-intro">{t('dossiers.views.saveIntro')}</p>
          <FormField label={t('dossiers.views.nameLabel')} htmlFor="dossier-view-name" required error={error ?? undefined}>
            <input
              id="dossier-view-name"
              value={name}
              maxLength={60}
              autoFocus
              onChange={(event) => {
                setName(event.target.value)
                setError(null)
              }}
              onKeyDown={(event) => {
                if (event.key === 'Enter') {
                  event.preventDefault()
                  submitSave()
                }
              }}
            />
          </FormField>
        </Modal>
      )}
    </div>
  )
}
