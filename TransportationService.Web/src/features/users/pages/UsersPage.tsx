import { Link } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { LoadingState } from '../../../components/feedback/LoadingState'
import { ErrorState } from '../../../components/feedback/ErrorState'
import { UsersTable } from '../components/UsersTable'
import { useUsers } from '../hooks/useUsers'

export function UsersPage() {
  const { users, isLoading, error } = useUsers()

  return (
    <>
      <PageHeader
        title="Gebruikers"
        action={
          <Link to="/users/new" className="primary-button">
            Nieuwe gebruiker
          </Link>
        }
      />

      {isLoading && <LoadingState message="Gebruikers laden..." />}
      {error && <ErrorState message={error} />}
      {!isLoading && !error && <UsersTable users={users} />}
    </>
  )
}
