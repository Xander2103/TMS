import { useState } from 'react'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { FormField } from '../../../components/ui/FormField'
import { ValidationSummary } from '../../../components/ui/ValidationSummary'
import { describeApiError } from '../../../api/problemDetails'
import { useLocale } from '../../../i18n/localeContext'
import { createOrderForActivity, deleteDossierActivity, updateDossierActivity } from '../api/dossiersApi'
import type { DossierActivity, DossierDetail } from '../types'
import { SectionDrawer } from './SectionDrawer'

interface ActivityDrawerProps {
  dossier: DossierDetail
  activity: DossierActivity
  canManage: boolean
  onClose: () => void
  /** Receives the refreshed dossier after save/delete/create-order. */
  onUpdated: (dossier: DossierDetail) => void
  /** 409: the dossier changed elsewhere — the page shows the rebase banner. Returns true when handled. */
  onConflict: (err: unknown) => boolean
}

/**
 * §11 activity drawer: label / planning / duur / notities / begeleiding voor standalone
 * activiteiten, plus "Transportopdracht aanmaken" voor transportactiviteiten zonder opdracht.
 */
export function ActivityDrawer({ dossier, activity, canManage, onClose, onUpdated, onConflict }: ActivityDrawerProps) {
  const { t } = useLocale()
  const [label, setLabel] = useState(activity.label ?? '')
  const [plannedDate, setPlannedDate] = useState(activity.plannedDate ?? '')
  const [durationHours, setDurationHours] = useState(activity.durationHours == null ? '' : String(activity.durationHours))
  const [notes, setNotes] = useState(activity.notes ?? '')
  const [linkedActivityId, setLinkedActivityId] = useState(activity.linkedActivityId ?? '')
  const [dirty, setDirty] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [confirmDelete, setConfirmDelete] = useState(false)

  const others = dossier.activities.filter((a) => a.id !== activity.id)
  const needsOrder = activity.hasStops && !activity.linkedTransportOrderId

  const touch = (setter: (value: string) => void) => (value: string) => {
    setter(value)
    setDirty(true)
  }

  async function run(action: () => Promise<DossierDetail>, fallback: string, closeAfter = true) {
    setBusy(true)
    setError(null)
    try {
      const updated = await action()
      onUpdated(updated)
      if (closeAfter) onClose()
    } catch (err) {
      if (onConflict(err)) {
        onClose()
        return
      }
      setError(describeApiError(err, fallback).message)
    } finally {
      setBusy(false)
    }
  }

  function save() {
    const parsedDuration = durationHours.trim() === '' ? null : Number(durationHours.replace(',', '.'))
    if (parsedDuration !== null && (!Number.isFinite(parsedDuration) || parsedDuration < 0)) {
      setError(t('dossiers.drawer.durationInvalid'))
      return
    }
    void run(
      () =>
        updateDossierActivity(dossier.id, activity.id, {
          activityTypeId: activity.activityTypeId,
          label: label.trim() || null,
          plannedDate: plannedDate || null,
          durationHours: parsedDuration,
          linkedActivityId: linkedActivityId || null,
          notes: notes.trim() || null,
          version: dossier.version,
        }),
      t('dossiers.drawer.saveFailed'),
    )
  }

  return (
    <SectionDrawer
      title={activity.label ? `${activity.activityTypeName} — ${activity.label}` : activity.activityTypeName}
      dirty={dirty}
      busy={busy}
      onClose={onClose}
      onSave={canManage ? save : undefined}
      footerExtra={
        canManage ? (
          <Button variant="danger" onClick={() => setConfirmDelete(true)} disabled={busy}>
            {t('ui.actions.delete')}
          </Button>
        ) : undefined
      }
    >
      <ValidationSummary message={error} />

      {needsOrder && canManage && (
        <div className="dossier-activity-order-cta">
          <p>{t('dossiers.drawer.needsOrder')}</p>
          <Button
            onClick={() =>
              void run(
                () => createOrderForActivity(dossier.id, activity.id, dossier.version),
                t('dossiers.drawer.createOrderFailed'),
              )
            }
            disabled={busy}
          >
            {t('dossiers.drawer.createOrder')}
          </Button>
        </div>
      )}

      <FormField label={t('dossiers.drawer.label')} htmlFor="ad-label">
        <input
          id="ad-label"
          value={label}
          onChange={(event) => touch(setLabel)(event.target.value)}
          maxLength={200}
          disabled={busy || !canManage}
        />
      </FormField>
      {!activity.hasStops && (
        <FormField label={t('dossiers.drawer.plannedDate')} htmlFor="ad-date" hint={t('dossiers.drawer.plannedDateHint')}>
          <input
            id="ad-date"
            type="date"
            value={plannedDate}
            onChange={(event) => touch(setPlannedDate)(event.target.value)}
            disabled={busy || !canManage}
          />
        </FormField>
      )}
      {activity.allowsDuration && (
        <FormField label={t('dossiers.drawer.duration')} htmlFor="ad-duration">
          <input
            id="ad-duration"
            type="number"
            min={0}
            step="0.25"
            value={durationHours}
            onChange={(event) => touch(setDurationHours)(event.target.value)}
            disabled={busy || !canManage}
          />
        </FormField>
      )}
      {others.length > 0 && (
        <FormField
          label={t('dossiers.drawer.linked')}
          htmlFor="ad-linked"
          hint={t('dossiers.drawer.linkedHint')}
        >
          <select
            id="ad-linked"
            value={linkedActivityId}
            onChange={(event) => touch(setLinkedActivityId)(event.target.value)}
            disabled={busy || !canManage}
          >
            <option value="">{t('dossiers.drawer.none')}</option>
            {others.map((other) => (
              <option key={other.id} value={other.id}>
                {other.label ? `${other.activityTypeName} — ${other.label}` : other.activityTypeName}
              </option>
            ))}
          </select>
        </FormField>
      )}
      <FormField label={t('dossiers.drawer.notes')} htmlFor="ad-notes">
        <textarea
          id="ad-notes"
          rows={3}
          value={notes}
          onChange={(event) => touch(setNotes)(event.target.value)}
          maxLength={2000}
          disabled={busy || !canManage}
        />
      </FormField>

      {confirmDelete && (
        <ConfirmDialog
          title={t('dossiers.drawer.deleteTitle')}
          message={t('dossiers.drawer.deleteMessage')}
          confirmLabel={t('ui.actions.delete')}
          destructive
          busy={busy}
          onCancel={() => setConfirmDelete(false)}
          onConfirm={() =>
            void run(
              () => deleteDossierActivity(dossier.id, activity.id, dossier.version),
              t('dossiers.drawer.deleteFailed'),
            )
          }
        />
      )}
    </SectionDrawer>
  )
}
