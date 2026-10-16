import type { ReactNode } from 'react'
import { Outlet } from 'react-router-dom'
import { AccessDeniedState } from '../components/feedback/AccessDeniedState'
import { useAuth } from '../features/auth/authContextValue'

interface RequirePermissionProps {
  /** Any-of: the user needs at least one of these permission codes. */
  permissions: string[]
  /** Extra way out on the access-denied screen. */
  deniedAction?: ReactNode
  /** Guarded content; omitted = layout route rendering its nested routes via <Outlet />. */
  children?: ReactNode
}

/**
 * Route-level permission guard: renders the page only when the user holds one of `permissions`,
 * otherwise a "Geen toegang" screen. The API stays the real enforcement — this keeps users who
 * follow a link or type a URL from landing on a page shell full of 403 load errors.
 */
export function RequirePermission({ permissions, deniedAction, children }: RequirePermissionProps) {
  const { hasAnyPermission } = useAuth()
  if (!hasAnyPermission(permissions)) return <AccessDeniedState action={deniedAction} />
  return children ?? <Outlet />
}
