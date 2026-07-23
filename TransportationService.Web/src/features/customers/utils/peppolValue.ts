/** Renders scheme + participant as the single combined UI value ("0208:0123456789"). */
export function combinePeppolValue(scheme: string, participantId: string): string {
  return scheme ? `${scheme}:${participantId}` : participantId
}

/**
 * Maps the single UI value onto the separately stored backend columns. A "NNNN:" prefix is
 * the scheme; anything else stays in the participant id verbatim (never invent values) so
 * validation can flag it instead of data being silently rewritten.
 */
export function parsePeppolValue(raw: string): { scheme: string; participantId: string } {
  const idx = raw.indexOf(':')
  if (idx < 0) return { scheme: '', participantId: raw }
  const prefix = raw.slice(0, idx)
  const rest = raw.slice(idx + 1)
  if (/^\d{4}$/.test(prefix) && !rest.includes(':')) return { scheme: prefix, participantId: rest }
  return { scheme: '', participantId: raw }
}

/** Client-side check of the combined format; null when the value is acceptable. */
export function peppolFormatError(raw: string): string | null {
  if (!raw.includes(':')) return null
  const idx = raw.indexOf(':')
  const prefix = raw.slice(0, idx)
  const rest = raw.slice(idx + 1)
  if (!/^\d{4}$/.test(prefix) || rest.includes(':')) {
    return 'Ongeldig Peppol-ID. Gebruik "schema:nummer", bv. 0208:0123456789.'
  }
  return null
}
