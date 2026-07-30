import type { CurrentUser } from '../features/auth/authTypes'

/** True for customer-portal accounts: a linked customer AND the base portal-view permission. */
export function isPortalUser(user: CurrentUser | null): boolean {
  return Boolean(user?.customerId) && (user?.permissions.includes('customer_portal.view') ?? false)
}
