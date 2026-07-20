import { useId, useState, type ReactNode } from 'react'
import './FormSection.css'

interface FormSectionProps {
  title: string
  description?: string
  /** Field-grid columns on wide screens (collapses to 1 below 720px). Default 2. */
  columns?: 1 | 2 | 3
  /**
   * Renders the section with a show/hide toggle — for optional groups (initial contact,
   * qualifications, technical details) that users may skip entirely.
   */
  collapsible?: boolean
  /** Initial state for collapsible sections. Default false (collapsed). */
  defaultOpen?: boolean
  children: ReactNode
}

/**
 * Standard form section: legend + optional description + responsive field grid.
 * Full-width children (textareas, sub-editors) can opt out of the grid with
 * className="form-span-all".
 */
export function FormSection({
  title,
  description,
  columns = 2,
  collapsible = false,
  defaultOpen = false,
  children,
}: FormSectionProps) {
  const [open, setOpen] = useState(defaultOpen)
  const bodyId = useId()
  const expanded = !collapsible || open

  return (
    <fieldset className="ui-form-section">
      <legend>
        {collapsible ? (
          <button
            type="button"
            className="ui-form-section-toggle"
            aria-expanded={open}
            aria-controls={bodyId}
            onClick={() => setOpen((value) => !value)}
          >
            <span className="ui-form-section-chevron" aria-hidden="true">
              {open ? '▾' : '▸'}
            </span>
            {title}
          </button>
        ) : (
          title
        )}
      </legend>
      {expanded && (
        <div id={bodyId}>
          {description && <p className="ui-form-section-description">{description}</p>}
          <div className={`ui-form-section-grid ui-form-section-grid-${columns}`}>{children}</div>
        </div>
      )}
    </fieldset>
  )
}
