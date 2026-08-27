import { Link } from 'react-router-dom'
import { useLocale } from '../../../i18n/localeContext'
import type { User } from '../types/user'
import './UsersTable.css'

interface UsersTableProps {
  users: User[]
}

export function UsersTable({ users }: UsersTableProps) {
  const { t } = useLocale()
  if (users.length === 0) {
    return <p>{t('usersRoles.users.table.empty')}</p>
  }

  return (
    <table className="users-table">
      <thead>
        <tr>
          <th scope="col">{t('usersRoles.users.table.name')}</th>
          <th scope="col">{t('usersRoles.users.table.email')}</th>
          <th scope="col">{t('usersRoles.users.table.roles')}</th>
          <th scope="col">{t('usersRoles.users.table.status')}</th>
        </tr>
      </thead>
      <tbody>
        {users.map((user) => (
          <tr key={user.id}>
            <td>
              <Link to={`/users/${user.id}`}>
                {user.firstName} {user.lastName}
              </Link>
            </td>
            <td>{user.email}</td>
            <td>
              {user.roles.length === 0 ? (
                <span className="muted-text">{t('usersRoles.users.table.noRoles')}</span>
              ) : (
                <span className="badge-list">
                  {user.roles.map((role) => (
                    <span key={role.id} className="badge">
                      {role.name}
                    </span>
                  ))}
                </span>
              )}
            </td>
            <td>
              <span className={user.isActive ? 'status-text status-active' : 'status-text status-inactive'}>
                {user.isActive ? t('usersRoles.users.table.active') : t('usersRoles.users.table.inactive')}
              </span>
              {user.isBlocked && <span className="status-text status-blocked">{t('usersRoles.users.table.blocked')}</span>}
            </td>
          </tr>
        ))}
      </tbody>
    </table>
  )
}
