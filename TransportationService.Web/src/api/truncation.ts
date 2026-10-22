/**
 * PERF-H3: list endpoints that cap their result (e.g. `GET /api/trips` at 500 rows) say so in
 * two response headers instead of silently returning a partial list:
 * `X-Result-Truncated: true` and `X-Result-Limit: <cap>`. Screens render a TruncationNotice
 * from this, so a planner never mistakes "the first 500" for "all".
 */
export interface TruncationInfo {
  truncated: boolean
  /** The server's cap when it reported one. */
  limit: number | null
}

export const NOT_TRUNCATED: TruncationInfo = { truncated: false, limit: null }

export function truncationFromHeaders(headers: Headers | undefined | null): TruncationInfo {
  if (!headers) return NOT_TRUNCATED
  const truncated = (headers.get('X-Result-Truncated') ?? '').trim().toLowerCase() === 'true'
  const rawLimit = headers.get('X-Result-Limit')
  const limit = rawLimit !== null && /^\d+$/.test(rawLimit.trim()) ? Number(rawLimit.trim()) : null
  return { truncated, limit }
}
