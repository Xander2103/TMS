import { useRef, type KeyboardEvent, type ReactNode } from 'react'
import './Tabs.css'

export interface TabItem {
  id: string
  label: ReactNode
  /** Optional badge/count rendered after the label. */
  badge?: ReactNode
}

interface TabsProps {
  tabs: TabItem[]
  activeId: string
  onChange: (id: string) => void
}

/** Accessible tab bar (roving tabindex, arrow-key navigation). Content is rendered by the parent. */
export function Tabs({ tabs, activeId, onChange }: TabsProps) {
  const listRef = useRef<HTMLDivElement>(null)

  function handleKeyDown(event: KeyboardEvent<HTMLButtonElement>, index: number) {
    let nextIndex: number | null = null
    if (event.key === 'ArrowRight') nextIndex = (index + 1) % tabs.length
    else if (event.key === 'ArrowLeft') nextIndex = (index - 1 + tabs.length) % tabs.length
    else if (event.key === 'Home') nextIndex = 0
    else if (event.key === 'End') nextIndex = tabs.length - 1
    if (nextIndex === null) return
    event.preventDefault()
    onChange(tabs[nextIndex].id)
    const buttons = listRef.current?.querySelectorAll<HTMLButtonElement>('[role="tab"]')
    buttons?.[nextIndex]?.focus()
  }

  return (
    <div className="ui-tabs" role="tablist" ref={listRef}>
      {tabs.map((tab, index) => {
        const active = tab.id === activeId
        return (
          <button
            key={tab.id}
            type="button"
            role="tab"
            id={`tab-${tab.id}`}
            aria-selected={active}
            aria-controls={`tabpanel-${tab.id}`}
            tabIndex={active ? 0 : -1}
            className={active ? 'ui-tab is-active' : 'ui-tab'}
            onClick={() => onChange(tab.id)}
            onKeyDown={(e) => handleKeyDown(e, index)}
          >
            {tab.label}
            {tab.badge != null && <span className="ui-tab-badge">{tab.badge}</span>}
          </button>
        )
      })}
    </div>
  )
}

/** Wrapper that wires a tab panel to its tab for assistive technology. */
export function TabPanel({ tabId, children }: { tabId: string; children: ReactNode }) {
  return (
    <div role="tabpanel" id={`tabpanel-${tabId}`} aria-labelledby={`tab-${tabId}`} className="ui-tabpanel">
      {children}
    </div>
  )
}
