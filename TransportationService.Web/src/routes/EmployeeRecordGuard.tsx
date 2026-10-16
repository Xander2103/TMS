import type { ReactNode } from 'react'
import { Link, useParams } from 'react-router-dom'
import { useAuth } from '../features/auth/authContextValue'
import { useLocale } from '../i18n/localeContext'
import { RequirePermission } from './RequirePermission'
import { navPermissions } from './routePermissions'

/**
 * Employee detail (/employees/:id) behind the same rights as the employee list. A user without
 * them who opens their OWN record (a chauffeur following a link) also gets the way to their
 * portal profile, where they can see their own data.
 */
export function EmployeeRecordGuard({ children }: { children: ReactNode }) {
  const { id = '' } = useParams<{ id: string }>()
  const { user } = useAuth()
  const { t } = useLocale()
  const ownRecord = Boolean(user?.employeeId) && user?.employeeId === id
  return (
    <RequirePermission
      permissions={navPermissions('/employees')}
      deniedAction={ownRecord ? (
        <Link className="ui-button ui-button-ghost" to="/portal/profile">
          {t('ui.feedback.accessDeniedMyProfile')}
        </Link>
      ) : undefined}
    >
      {children}
    </RequirePermission>
  )
}
