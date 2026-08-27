import { useEffect, useState } from 'react'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { ValidationSummary } from '../../../components/ui/ValidationSummary'
import { describeApiError } from '../../../api/problemDetails'
import { useLocale } from '../../../i18n/localeContext'
import type { TranslateFn } from '../../../i18n/localeContext'
import { listActivityTypes, type ActivityType } from '../api/activityTypesApi'
import { activityTypeIcon } from '../activityTypeIcons'
import { addDossierActivity } from '../api/dossiersApi'
import type { DossierDetail } from '../types'
import '../../transport-orders/components/transport-order-form.css'

interface AddActivityDialogProps {
  dossier: DossierDetail
  onClose: () => void
  /** Receives the refreshed dossier after a successful add. */
  onAdded: (dossier: DossierDetail) => void
  /** 409: the dossier changed elsewhere — the page shows the rebase banner. */
  onConflict: (err: unknown) => boolean
}

/** Capability hint under a type name in the picker. */
function capabilityHint(t: TranslateFn, type: ActivityType): string {
  const parts: string[] = []
  if (type.hasStops) parts.push(t('dossiers.addActivity.capTransportOrder'))
  if (type.supportsGoods) parts.push(t('dossiers.addActivity.capGoods'))
  if (type.allowsDuration) parts.push(t('dossiers.addActivity.capDuration'))
  return parts.length > 0 ? parts.join(' · ') : t('dossiers.addActivity.capDescriptive')
}

/** §11 [+ Activiteit]: type picker (active types), optional label, createLinkedOrder default ON. */
export function AddActivityDialog({ dossier, onClose, onAdded, onConflict }: AddActivityDialogProps) {
  const { t } = useLocale()
  const [types, setTypes] = useState<ActivityType[]>([])
  const [selectedTypeId, setSelectedTypeId] = useState<string | null>(null)
  const [label, setLabel] = useState('')
  const [createLinkedOrder, setCreateLinkedOrder] = useState(true)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    listActivityTypes()
      .then((all) => setTypes(all.filter((t) => t.isActive).sort((a, b) => a.sortOrder - b.sortOrder)))
      .catch(() => setTypes([]))
  }, [])

  const selectedType = types.find((t) => t.id === selectedTypeId) ?? null

  async function submit() {
    if (!selectedType) {
      setError(t('dossiers.addActivity.chooseType'))
      return
    }
    setBusy(true)
    setError(null)
    try {
      const updated = await addDossierActivity(dossier.id, {
        activityTypeId: selectedType.id,
        label: label.trim() || null,
        createLinkedOrder: selectedType.hasStops && createLinkedOrder,
        version: dossier.version,
      })
      onAdded(updated)
      onClose()
    } catch (err) {
      if (onConflict(err)) {
        onClose()
        return
      }
      setError(describeApiError(err, t('dossiers.addActivity.addFailed')).message)
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      title={t('dossiers.addActivity.title')}
      onClose={onClose}
      busy={busy}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            {t('ui.actions.cancel')}
          </Button>
          <Button onClick={() => void submit()} disabled={busy || !selectedType}>
            {t('ui.actions.add')}
          </Button>
        </>
      }
    >
      <ValidationSummary message={error} />
      <div className="dossier-type-picker" role="radiogroup" aria-label={t('dossiers.addActivity.typePicker')}>
        {types.map((type) => {
          const Icon = activityTypeIcon(type.icon)
          const selected = type.id === selectedTypeId
          return (
            <button
              key={type.id}
              type="button"
              role="radio"
              aria-checked={selected}
              className={`new-dossier-tile${selected ? ' new-dossier-tile-selected' : ''}`}
              onClick={() => setSelectedTypeId(type.id)}
              disabled={busy}
            >
              <Icon size={20} aria-hidden="true" />
              <span>
                {type.name}
                <span className="dossier-type-hint">{capabilityHint(t, type)}</span>
              </span>
            </button>
          )
        })}
        {types.length === 0 && <p className="placeholder-text">{t('dossiers.addActivity.noTypes')}</p>}
      </div>
      <FormField label={t('dossiers.drawer.label')} htmlFor="aa-label" hint={t('dossiers.addActivity.labelHint')}>
        <input id="aa-label" value={label} onChange={(event) => setLabel(event.target.value)} maxLength={200} disabled={busy} />
      </FormField>
      {selectedType?.hasStops && (
        <label className="tof-checkbox">
          <input
            type="checkbox"
            checked={createLinkedOrder}
            onChange={(event) => setCreateLinkedOrder(event.target.checked)}
            disabled={busy}
          />
          {t('dossiers.addActivity.createOrderNow')}
        </label>
      )}
    </Modal>
  )
}
