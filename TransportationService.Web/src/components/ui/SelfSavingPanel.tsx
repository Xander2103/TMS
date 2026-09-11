import type { FormEvent, ReactNode, SyntheticEvent } from 'react'

interface SelfSavingPanelProps {
  children: ReactNode
  className?: string
}

function stop(event: SyntheticEvent) {
  event.stopPropagation()
}

/**
 * Isolation boundary for a panel that persists its own data (contacts, addresses, rules …)
 * while being hosted inside a larger page form.
 *
 * Root cause it fixes: React bubbles `change`/`submit` through the component tree, even across
 * portals. A page-level `<form onChange={touch}>` therefore marked itself dirty on every
 * keystroke inside an embedded dialog, and the dialog's Opslaan also ran the page form's
 * submit handler. Stopping propagation here keeps the page's dirty state honest without
 * touching `beforeunload` globally. Native default actions are untouched.
 */
export function SelfSavingPanel({ children, className }: SelfSavingPanelProps) {
  return (
    <div
      className={className}
      data-self-saving-panel=""
      onChange={stop}
      onInput={stop}
      onSubmit={(event: FormEvent) => event.stopPropagation()}
      onReset={stop}
    >
      {children}
    </div>
  )
}
