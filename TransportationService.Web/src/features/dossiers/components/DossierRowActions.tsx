import { useEffect, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import { useLocale } from '../../../i18n/localeContext'

interface DossierRowActionsProps {
  dossierNumber: string
  onOpen: () => void
  /** Each lifecycle action is offered only when its handler is passed (status + permission decide). */
  onConfirm?: () => void
  onCancel?: () => void
  onReopen?: () => void
}

/**
 * Compact "⋯" menu per dossier row. The popover renders through a portal (the table wrapper
 * scrolls and would clip it) and is positioned from the button's rect; it closes on outside
 * click, Escape, scroll and resize. Clicks and keys never reach the row (which navigates). The
 * menu only picks an action — the lifecycle dialogs and services are the ones the detail page uses.
 */
export function DossierRowActions({ dossierNumber, onOpen, onConfirm, onCancel, onReopen }: DossierRowActionsProps) {
  const { t } = useLocale()
  const buttonRef = useRef<HTMLButtonElement>(null)
  const menuRef = useRef<HTMLDivElement>(null)
  const [position, setPosition] = useState<{ top: number; right: number } | null>(null)
  const open = position !== null

  function toggle() {
    if (open) {
      setPosition(null)
      return
    }
    const rect = buttonRef.current?.getBoundingClientRect()
    setPosition(rect ? { top: rect.bottom + 4, right: Math.max(8, window.innerWidth - rect.right) } : { top: 0, right: 8 })
  }

  useEffect(() => {
    if (!open) return
    const close = () => setPosition(null)
    function onPointerDown(event: PointerEvent) {
      const target = event.target as Node
      if (menuRef.current?.contains(target) || buttonRef.current?.contains(target)) return
      close()
    }
    // Escape while focus is elsewhere on the page; inside the menu/button the wrapper handles it.
    function onKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape') close()
    }
    document.addEventListener('pointerdown', onPointerDown)
    document.addEventListener('keydown', onKeyDown)
    window.addEventListener('scroll', close, true)
    window.addEventListener('resize', close)
    return () => {
      document.removeEventListener('pointerdown', onPointerDown)
      document.removeEventListener('keydown', onKeyDown)
      window.removeEventListener('scroll', close, true)
      window.removeEventListener('resize', close)
    }
  }, [open])

  const select = (action: () => void) => () => {
    setPosition(null)
    action()
  }

  return (
    <span
      className="dossier-row-actions"
      onClick={(event) => event.stopPropagation()}
      // Keys stop here so Enter/Space never open the row. That also keeps them from the document
      // listener above, so Escape from the button or a menu item (the portal bubbles through
      // React) is handled on the spot.
      onKeyDown={(event) => {
        event.stopPropagation()
        if (event.key === 'Escape' && open) {
          setPosition(null)
          buttonRef.current?.focus()
        }
      }}
    >
      <button
        ref={buttonRef}
        type="button"
        className="dossier-row-actions-button"
        aria-label={t('dossiers.list.actions', { number: dossierNumber })}
        aria-haspopup="menu"
        aria-expanded={open}
        onClick={toggle}
      >
        ⋯
      </button>
      {open &&
        createPortal(
          <div
            ref={menuRef}
            className="dossier-row-menu"
            role="menu"
            style={{ top: position.top, right: position.right }}
            onClick={(event) => event.stopPropagation()}
          >
            <button type="button" role="menuitem" onClick={select(onOpen)}>
              {t('dossiers.list.open')}
            </button>
            {onConfirm && (
              <button type="button" role="menuitem" onClick={select(onConfirm)}>
                {t('dossiers.lifecycle.confirm')}
              </button>
            )}
            {onReopen && (
              <button type="button" role="menuitem" onClick={select(onReopen)}>
                {t('dossiers.lifecycle.reopenShort')}
              </button>
            )}
            {onCancel && (
              <button type="button" role="menuitem" className="dossier-row-menu-danger" onClick={select(onCancel)}>
                {t('dossiers.lifecycle.cancel')}
              </button>
            )}
          </div>,
          document.body,
        )}
    </span>
  )
}
