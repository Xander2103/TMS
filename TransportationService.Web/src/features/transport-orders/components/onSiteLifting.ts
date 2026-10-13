/**
 * On-site crane lifting orders carry a lifted load, not goods lines (master sprint 2026-09-21, D2).
 * Read structurally: `craneJobKind` lands with the crane work and is absent on older payloads →
 * normal goods behaviour.
 */
export function isOnSiteLiftingOrder(order: object | null | undefined): boolean {
  return order != null && (order as { craneJobKind?: unknown }).craneJobKind === 'OnSiteLifting'
}
