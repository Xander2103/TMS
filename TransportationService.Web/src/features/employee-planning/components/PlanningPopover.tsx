import { useEffect, useRef, type ReactNode } from 'react'
import { useEscapeLayer } from '../../../components/ui/escapeLayerStack'

interface PlanningPopoverProps {
  open: boolean
  onClose: () => void
  /** The trigger button; focus returns here on close and outside clicks on it do not re-open. */
  anchorRef: React.RefObject<HTMLElement | null>
  label: string
  children: ReactNode
  className?: string
  /**
   * Where focus lands when the panel opens. `first-control` (default) moves it to the first
   * enabled control, which suits a panel that leads with a search box. `panel` focuses the
   * panel itself: focus is inside the dialog (Escape works, Tab reaches the first control) but
   * no control receives a focus event — a combobox that opens on focus stays closed until the
   * user actually clicks or tabs into it.
   */
  initialFocus?: 'first-control' | 'panel'
}

/**
 * Compact anchored panel for the toolbar (filters, employee picker): Escape closes (top layer
 * only, shared stack with modals), a click outside closes, focus moves into the panel on open
 * and back to the trigger on close. Not a modal: the page behind stays usable and scrollable.
 */
export function PlanningPopover({ open, onClose, anchorRef, label, children, className, initialFocus = 'first-control' }: PlanningPopoverProps) {
  const panelRef = useRef<HTMLDivElement>(null)

  useEscapeLayer(open, onClose)

  useEffect(() => {
    if (!open) return
    const panel = panelRef.current
    if (initialFocus === 'panel') {
      panel?.focus()
    } else {
      const first = panel?.querySelector<HTMLElement>(
        'input:not([disabled]), button:not([disabled]), select:not([disabled]), [tabindex]:not([tabindex="-1"])',
      )
      first?.focus()
    }
    const anchor = anchorRef.current
    return () => {
      if (anchor && document.contains(anchor)) anchor.focus()
    }
  }, [open, anchorRef, initialFocus])

  useEffect(() => {
    if (!open) return
    function handlePointerDown(event: PointerEvent) {
      const target = event.target as Node
      if (panelRef.current?.contains(target) || anchorRef.current?.contains(target)) return
      onClose()
    }
    document.addEventListener('pointerdown', handlePointerDown)
    return () => document.removeEventListener('pointerdown', handlePointerDown)
  }, [open, onClose, anchorRef])

  if (!open) return null
  return (
    <div ref={panelRef} role="dialog" aria-label={label} tabIndex={-1} className={`ep-popover${className ? ` ${className}` : ''}`}>
      {children}
    </div>
  )
}
