import { useEffect, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import { MoreHorizontal } from 'lucide-react'
import type { ActionMenuItem } from './ActionMenu'
import './RowActionMenu.css'

interface RowActionMenuProps {
  items: ActionMenuItem[]
  /** Accessible name of the trigger, naming the row ("Acties ORD-0031"). */
  label: string
}

/**
 * B14: the one "⋯" per table row. The visible row keeps its identifier link and at most one
 * important action; everything else lives here. The popover renders through a portal (table
 * wrappers scroll and would clip it), closes on outside click, Escape, scroll and resize, and
 * clicks/keys never reach the row (which usually navigates). Danger items close the list
 * behind a separator; destructive ones still open their own ConfirmDialog in the caller.
 */
export function RowActionMenu({ items, label }: RowActionMenuProps) {
  const buttonRef = useRef<HTMLButtonElement>(null)
  const menuRef = useRef<HTMLDivElement>(null)
  const [position, setPosition] = useState<{ top: number; right: number } | null>(null)
  const open = position !== null
  const regular = items.filter((item) => !item.danger)
  const dangerous = items.filter((item) => item.danger)

  useEffect(() => {
    if (!open) return
    const close = () => setPosition(null)
    function onPointerDown(event: PointerEvent) {
      const target = event.target as Node
      if (menuRef.current?.contains(target) || buttonRef.current?.contains(target)) return
      close()
    }
    function onKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape') close()
    }
    document.addEventListener('pointerdown', onPointerDown)
    document.addEventListener('keydown', onKeyDown)
    window.addEventListener('scroll', close, true)
    window.addEventListener('resize', close)
    menuRef.current?.querySelector<HTMLButtonElement>('button:not(:disabled)')?.focus()
    return () => {
      document.removeEventListener('pointerdown', onPointerDown)
      document.removeEventListener('keydown', onKeyDown)
      window.removeEventListener('scroll', close, true)
      window.removeEventListener('resize', close)
    }
  }, [open])

  if (items.length === 0) return null

  function toggle() {
    if (open) {
      setPosition(null)
      return
    }
    const rect = buttonRef.current?.getBoundingClientRect()
    setPosition(rect ? { top: rect.bottom + 4, right: Math.max(8, window.innerWidth - rect.right) } : { top: 0, right: 8 })
  }

  function onMenuKeyDown(event: React.KeyboardEvent<HTMLDivElement>) {
    if (event.key !== 'ArrowDown' && event.key !== 'ArrowUp') return
    const buttons = Array.from(menuRef.current?.querySelectorAll<HTMLButtonElement>('button:not(:disabled)') ?? [])
    if (buttons.length === 0) return
    const index = buttons.indexOf(document.activeElement as HTMLButtonElement)
    const next = event.key === 'ArrowDown' ? (index + 1) % buttons.length : (index - 1 + buttons.length) % buttons.length
    buttons[next].focus()
    event.preventDefault()
  }

  function renderItem(item: ActionMenuItem) {
    return (
      <button
        key={item.key}
        type="button"
        role="menuitem"
        className={item.danger ? 'ui-row-menu-item ui-row-menu-item-danger' : 'ui-row-menu-item'}
        disabled={item.disabled}
        onClick={() => {
          setPosition(null)
          buttonRef.current?.focus()
          item.onSelect()
        }}
      >
        {item.label}
      </button>
    )
  }

  return (
    <span
      className="ui-row-actions"
      onClick={(event) => event.stopPropagation()}
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
        className="ui-row-actions-button"
        aria-label={label}
        title={label}
        aria-haspopup="menu"
        aria-expanded={open}
        onClick={toggle}
      >
        <MoreHorizontal size={18} aria-hidden="true" />
      </button>
      {open &&
        createPortal(
          <div
            ref={menuRef}
            className="ui-row-menu"
            role="menu"
            aria-label={label}
            style={{ top: position.top, right: position.right }}
            onClick={(event) => event.stopPropagation()}
            onKeyDown={onMenuKeyDown}
          >
            {regular.map(renderItem)}
            {regular.length > 0 && dangerous.length > 0 && <div className="ui-row-menu-separator" role="separator" />}
            {dangerous.map(renderItem)}
          </div>,
          document.body,
        )}
    </span>
  )
}
