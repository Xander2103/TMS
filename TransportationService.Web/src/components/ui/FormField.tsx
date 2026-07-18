import type { ReactNode } from 'react'
import './FormField.css'

interface FormFieldProps {
  label: string
  htmlFor?: string
  error?: string
  hint?: string
  required?: boolean
  children: ReactNode
}

/** Label + control + inline validation error wrapper used by every form. */
export function FormField({ label, htmlFor, error, hint, required, children }: FormFieldProps) {
  return (
    <div className="ui-form-field">
      <label htmlFor={htmlFor}>
        {label}
        {required && <span className="ui-form-field-required" aria-hidden="true"> *</span>}
      </label>
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
