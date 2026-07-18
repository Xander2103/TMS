import { Link } from 'react-router-dom'
import type { Role } from '../types/role'
import './RolesTable.css'

interface RolesTableProps {
  roles: Role[]
}

export function RolesTable({ roles }: RolesTableProps) {
  if (roles.length === 0) {
    return <p>Er zijn nog geen rollen.</p>
  }

  return (
    <table className="roles-table">
      <thead>
        <tr>
          <th scope="col">Naam</th>
          <th scope="col">Omschrijving</th>
          <th scope="col">Status</th>
        </tr>
      </thead>
      <tbody>
        {roles.map((role) => (
          <tr key={role.id}>
            <td>
              <Link to={`/roles/${role.id}`}>{role.name}</Link>
              {role.isSystemRole && <span className="badge">Systeemrol</span>}
            </td>
            <td>{role.description ?? <span className="muted-text">Geen omschrijving</span>}</td>
            <td>
              <span className={role.isActive ? 'status-text status-active' : 'status-text status-inactive'}>
                {role.isActive ? 'Actief' : 'Inactief'}
              </span>
            </td>
          </tr>
        ))}
      </tbody>
    </table>
  )
}
