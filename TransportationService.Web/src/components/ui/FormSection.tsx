import type { ReactNode } from 'react'
import './FormSection.css'

interface FormSectionProps {
  title: string
  description?: string
  /** Field-grid columns on wide screens (collapses to 1 below 720px). Default 2. */
  columns?: 1 | 2 | 3
  children: ReactNode
}

/**
 * Standard form section: legend + optional description + responsive field grid.
 * Full-width children (textareas, sub-editors) can opt out of the grid with
 * className="form-span-all".
 */
export function FormSection({ title, description, columns = 2, children }: FormSectionProps) {
  return (
    <fieldset className="ui-form-section">
      <legend>{title}</legend>
      {description && <p className="ui-form-section-description">{description}</p>}
      <div className={`ui-form-section-grid ui-form-section-grid-${columns}`}>{children}</div>
    </fieldset>
  )
}
