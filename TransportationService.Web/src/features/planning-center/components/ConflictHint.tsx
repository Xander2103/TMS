import { useId, type ReactNode } from 'react'
import { Badge, type BadgeTone } from '../../../components/ui/Badge'

interface ConflictHintProps {
  tone: BadgeTone
  /** The compact chip text, e.g. "⚠ 2". */
  label: ReactNode
  /** Heading of the explanation. */
  title: string
  /** Human-readable reasons (never technical codes); the first few are enough to act on. */
  reasons: string[]
  /** Accessible name of the chip ("2 aandachtspunten"). */
  ariaLabel: string
  className?: string
}

/**
 * B09: a warning chip that explains itself on hover AND on keyboard focus, so the planner never
 * has to select a card first to learn why it is flagged. Pure CSS popover (no portal): it lives
 * inside the scrolling board and follows its card.
 */
export function ConflictHint({ tone, label, title, reasons, ariaLabel, className }: ConflictHintProps) {
  const id = useId()
  return (
    <span className={className ? `pc-hint ${className}` : 'pc-hint'} tabIndex={0} aria-label={ariaLabel} aria-describedby={id}>
      <Badge tone={tone}>{label}</Badge>
      <span role="tooltip" id={id} className="pc-hint-pop">
        <strong>{title}</strong>
        {reasons.map((reason, index) => (
          <span key={index} className="pc-hint-line">{reason}</span>
        ))}
      </span>
    </span>
  )
}
