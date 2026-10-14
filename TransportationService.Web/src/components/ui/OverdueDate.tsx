import { AlertTriangle } from 'lucide-react'

interface OverdueDateProps {
  /** The already formatted date (or date-time). */
  children: string
  /** Spoken and tooltip label, e.g. "Achterstallig" / "te laat". */
  label: string
}

/**
 * B14: an overdue deadline in a dense list column — red date plus a named warning icon, so the
 * state is never colour-only and never needs a badge wider than the column.
 */
export function OverdueDate({ children, label }: OverdueDateProps) {
  return (
    <span className="ui-overdue-date" title={label}>
      <AlertTriangle size={14} role="img" aria-label={label} />
      {children}
    </span>
  )
}
