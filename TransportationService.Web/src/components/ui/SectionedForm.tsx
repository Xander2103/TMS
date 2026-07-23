import type { ReactNode } from 'react'
import { SectionNav } from './SectionNav'
import { SectionSelect } from './SectionSelect'
import { SectionedFormBodyContext } from './sectionedFormContext'
import './SectionedForm.css'

export interface SectionDef {
  id: string
  label: string
  optional?: boolean
  hasError?: boolean
  complete?: boolean
  /** Embedded self-saving panel: the shared Save/Cancel actions are hidden on this section. */
  panel?: boolean
  render: () => ReactNode
}

interface SectionedFormProps {
  sections: SectionDef[]
  activeId: string
  onActiveChange: (id: string) => void
  /** Sticky Save/Cancel bar — auto-hidden when the active section has `panel: true`. */
  actions?: ReactNode
}

/**
 * Section-navigation form shell: a horizontal subnav (tablist on desktop, `<select>` on
 * mobile) below the page title, rendering exactly one section body at a time. All field
 * state stays lifted in the parent, so switching sections preserves values.
 */
export function SectionedForm({ sections, activeId, onActiveChange, actions }: SectionedFormProps) {
  const active = sections.find((s) => s.id === activeId) ?? sections[0]
  const navItems = sections.map((s) => ({
    id: s.id,
    label: s.label,
    optional: s.optional,
    hasError: s.hasError,
    complete: s.complete,
  }))

  return (
    <div className="ui-sectioned-form">
      <div className="ui-sectioned-form-nav">
        <SectionNav items={navItems} activeId={active.id} onActiveChange={onActiveChange} />
        <SectionSelect items={navItems} activeId={active.id} onActiveChange={onActiveChange} />
      </div>
      <div
        className="ui-sectioned-form-body"
        role="tabpanel"
        id={`section-panel-${active.id}`}
        aria-labelledby={`section-tab-${active.id}`}
      >
        <SectionedFormBodyContext.Provider value={true}>{active.render()}</SectionedFormBodyContext.Provider>
      </div>
      {actions && !active.panel && <div className="ui-sectioned-form-actions">{actions}</div>}
    </div>
  )
}
