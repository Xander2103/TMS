import type { ReactNode } from 'react'
import './PanelHeader.css'

interface PanelHeaderProps {
  /** Section title. Omit when the hosting section already renders the title (e.g. a FormSection). */
  title?: string
  /** Short muted orientation line under the title. */
  description?: string
  /** Filter controls; rendered left, on the same row as the actions. */
  children?: ReactNode
  /** Primary/secondary actions; rendered right on desktop, stacked below on narrow screens. */
  actions?: ReactNode
  /** Heading level for the title. Default h3. */
  as?: 'h2' | 'h3' | 'h4'
}

/**
 * One toolbar row for a page section: title + description (optional) and filters on the left,
 * actions on the right. Replaces the old pattern of re-using `.page-header` with an inline h3
 * for every panel, which repeated the section title and stacked filters on a separate line.
 */
export function PanelHeader({ title, description, children, actions, as: Heading = 'h3' }: PanelHeaderProps) {
  const hasIntro = Boolean(title || description)
  return (
    <div className="ui-panel-header">
      {hasIntro && (
        <div className="ui-panel-header-intro">
          {title && <Heading className="ui-panel-header-title">{title}</Heading>}
          {description && <p className="ui-panel-header-description">{description}</p>}
        </div>
      )}
      {(children || actions) && (
        <div className="ui-panel-header-toolbar">
          {children && <div className="ui-panel-header-filters">{children}</div>}
          {actions && <div className="ui-panel-header-actions">{actions}</div>}
        </div>
      )}
    </div>
  )
}
