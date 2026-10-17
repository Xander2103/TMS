import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { ApiError } from '../../api/apiClient'
import { Button } from '../../components/ui/Button'
import { Modal } from '../../components/ui/Modal'
import { useLocale, type TranslateFn } from '../../i18n/localeContext'
import { formatDate } from '../../utils/dates'
import { getAssetReferences } from './fleetReadinessApi'
import type { AssetReferences, FleetAssetKind, TripReference } from './types'
import './fleet-readiness.css'

function useReferences(kind: FleetAssetKind, assetId: string) {
  const [state, setState] = useState<{ references: AssetReferences | null; failed: boolean }>({ references: null, failed: false })
  useEffect(() => {
    let mounted = true
    getAssetReferences(kind, assetId)
      .then((references) => mounted && setState({ references, failed: false }))
      .catch(() => mounted && setState({ references: null, failed: true }))
    return () => {
      mounted = false
    }
  }, [kind, assetId])
  return state
}

function TripLinks({ trips }: { trips: TripReference[] }) {
  return (
    <ul className="fleet-references-list" data-testid="fleet-upcoming-trips">
      {trips.map((trip) => (
        <li key={trip.id}>
          <Link to={`/planning/${trip.id}`}>{trip.tripNumber}</Link> ({formatDate(trip.tripDate)})
        </li>
      ))}
    </ul>
  )
}

/** The non-zero reference counts as Dutch fragments ("4 geplande ritten", "2 onderhoudsbeurten"…). */
function referenceParts(refs: AssetReferences, t: TranslateFn): string[] {
  const parts: [number, string][] = [
    [refs.plannedTrips, 'plannedTrips'],
    [refs.completedTrips, 'completedTrips'],
    [refs.otherTrips, 'otherTrips'],
    [refs.maintenance, 'maintenance'],
    [refs.damage, 'damage'],
    [refs.documents, 'documents'],
    [refs.inspections, 'inspections'],
    [refs.fuel, 'fuel'],
    [refs.leasing, 'leasing'],
    [refs.tachograph, 'tachograph'],
    [refs.tankCards, 'tankCards'],
  ]
  return parts.filter(([count]) => count > 0).map(([count, key]) => t(`fleet.references.${key}`, { count }))
}

interface AssetDeleteDialogProps {
  kind: FleetAssetKind
  assetId: string
  number: string
  isActive: boolean
  canDeactivate: boolean
  onDelete: () => Promise<void>
  onDeactivate: () => void
  onCancel: () => void
}

/**
 * FL-B1: delete is offered only for an asset nothing refers to. With trips, maintenance, damage,
 * documents… on record the dialog explains why it cannot be deleted and offers deactivation; a
 * 409 from the server (references created meanwhile) shows the server's reason.
 */
export function AssetDeleteDialog({ kind, assetId, number, isActive, canDeactivate, onDelete, onDeactivate, onCancel }: AssetDeleteDialogProps) {
  const { t } = useLocale()
  const { references, failed } = useReferences(kind, assetId)
  const [busy, setBusy] = useState(false)
  const [serverMessage, setServerMessage] = useState<string | null>(null)
  const noun = t(kind === 'vehicle' ? 'fleet.references.vehicle' : 'fleet.references.trailer')
  const blocked = Boolean(references?.hasReferences) || serverMessage !== null

  async function confirm() {
    setBusy(true)
    try {
      await onDelete()
    } catch (err) {
      setServerMessage(err instanceof ApiError && err.message ? err.message : t('fleet.references.deleteFailed'))
    } finally {
      setBusy(false)
    }
  }

  const title = blocked ? t('fleet.references.cannotDeleteTitle', { noun }) : t('fleet.references.deleteTitle', { noun })
  return (
    <Modal
      title={title}
      onClose={onCancel}
      busy={busy}
      footer={
        <>
          <Button variant="secondary" onClick={onCancel} disabled={busy}>
            {t('ui.actions.cancel')}
          </Button>
          {blocked ? (
            isActive && canDeactivate && (
              <Button onClick={onDeactivate} disabled={busy}>
                {t('vehicles.detail.deactivate')}
              </Button>
            )
          ) : (
            <Button variant="danger" onClick={() => void confirm()} disabled={busy || references === null}>
              {busy ? t('ui.actions.busy') : t('ui.actions.delete')}
            </Button>
          )}
        </>
      }
    >
      {serverMessage ? (
        <p role="alert">{serverMessage}</p>
      ) : failed ? (
        <p role="alert">{t('fleet.references.loadFailed')}</p>
      ) : references === null ? (
        <p>{t('fleet.common.loading')}</p>
      ) : references.hasReferences ? (
        <>
          <p role="alert">{t('fleet.references.cannotDelete', { noun, number, parts: referenceParts(references, t).join(', ') })}</p>
          {references.upcomingTrips.length > 0 && <TripLinks trips={references.upcomingTrips} />}
          <p>{t(isActive ? 'fleet.references.deactivateInstead' : 'fleet.references.alreadyInactive', { noun })}</p>
        </>
      ) : (
        <p>{t('fleet.references.confirmDelete', { noun, number })}</p>
      )}
    </Modal>
  )
}

interface AssetDeactivateDialogProps {
  kind: FleetAssetKind
  assetId: string
  message: string
  confirmLabel: string
  onConfirm: () => void
  onCancel: () => void
}

/**
 * FL-M8: deactivation stays allowed (nothing is cancelled or unassigned), but the planned trips
 * that will then show a blocking "voertuig inactief" conflict are listed first.
 */
export function AssetDeactivateDialog({ kind, assetId, message, confirmLabel, onConfirm, onCancel }: AssetDeactivateDialogProps) {
  const { t } = useLocale()
  const { references } = useReferences(kind, assetId)
  const noun = t(kind === 'vehicle' ? 'fleet.references.vehicle' : 'fleet.references.trailer')
  const planned = references?.plannedTrips ?? 0
  return (
    <Modal
      title={t(kind === 'vehicle' ? 'vehicles.detail.deactivateTitle' : 'trailers.detail.deactivateTitle')}
      onClose={onCancel}
      footer={
        <>
          <Button variant="secondary" onClick={onCancel}>
            {t('ui.actions.cancel')}
          </Button>
          <Button variant="danger" onClick={onConfirm}>
            {confirmLabel}
          </Button>
        </>
      }
    >
      <p>{message}</p>
      {planned > 0 && references && (
        <div role="alert" data-testid="fleet-deactivate-trips">
          <p>{t('fleet.references.plannedTripsWarning', { noun, count: planned })}</p>
          <TripLinks trips={references.upcomingTrips} />
          <p className="fleet-readiness-muted">{t('fleet.references.plannedTripsConsequence')}</p>
        </div>
      )}
    </Modal>
  )
}
