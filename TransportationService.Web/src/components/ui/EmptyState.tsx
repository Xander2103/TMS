import type { ReactNode } from 'react'
import './EmptyState.css'

interface EmptyStateProps {
  message: string
  action?: ReactNode
}

/** Consistent placeholder shown when a list or panel has no data. */
export function EmptyState({ message, action }: EmptyStateProps) {
  return (
    <div className="empty-state">
      <p>{message}</p>
      {action && <div className="empty-state-action">{action}</div>}
    </div>
  )
}
