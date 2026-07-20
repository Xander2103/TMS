import { useState } from 'react'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { Modal } from '../../../components/ui/Modal'
import { useAuth } from '../../auth/authContextValue'
import { CONFLICT_CATEGORY_LABELS, CONFLICT_SEVERITY_META, type PlanningConflict } from '../../planning/types'
import './conflict-dialog.css'

interface ConflictDialogProps {
  title?: string
  message: string
  conflicts: PlanningConflict[]
  busy?: boolean
  onClose: () => void
  /** Present only when the rejected action can be retried with an override. */
  onOverride?: (reason: string) => void
}

/**
 * Shows the exact structured conflicts a rejected planning action produced. When every
 * blocking conflict is overrideable and the user holds the required permission, an
 * override-with-mandatory-reason retry is offered.
 */
export function ConflictDialog({ title, message, conflicts, busy = false, onClose, onOverride }: ConflictDialogProps) {
  const { hasPermission } = useAuth()
  const [reason, setReason] = useState('')

  const blocking = conflicts.filter((c) => c.severity === 'Blocking')
  const canOverride =
    onOverride !== undefined &&
    blocking.length > 0 &&
    blocking.every((c) => c.overrideAllowed && (!c.requiredPermission || hasPermission(c.requiredPermission)))

  return (
    <Modal title={title ?? 'Planningsconflicten'} onClose={onClose} busy={busy}>
      <p className="cd-message">{message}</p>
      <ul className="cd-list">
        {conflicts.map((conflict, index) => {
          const meta = CONFLICT_SEVERITY_META[conflict.severity]
          return (
            <li key={`${conflict.code}-${index}`} className="cd-item">
              <div className="cd-item-head">
                <Badge tone={meta.tone}>{meta.label}</Badge>
                <span className="cd-category">{CONFLICT_CATEGORY_LABELS[conflict.category] ?? conflict.category}</span>
              </div>
              <p className="cd-description">{conflict.description}</p>
              {conflict.suggestedAction && <p className="cd-suggestion">{conflict.suggestedAction}</p>}
            </li>
          )
        })}
      </ul>
      {canOverride && (
        <div className="cd-override">
          <label htmlFor="cd-override-reason">Reden voor overschrijven (verplicht)</label>
          <textarea
            id="cd-override-reason"
            rows={2}
            value={reason}
            onChange={(event) => setReason(event.target.value)}
            placeholder="Waarom mag deze planning toch doorgaan?"
            disabled={busy}
          />
        </div>
      )}
      <div className="cd-actions">
        <Button variant="secondary" onClick={onClose} disabled={busy}>
          Sluiten
        </Button>
        {canOverride && (
          <Button onClick={() => onOverride(reason.trim())} disabled={busy || reason.trim().length === 0}>
            Overschrijven en doorgaan
          </Button>
        )}
      </div>
    </Modal>
  )
}
