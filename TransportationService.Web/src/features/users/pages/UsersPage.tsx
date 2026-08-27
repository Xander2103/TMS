import { Link } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { LoadingState } from '../../../components/feedback/LoadingState'
import { ErrorState } from '../../../components/feedback/ErrorState'
import { useAuth } from '../../auth/authContextValue'
import { useLocale } from '../../../i18n/localeContext'
import { UsersTable } from '../components/UsersTable'
import { useUsers } from '../hooks/useUsers'

export function UsersPage() {
  const { t } = useLocale()
  const { users, isLoading, error } = useUsers()
  const { hasPermission } = useAuth()

  return (
    <>
      <PageHeader
        title={t('usersRoles.users.page.title')}
        action={
          hasPermission('users.create') && (
            <Link to="/users/new" className="primary-button">
              {t('usersRoles.users.page.newUser')}
            </Link>
          )
        }
      />

      {isLoading && <LoadingState message={t('usersRoles.users.page.loading')} />}
      {error && <ErrorState message={error} />}
      {!isLoading && !error && <UsersTable users={users} />}
    </>
  )
}
