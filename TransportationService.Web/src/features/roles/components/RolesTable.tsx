import { Link } from 'react-router-dom'
import { useLocale } from '../../../i18n/localeContext'
import type { Role } from '../types/role'
import './RolesTable.css'

interface RolesTableProps {
  roles: Role[]
}

export function RolesTable({ roles }: RolesTableProps) {
  const { t } = useLocale()
  if (roles.length === 0) {
    return <p>{t('usersRoles.roles.table.empty')}</p>
  }

  return (
    <table className="roles-table">
      <thead>
        <tr>
          <th scope="col">{t('usersRoles.roles.table.name')}</th>
          <th scope="col">{t('usersRoles.roles.table.description')}</th>
          <th scope="col">{t('usersRoles.roles.table.status')}</th>
        </tr>
      </thead>
      <tbody>
        {roles.map((role) => (
          <tr key={role.id}>
            <td>
              <Link to={`/roles/${role.id}`}>{role.name}</Link>
              {role.isSystemRole && <span className="badge">{t('usersRoles.roles.table.systemRole')}</span>}
            </td>
            <td>{role.description ?? <span className="muted-text">{t('usersRoles.roles.table.noDescription')}</span>}</td>
            <td>
              <span className={role.isActive ? 'status-text status-active' : 'status-text status-inactive'}>
                {role.isActive ? t('usersRoles.roles.table.active') : t('usersRoles.roles.table.inactive')}
              </span>
            </td>
          </tr>
        ))}
      </tbody>
    </table>
  )
}
