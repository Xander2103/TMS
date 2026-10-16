import { Link } from 'react-router-dom'
import { Badge } from '../../../components/ui/Badge'
import { DataTable, type Column } from '../../../components/ui/DataTable'
import { useLocale } from '../../../i18n/localeContext'
import type { User } from '../types/user'
import './UsersTable.css'

interface UsersTableProps {
  users: User[]
}

/** B46: the same DataTable and status badges as every other list in the app. */
export function UsersTable({ users }: UsersTableProps) {
  const { t } = useLocale()
  const columns: Column<User>[] = [
    {
      key: 'name',
      header: t('usersRoles.users.table.name'),
      render: (user) => (
        <Link to={`/users/${user.id}`}>
          {user.firstName} {user.lastName}
        </Link>
      ),
    },
    { key: 'email', header: t('usersRoles.users.table.email'), render: (user) => user.email },
    {
      key: 'roles',
      header: t('usersRoles.users.table.roles'),
      render: (user) =>
        user.roles.length === 0 ? (
          <span className="muted-text">{t('usersRoles.users.table.noRoles')}</span>
        ) : (
          <span className="badge-list">
            {user.roles.map((role) => (
              <Badge key={role.id} tone="neutral">
                {role.name}
              </Badge>
            ))}
          </span>
        ),
    },
    {
      key: 'status',
      header: t('usersRoles.users.table.status'),
      width: '200px',
      render: (user) => (
        <span className="badge-list">
          {user.isActive ? (
            <Badge tone="success">{t('usersRoles.users.table.active')}</Badge>
          ) : (
            <Badge tone="neutral">{t('usersRoles.users.table.inactive')}</Badge>
          )}
          {user.isBlocked && <Badge tone="danger">{t('usersRoles.users.table.blocked')}</Badge>}
          {user.isActivated === false && (
            <Badge tone="warning">{t('usersRoles.users.activation.notActivated')}</Badge>
          )}
        </span>
      ),
    },
  ]

  return <DataTable columns={columns} rows={users} rowKey={(user) => user.id} emptyMessage={t('usersRoles.users.table.empty')} />
}
