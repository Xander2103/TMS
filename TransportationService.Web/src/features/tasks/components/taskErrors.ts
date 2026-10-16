import { describeApiError, localizeApiError } from '../../../api/problemDetails'

type Translate = (key: string, params?: Record<string, string | number>) => string

/**
 * HR acceptance #5: a task-assign refusal arrives as a field error (e.g. `assignedEmployeeIds`)
 * with a specific message. `localizeApiError` maps the generic validation code to "De invoer is
 * ongeldig", so the field message (when present) wins for the given fields.
 */
export function taskFieldError(error: unknown, ...fields: string[]): string | undefined {
  const { fieldErrors } = describeApiError(error, '')
  for (const field of fields) {
    const message = fieldErrors[field]?.[0]
    if (message) return message
  }
  return undefined
}

/** The most specific user-facing message: a field message for `fields`, else the localized error. */
export function taskErrorMessage(t: Translate, error: unknown, fallback: string, ...fields: string[]): string {
  return taskFieldError(error, ...fields) ?? localizeApiError(t, error, fallback)
}
