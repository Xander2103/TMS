import type { ReactNode } from 'react'
import './FormField.css'

interface FormFieldProps {
  label: string
  htmlFor?: string
  error?: string
  hint?: string
  required?: boolean
  className?: string
  /**
   * Extra content rendered next to the label (e.g. an `InfoTip`). Kept OUTSIDE the `<label>`
   * element so it never becomes part of the control's accessible name.
   */
  labelExtra?: ReactNode
  children: ReactNode
}

/** Label + control + inline validation error wrapper used by every form. */
export function FormField({ label, htmlFor, error, hint, required, className, labelExtra, children }: FormFieldProps) {
  const labelElement = (
    <label htmlFor={htmlFor}>
      {label}
      {required && <span className="ui-form-field-required" aria-hidden="true"> *</span>}
    </label>
  )
  return (
    <div className={['ui-form-field', className].filter(Boolean).join(' ')}>
      {labelExtra ? (
        <div className="ui-form-field-label-row">
          {labelElement}
          {labelExtra}
        </div>
      ) : (
        labelElement
      )}
      {children}
      {hint && !error && <p className="ui-form-field-hint">{hint}</p>}
      {error && (
        <p className="ui-form-field-error" role="alert">
          {error}
        </p>
      )}
    </div>
  )
}
