import type { QualificationStatus } from '../types/qualification'
import { QUALIFICATION_STATUS_LABELS } from '../types/qualification'
import './QualificationStatusBadge.css'

interface QualificationStatusBadgeProps {
  status: QualificationStatus
}

const STATUS_ICON: Record<QualificationStatus, string> = {
  Valid: '✓',
  ExpiringSoon: '⚠',
  Expired: '✕',
  Pending: '…',
  Rejected: '✕',
  Suspended: '⏸',
}

const STATUS_CLASS: Record<QualificationStatus, string> = {
  Valid: 'qualification-status-valid',
  ExpiringSoon: 'qualification-status-expiring-soon',
  Expired: 'qualification-status-expired',
  Pending: 'qualification-status-pending',
  Rejected: 'qualification-status-rejected',
  Suspended: 'qualification-status-suspended',
}

export function QualificationStatusBadge({ status }: QualificationStatusBadgeProps) {
  return (
    <span className={`qualification-status-badge ${STATUS_CLASS[status]}`}>
      <span aria-hidden="true">{STATUS_ICON[status]}</span>
      {QUALIFICATION_STATUS_LABELS[status]}
    </span>
  )
}
