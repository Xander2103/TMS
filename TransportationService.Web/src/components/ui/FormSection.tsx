import { useContext, useId, useState, type ReactNode } from 'react'
import { SectionedFormBodyContext } from './sectionedFormContext'
import './FormSection.css'

export type FormSectionVariant = 'framed' | 'flat'

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
  /**
   * `framed` draws the classic bordered box with a legend; `flat` renders a heading + content
   * without a frame. Inside a SectionedForm the default is `flat`: the active rail tab and
   * tab panel already frame the content, so a second box would only nest borders.
   */
  variant?: FormSectionVariant
  children: ReactNode
}

/**
 * Standard form section: heading + optional description + responsive field grid.
 * Full-width children (textareas, sub-editors) can opt out of the grid with
 * className="form-span-all".
 */
export function FormSection({
  title,
  description,
  columns = 2,
  collapsible = false,
  defaultOpen = false,
  variant,
  children,
}: FormSectionProps) {
  const [open, setOpen] = useState(defaultOpen)
  const bodyId = useId()
  const titleId = useId()
  // Inside a SectionedForm the user explicitly opened this section via its tab, so the
  // content must be immediately visible — never behind a second (accordion) click.
  const inSectionedForm = useContext(SectionedFormBodyContext)
  const effectiveCollapsible = collapsible && !inSectionedForm
  const expanded = !effectiveCollapsible || open
  const effectiveVariant: FormSectionVariant = variant ?? (inSectionedForm ? 'flat' : 'framed')

  const heading = effectiveCollapsible ? (
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
  )

  const body = expanded && (
    <div id={bodyId}>
      {description && <p className="ui-form-section-description">{description}</p>}
      <div className={`ui-form-section-grid ui-form-section-grid-${columns}`}>{children}</div>
    </div>
  )

  if (effectiveVariant === 'flat') {
    return (
      <section className="ui-form-section ui-form-section-flat" aria-labelledby={titleId}>
        <h3 id={titleId} className="ui-form-section-title">
          {heading}
        </h3>
        {body}
      </section>
    )
  }

  return (
    <fieldset className="ui-form-section ui-form-section-framed">
      <legend>{heading}</legend>
      {body}
    </fieldset>
  )
}
