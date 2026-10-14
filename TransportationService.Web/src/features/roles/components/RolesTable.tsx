import { Link } from 'react-router-dom'
import { Badge } from '../../../components/ui/Badge'
import { DataTable, type Column } from '../../../components/ui/DataTable'
import { useLocale } from '../../../i18n/localeContext'
import type { Role } from '../types/role'
import './RolesTable.css'

interface RolesTableProps {
  roles: Role[]
}

/** B46: the same DataTable and status badges as every other list in the app. */
export function RolesTable({ roles }: RolesTableProps) {
  const { t } = useLocale()
  const columns: Column<Role>[] = [
    {
      key: 'name',
      header: t('usersRoles.roles.table.name'),
      render: (role) => (
        <span className="badge-list">
          <Link to={`/roles/${role.id}`}>{role.name}</Link>
          {role.isSystemRole && <Badge tone="info">{t('usersRoles.roles.table.systemRole')}</Badge>}
        </span>
      ),
    },
    {
      key: 'description',
      header: t('usersRoles.roles.table.description'),
      render: (role) => role.description ?? <span className="muted-text">{t('usersRoles.roles.table.noDescription')}</span>,
    },
    {
      key: 'status',
      header: t('usersRoles.roles.table.status'),
      width: '140px',
      render: (role) =>
        role.isActive ? (
          <Badge tone="success">{t('usersRoles.roles.table.active')}</Badge>
        ) : (
          <Badge tone="neutral">{t('usersRoles.roles.table.inactive')}</Badge>
        ),
    },
  ]

  return <DataTable columns={columns} rows={roles} rowKey={(role) => role.id} emptyMessage={t('usersRoles.roles.table.empty')} />
}
