/**
 * Sprint 6: an order may be created on a temporary customer ("VCB tijdelijk") before the real
 * customer is known. The customer master has no explicit placeholder flag, so the marker is a
 * naming convention on the customer name; it only drives a visual hint, never any logic.
 */
const PLACEHOLDER_PATTERN = /(^|[\s(\-_/])(tijdelijk(e)?|temp|tmp|placeholder|onbekend|unknown|inconnu|provisoire)($|[\s)\-_/])/i

export function isPlaceholderCustomerName(name: string | null | undefined): boolean {
  return !!name && PLACEHOLDER_PATTERN.test(name)
}
