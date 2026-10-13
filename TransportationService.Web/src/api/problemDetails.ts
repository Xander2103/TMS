/**
 * Mapping of backend validation errors (RFC 7807 ProblemDetails with an optional
 * "errors" dictionary — emitted by both DomainValidationException and ASP.NET's
 * ValidationProblemDetails) onto frontend form fields.
 */

export type FieldErrors = Record<string, string[]>

/**
 * Normalises a backend field path to the frontend form-field convention:
 * camelCase segments with indexers preserved ("Stops[0].City" → "stops[0].city").
 * Leading "$" (System.Text.Json binding paths) and "request" (controller parameter
 * name) segments are dropped.
 */
export function normalizeFieldPath(path: string): string {
  return path
    .split('.')
    .filter((segment, index) => !(index === 0 && (segment === '$' || segment.toLowerCase() === 'request')))
    .map((segment) => (segment.length > 0 ? segment.charAt(0).toLowerCase() + segment.slice(1) : segment))
    .join('.')
}

/** Extracts the per-field errors dictionary from a parsed error body, or {} when absent. */
export function extractFieldErrors(body: unknown): FieldErrors {
  if (typeof body !== 'object' || body === null) return {}
  const errors = (body as { errors?: unknown }).errors
  if (typeof errors !== 'object' || errors === null || Array.isArray(errors)) return {}

  const result: FieldErrors = {}
  for (const [rawPath, value] of Object.entries(errors as Record<string, unknown>)) {
    const messages = Array.isArray(value)
      ? value.filter((entry): entry is string => typeof entry === 'string')
      : typeof value === 'string'
        ? [value]
        : []
    if (messages.length === 0) continue
    const path = normalizeFieldPath(rawPath)
    result[path] = [...(result[path] ?? []), ...messages]
  }
  return result
}

/** First message for any of the given field paths, for display next to the input. */
export function getFieldError(errors: FieldErrors | undefined, ...paths: string[]): string | undefined {
  if (!errors) return undefined
  for (const path of paths) {
    const messages = errors[path]
    if (messages && messages.length > 0) return messages[0]
  }
  return undefined
}

/**
 * Turns any thrown value into a user-facing message + field errors, without depending
 * on the ApiError class (duck-typed to avoid an import cycle with apiClient).
 * `code` is de stabiele machineleesbare foutcode wanneer de server die meestuurde.
 */
export function describeApiError(
  error: unknown,
  fallback: string,
): { message: string; fieldErrors: FieldErrors; code?: string } {
  if (error instanceof Error) {
    const fieldErrors =
      'fieldErrors' in error && typeof error.fieldErrors === 'object' && error.fieldErrors !== null
        ? (error.fieldErrors as FieldErrors)
        : {}
    const code = 'code' in error && typeof error.code === 'string' ? error.code : undefined
    return { message: error.message || fallback, fieldErrors, code }
  }
  return { message: fallback, fieldErrors: {} }
}

/**
 * i18n-foutresolutie (§24/§78): vertaal op de STABIELE code wanneer daar een vertaling
 * voor bestaat (`errors.codes.<code>`), anders de servermessage (vandaag Nederlands),
 * anders de vertaalde fallback. Logica hoort op `code`, nooit op de tekst.
 */
export function localizeApiError(
  t: (key: string, params?: Record<string, string | number>) => string,
  error: unknown,
  fallback: string,
): string {
  const { message, code } = describeApiError(error, fallback)
  if (code) {
    const key = `errors.codes.${code}`
    const translated = t(key)
    if (translated !== key) {
      return translated
    }
  }
  return message
}
