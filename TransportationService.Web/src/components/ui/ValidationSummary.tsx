import { useEffect, useRef } from 'react'
import type { FieldErrors } from '../../api/problemDetails'
import { useLocale } from '../../i18n/localeContext'
import './ValidationSummary.css'

interface ValidationSummaryProps {
  /** Form-wide error message (e.g. the ProblemDetails `detail`). */
  message?: string | null
  /** Per-field messages, shown as a bullet list under the message. */
  fieldErrors?: FieldErrors
  /**
   * Field-path → user-facing label. Entries without a label show only their message,
   * so technical paths never reach the user.
   */
  fieldLabels?: Record<string, string>
  title?: string
  /**
   * When provided, entries render as links; clicking one calls back with the field path so
   * the form can navigate to the owning section (Wave 1 §12 targeted validation).
   */
  onSelect?: (path: string) => void
}

/**
 * Global validation summary shown at the top of a form after a failed submission.
 * Renders nothing while there are no errors; scrolls itself into view when they appear.
 */
export function ValidationSummary({
  message,
  fieldErrors,
  fieldLabels,
  title,
  onSelect,
}: ValidationSummaryProps) {
  const { t } = useLocale()
  const entries = Object.entries(fieldErrors ?? {}).flatMap(([path, messages]) =>
    messages.map((text) => ({ path, label: fieldLabels?.[path], text })),
  )
  const hasContent = Boolean(message) || entries.length > 0
  const ref = useRef<HTMLDivElement>(null)

  useEffect(() => {
    if (hasContent) {
      // Optional call: jsdom has no scrollIntoView implementation.
      ref.current?.scrollIntoView?.({ block: 'nearest' })
    }
  }, [hasContent, message, entries.length])

  if (!hasContent) return null

  // Skip the form-wide message when it merely repeats a single field message.
  const showMessage = Boolean(message) && !(entries.length === 1 && entries[0].text === message)

  return (
    <div className="ui-validation-summary" role="alert" ref={ref}>
      <p className="ui-validation-summary-title">{title ?? t('ui.validation.saveFailed')}</p>
      {showMessage && <p className="ui-validation-summary-message">{message}</p>}
      {entries.length > 0 && (
        <ul className="ui-validation-summary-list">
          {entries.map(({ path, label, text }, index) => {
            const content = label ? (
              <>
                <span className="ui-validation-summary-field">{label}:</span> <span>{text}</span>
              </>
            ) : (
              <span>{text}</span>
            )
            return (
              <li key={`${path}-${index}`}>
                {onSelect ? (
                  <button type="button" className="ui-validation-summary-link" onClick={() => onSelect(path)}>
                    {content}
                  </button>
                ) : (
                  content
                )}
              </li>
            )
          })}
        </ul>
      )}
    </div>
  )
}
