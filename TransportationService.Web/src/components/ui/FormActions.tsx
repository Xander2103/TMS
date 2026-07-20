import type { ReactNode } from 'react'
import './FormActions.css'

interface FormActionsProps {
  children: ReactNode
  /** Shows an "unsaved changes" hint next to the buttons. */
  dirty?: boolean
  /**
   * "bottom" (default) renders the sticky bottom bar; "top" renders a static bar for
   * the top of a long form. Render the same buttons in both places — one submit handler.
   */
  position?: 'top' | 'bottom'
}

/** Form action bar — save/cancel stay reachable at the top and (sticky) bottom of long forms. */
export function FormActions({ children, dirty, position = 'bottom' }: FormActionsProps) {
  return (
    <div className={position === 'top' ? 'ui-form-actions ui-form-actions-top' : 'ui-form-actions'}>
      {dirty && <span className="ui-form-actions-dirty">Niet-opgeslagen wijzigingen</span>}
      <div className="ui-form-actions-buttons">{children}</div>
    </div>
  )
}
