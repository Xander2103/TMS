import type { ReactNode } from 'react'
import './FormActions.css'

interface FormActionsProps {
  children: ReactNode
  /** Shows an "unsaved changes" hint next to the buttons. */
  dirty?: boolean
}

/** Sticky bottom action bar for long forms — save/cancel stay reachable while scrolling. */
export function FormActions({ children, dirty }: FormActionsProps) {
  return (
    <div className="ui-form-actions">
      {dirty && <span className="ui-form-actions-dirty">Niet-opgeslagen wijzigingen</span>}
      <div className="ui-form-actions-buttons">{children}</div>
    </div>
  )
}
