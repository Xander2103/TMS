import type { ReactNode } from 'react'
import './Badge.css'

export type BadgeTone = 'neutral' | 'success' | 'warning' | 'danger' | 'info'

interface BadgeProps {
  children: ReactNode
  tone?: BadgeTone
}

/** Small status pill. Tone maps to a semantic colour set that works in light and dark themes. */
export function Badge({ children, tone = 'neutral' }: BadgeProps) {
  return <span className={`ui-badge ui-badge-${tone}`}>{children}</span>
}
