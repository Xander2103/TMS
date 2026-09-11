import { useEffect, useRef, useState, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { useLocale } from '../../i18n/localeContext'
import './Modal.css'

interface ModalProps {
  title: string
  onClose: () => void
  children: ReactNode
  footer?: ReactNode
  /** Prevents backdrop/Escape close while a mutation is in flight. */
  busy?: boolean
}

/**
 * Accessible modal dialog: focus-trapping backdrop, Escape-to-close, scroll-locked body.
 *
 * Rendered through a portal on `document.body` so a dialog opened from inside a page form
 * never becomes a nested `<form>` in the DOM and is never clipped by an `overflow` ancestor.
 * (React still bubbles synthetic events through the component tree; hosts that must not
 * react to a dialog's edits wrap it in `SelfSavingPanel`.) Focus returns to the element that
 * opened the dialog when it closes.
 */
export function Modal({ title, onClose, children, footer, busy = false }: ModalProps) {
  const { t } = useLocale()
  const dialogRef = useRef<HTMLDivElement>(null)
  // Captured during the FIRST render, before commit: content with `autoFocus` (e.g. the contact
  // dialog) already owns focus by the time effects run, and that element disappears with the
  // dialog — capturing it there would leave focus on <body> after close (found in browser smoke).
  const [opener] = useState(() => document.activeElement as HTMLElement | null)

  useEffect(() => {
    const dialog = dialogRef.current
    if (dialog && !dialog.contains(document.activeElement)) {
      const first = dialog.querySelector<HTMLElement>(
        'input:not([type="hidden"]):not([disabled]), select:not([disabled]), textarea:not([disabled]), button:not([disabled]), [tabindex]:not([tabindex="-1"])',
      )
      ;(first ?? dialog).focus()
    }
    return () => {
      if (opener && typeof opener.focus === 'function' && document.contains(opener)) opener.focus()
    }
  }, [opener])

  useEffect(() => {
    function handleKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape' && !busy) onClose()
    }
    document.addEventListener('keydown', handleKeyDown)
    const previousOverflow = document.body.style.overflow
    document.body.style.overflow = 'hidden'
    return () => {
      document.removeEventListener('keydown', handleKeyDown)
      document.body.style.overflow = previousOverflow
    }
  }, [onClose, busy])

  const dialog = (
    <div className="ui-modal-backdrop" onClick={busy ? undefined : onClose}>
      <div
        ref={dialogRef}
        className="ui-modal"
        role="dialog"
        aria-modal="true"
        aria-label={title}
        tabIndex={-1}
        onClick={(event) => event.stopPropagation()}
      >
        <header className="ui-modal-header">
          <h3>{title}</h3>
          <button type="button" className="ui-modal-close" onClick={onClose} disabled={busy} aria-label={t('ui.modal.close')}>
            ×
          </button>
        </header>
        <div className="ui-modal-body">{children}</div>
        {footer && <footer className="ui-modal-footer">{footer}</footer>}
      </div>
    </div>
  )

  return createPortal(dialog, document.body)
}
