/**
 * Master-data remediation 2026-10-18: the two 409 shapes the Core integrity layer returns.
 * Branch on the stable `code`, never on the Dutch message text.
 */

/** Backend `ErrorCodes.RecordReferenced`: a delete refused because history references the record. */
export const RECORD_REFERENCED_CODE = 'common.record_referenced'

/** Backend `ErrorCodes.ConcurrencyConflict`: the record changed since it was loaded. */
export const CONCURRENCY_CONFLICT_CODE = 'common.concurrency_conflict'

export interface ReferenceCount {
  key: string
  count: number
  /** Ready-to-show Dutch label, e.g. "12 opdrachten". */
  label: string
}

export interface ReferenceConflict {
  message: string
  references: ReferenceCount[]
  canDeactivate: boolean
}

function bodyOf(error: unknown): Record<string, unknown> | null {
  if (typeof error !== 'object' || error === null) return null
  const body = (error as { body?: unknown }).body
  return typeof body === 'object' && body !== null ? (body as Record<string, unknown>) : null
}

function codeOf(error: unknown): string | undefined {
  if (typeof error !== 'object' || error === null) return undefined
  const code = (error as { code?: unknown }).code
  return typeof code === 'string' ? code : undefined
}

/** The refused-delete payload (message + counts), or null when the error is something else. */
export function getReferenceConflict(error: unknown): ReferenceConflict | null {
  if (codeOf(error) !== RECORD_REFERENCED_CODE) return null
  const body = bodyOf(error)
  const references = Array.isArray(body?.references)
    ? (body.references as unknown[]).filter(
        (r): r is ReferenceCount =>
          typeof r === 'object' && r !== null && typeof (r as ReferenceCount).label === 'string',
      )
    : []
  return {
    message: typeof body?.message === 'string' ? body.message : (error as Error).message,
    references,
    canDeactivate: body?.canDeactivate === true,
  }
}

/** True for "someone else saved this record in the meantime". */
export function isConcurrencyConflict(error: unknown): boolean {
  return codeOf(error) === CONCURRENCY_CONFLICT_CODE
}
