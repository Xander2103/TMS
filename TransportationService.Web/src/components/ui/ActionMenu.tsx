import { useEffect, useId, useRef, useState, type KeyboardEvent as ReactKeyboardEvent, type ReactNode } from 'react'
import { ChevronDown } from 'lucide-react'
import { useLocale } from '../../i18n/localeContext'
import './ActionMenu.css'

export interface ActionMenuItem {
  key: string
  label: ReactNode
  onSelect: () => void
  /** Destructive or irreversible: grouped last behind a separator and coloured as such. */
  danger?: boolean
  disabled?: boolean
}

interface ActionMenuProps {
  items: ActionMenuItem[]
  /** Trigger label; defaults to "Meer". */
  label?: string
  disabled?: boolean
  /** Compact trigger for dense detail headers. */
  size?: 'md' | 'sm'
  className?: string
}

/**
 * "Meer ▾" overflow menu for page headers (B03/B04). The one primary action stays a button;
 * rare, backward and destructive actions live in here so they never compete with it. Items
 * are rendered in the order given, except that danger items always close the list behind a
 * separator. Closes on selection, Escape and outside click; arrows move between items.
 */
export function ActionMenu({ items, label, disabled = false, size = 'md', className }: ActionMenuProps) {
  const { t } = useLocale()
  const [open, setOpen] = useState(false)
  const rootRef = useRef<HTMLDivElement>(null)
  const triggerRef = useRef<HTMLButtonElement>(null)
  const listRef = useRef<HTMLDivElement>(null)
  const menuId = useId()

  const regular = items.filter((item) => !item.danger)
  const dangerous = items.filter((item) => item.danger)

  useEffect(() => {
    if (!open) return
    function onPointerDown(event: PointerEvent) {
      if (!rootRef.current?.contains(event.target as Node)) setOpen(false)
    }
    function onKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape') {
        setOpen(false)
        triggerRef.current?.focus()
      }
    }
    document.addEventListener('pointerdown', onPointerDown)
    document.addEventListener('keydown', onKeyDown)
    listRef.current?.querySelector<HTMLButtonElement>('button:not(:disabled)')?.focus()
    return () => {
      document.removeEventListener('pointerdown', onPointerDown)
      document.removeEventListener('keydown', onKeyDown)
    }
  }, [open])

  if (regular.length + dangerous.length === 0) return null

  function onListKeyDown(event: ReactKeyboardEvent<HTMLDivElement>) {
    if (event.key !== 'ArrowDown' && event.key !== 'ArrowUp') return
    const buttons = Array.from(listRef.current?.querySelectorAll<HTMLButtonElement>('button:not(:disabled)') ?? [])
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
        className={item.danger ? 'ui-action-menu-item ui-action-menu-item-danger' : 'ui-action-menu-item'}
        disabled={item.disabled}
        onClick={() => {
          setOpen(false)
          triggerRef.current?.focus()
          item.onSelect()
        }}
      >
        {item.label}
      </button>
    )
  }

  const triggerClass = ['ui-button', 'ui-button-secondary', 'ui-action-menu-trigger', size === 'sm' ? 'ui-action-menu-trigger-sm' : null]
    .filter(Boolean)
    .join(' ')

  return (
    <div ref={rootRef} className={['ui-action-menu', className].filter(Boolean).join(' ')}>
      <button
        ref={triggerRef}
        type="button"
        className={triggerClass}
        aria-haspopup="menu"
        aria-expanded={open}
        aria-controls={open ? menuId : undefined}
        disabled={disabled}
        onClick={() => setOpen((value) => !value)}
      >
        {label ?? t('ui.actions.more')}
        <ChevronDown size={16} aria-hidden="true" />
      </button>
      {open && (
        <div id={menuId} ref={listRef} className="ui-action-menu-list" role="menu" onKeyDown={onListKeyDown}>
          {regular.map(renderItem)}
          {regular.length > 0 && dangerous.length > 0 && <div className="ui-action-menu-separator" role="separator" />}
          {dangerous.map(renderItem)}
        </div>
      )}
    </div>
  )
}
