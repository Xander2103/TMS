import type { AddressDuplicateCandidate, AddressDuplicateCheckResult } from './customerAddressesApi'

/** Stable error code the server sends with a 409 when the address already exists (same front door). */
export const ADDRESS_DUPLICATE_ERROR_CODE = 'address_duplicate'

/**
 * The candidate list carried by a 409 `address_duplicate` from POST /api/locations, or null
 * for any other error. Callers show the candidates and resubmit with `overrideDuplicate: true`.
 * Duck-typed on the ApiError shape (code + body) to stay free of the apiClient import.
 */
export function extractAddressDuplicateConflict(error: unknown): AddressDuplicateCheckResult | null {
  if (!(error instanceof Error)) return null
  const code = 'code' in error && typeof error.code === 'string' ? error.code : undefined
  const body = 'body' in error ? (error.body as { hasExactMatch?: unknown; candidates?: unknown } | undefined) : undefined
  if (code !== ADDRESS_DUPLICATE_ERROR_CODE || !body || !Array.isArray(body.candidates)) return null
  return { hasExactMatch: body.hasExactMatch === true, candidates: body.candidates as AddressDuplicateCandidate[] }
}
