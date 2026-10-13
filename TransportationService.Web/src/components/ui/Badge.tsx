import type { ReactNode } from 'react'
import './Badge.css'

export type BadgeTone = 'neutral' | 'success' | 'warning' | 'danger' | 'info'

interface BadgeProps {
  children: ReactNode
  tone?: BadgeTone
  /** Optional tooltip (native `title` attribute), e.g. a completeness percentage breakdown. */
  title?: string
}

/** Small status pill. Tone maps to a semantic colour set that works in light and dark themes. */
export function Badge({ children, tone = 'neutral', title }: BadgeProps) {
  return (
    <span className={`ui-badge ui-badge-${tone}`} title={title}>
      {children}
    </span>
  )
}
