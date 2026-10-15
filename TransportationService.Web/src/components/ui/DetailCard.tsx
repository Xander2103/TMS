import { useId, type ReactNode } from 'react'
import './DetailCard.css'

interface DetailCardProps {
  title: string
  /** Muted count/badge right after the title ("3 actief", a status badge). */
  aside?: ReactNode
  /** The card's own action, usually a quiet "Bewerken" — never a page-level primary. */
  action?: ReactNode
  /** Anchor for deep links and attention jumps (`#id`). */
  id?: string
  className?: string
  children: ReactNode
}

/**
 * One logical block of a read-first entity detail page (B15/B16/B17/B24): a title row with an
 * optional quiet action, then facts or a short list. Flat — one border, no nested frames — so a
 * page of cards stays calm. Edit mode lives inside the card (the host swaps its children), so
 * the user edits one block intentionally instead of facing the whole entity as a form.
 */
export function DetailCard({ title, aside, action, id, className, children }: DetailCardProps) {
  const headingId = useId()
  return (
    <section id={id} className={['ui-detail-card', className].filter(Boolean).join(' ')} aria-labelledby={headingId}>
      <header className="ui-detail-card-head">
        <h3 id={headingId} className="ui-detail-card-title">
          {title}
        </h3>
        {aside && <span className="ui-detail-card-aside">{aside}</span>}
        {action && <div className="ui-detail-card-action">{action}</div>}
      </header>
      <div className="ui-detail-card-body">{children}</div>
    </section>
  )
}

/** Responsive grid of DetailCards: two columns from ~1100px of content width, one below. */
export function DetailGrid({ children, className }: { children: ReactNode; className?: string }) {
  return <div className={['ui-detail-grid', className].filter(Boolean).join(' ')}>{children}</div>
}
