import { localizeApiError } from '../../../api/problemDetails'
import { useState, type FormEvent } from 'react'
import { ApiError } from '../../../api/apiClient'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { SearchableSelect, type SearchableSelectOption } from '../../../components/ui/SearchableSelect'
import { useLocale } from '../../../i18n/localeContext'
import { ConflictDialog } from '../../planning-center/components/ConflictDialog'
import { replaceTripVehicle } from '../api/planningApi'
import type { PlanningConflict, TripDetail } from '../types'

interface ReplaceVehicleDialogProps {
  trip: Pick<TripDetail, 'id' | 'tripNumber' | 'vehicleId' | 'trailerId'>
  vehicleOptions: SearchableSelectOption[]
  trailerOptions: SearchableSelectOption[]
  vehiclesLoading?: boolean
  onReplaced: (trip: TripDetail) => void
  onClose: () => void
}

/**
 * FL-M13: a planned trip whose vehicle became unavailable gets another one in place — same conflict
 * check as planning, a mandatory reason, and the override path (with its own reason) only for
 * holders of planning.override_restriction. The trip stays Planned; nothing skips validation.
 */
export function ReplaceVehicleDialog({ trip, vehicleOptions, trailerOptions, vehiclesLoading, onReplaced, onClose }: ReplaceVehicleDialogProps) {
  const { t } = useLocale()
  const [vehicleId, setVehicleId] = useState<string | null>(null)
  const [trailerId, setTrailerId] = useState<string | null>(trip.trailerId)
  const [reason, setReason] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [conflicts, setConflicts] = useState<{ message: string; conflicts: PlanningConflict[] } | null>(null)

  async function submit(override: boolean, overrideReason?: string) {
    if (!vehicleId) return
    setBusy(true)
    setError(null)
    try {
      const updated = await replaceTripVehicle(trip.id, {
        vehicleId,
        trailerId,
        reason: override && overrideReason ? `${reason.trim()} — ${overrideReason.trim()}` : reason.trim(),
        ...(override ? { override: true } : {}),
      })
      onReplaced(updated)
    } catch (err) {
      const body = err instanceof ApiError ? (err.body as { message?: string; conflicts?: PlanningConflict[] } | null) : null
      if (err instanceof ApiError && err.status === 409 && body?.conflicts?.length) {
        setConflicts({ message: body.message ?? err.message, conflicts: body.conflicts })
      } else {
        setError(localizeApiError(t, err, t('planning.replaceVehicle.failed')))
      }
    } finally {
      setBusy(false)
    }
  }

  function handleSubmit(event: FormEvent) {
    event.preventDefault()
    if (!vehicleId) {
      setError(t('planning.replaceVehicle.vehicleRequired'))
      return
    }
    if (vehicleId === trip.vehicleId) {
      setError(t('planning.replaceVehicle.sameVehicle'))
      return
    }
    if (!reason.trim()) {
      setError(t('planning.replaceVehicle.reasonRequired'))
      return
    }
    void submit(false)
  }

  if (conflicts) {
    return (
      <ConflictDialog
        title={t('planning.replaceVehicle.conflictTitle')}
        message={conflicts.message}
        conflicts={conflicts.conflicts}
        busy={busy}
        onClose={() => setConflicts(null)}
        onOverride={(overrideReason) => void submit(true, overrideReason)}
      />
    )
  }

  return (
    <Modal
      title={t('planning.replaceVehicle.title', { number: trip.tripNumber })}
      onClose={onClose}
      busy={busy}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            {t('ui.actions.cancel')}
          </Button>
          <Button type="submit" form="replace-vehicle-form" disabled={busy}>
            {busy ? t('ui.actions.busy') : t('planning.replaceVehicle.confirm')}
          </Button>
        </>
      }
    >
      <form id="replace-vehicle-form" onSubmit={handleSubmit} noValidate>
        <p>{t('planning.replaceVehicle.intro')}</p>
        {error && (
          <p className="error-text" role="alert">
            {error}
          </p>
        )}
        <FormField label={t('planning.replaceVehicle.vehicle')} htmlFor="rv-vehicle" required>
          <SearchableSelect
            id="rv-vehicle"
            value={vehicleId}
            onChange={setVehicleId}
            options={vehicleOptions.filter((option) => option.value !== trip.vehicleId)}
            placeholder={t('planning.detail.vehicleSearchPlaceholder')}
            isLoading={vehiclesLoading}
            disabled={busy}
          />
        </FormField>
        <FormField label={t('planning.replaceVehicle.trailer')} htmlFor="rv-trailer">
          <SearchableSelect
            id="rv-trailer"
            value={trailerId}
            onChange={setTrailerId}
            options={trailerOptions}
            placeholder={t('planning.detail.trailerSearchPlaceholder')}
            disabled={busy}
          />
        </FormField>
        <FormField label={t('planning.replaceVehicle.reason')} htmlFor="rv-reason" required>
          <textarea id="rv-reason" rows={2} value={reason} onChange={(e) => setReason(e.target.value)} disabled={busy} maxLength={500} />
        </FormField>
      </form>
    </Modal>
  )
}
