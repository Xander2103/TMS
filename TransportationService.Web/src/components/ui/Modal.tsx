import { useEffect, type ReactNode } from 'react'
import './Modal.css'

interface ModalProps {
  title: string
  onClose: () => void
  children: ReactNode
  footer?: ReactNode
  /** Prevents backdrop/Escape close while a mutation is in flight. */
  busy?: boolean
}

/** Accessible modal dialog: focus-trapping backdrop, Escape-to-close, scroll-locked body. */
export function Modal({ title, onClose, children, footer, busy = false }: ModalProps) {
  useEffect(() => {
    function handleKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape' && !busy) onClose()
    }
    document.addEventListener('keydown', handleKeyDown)
    document.body.style.overflow = 'hidden'
    return () => {
      document.removeEventListener('keydown', handleKeyDown)
      document.body.style.overflow = ''
    }
  }, [onClose, busy])

  return (
    <div className="ui-modal-backdrop" onClick={busy ? undefined : onClose}>
      <div
        className="ui-modal"
        role="dialog"
        aria-modal="true"
        aria-label={title}
        onClick={(event) => event.stopPropagation()}
      >
        <header className="ui-modal-header">
          <h3>{title}</h3>
          <button type="button" className="ui-modal-close" onClick={onClose} disabled={busy} aria-label="Sluiten">
            ×
          </button>
        </header>
        <div className="ui-modal-body">{children}</div>
        {footer && <footer className="ui-modal-footer">{footer}</footer>}
      </div>
    </div>
  )
}
