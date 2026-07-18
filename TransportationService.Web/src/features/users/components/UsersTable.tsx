import { Link } from 'react-router-dom'
import type { User } from '../types/user'
import './UsersTable.css'

interface UsersTableProps {
  users: User[]
}

export function UsersTable({ users }: UsersTableProps) {
  if (users.length === 0) {
    return <p>Er zijn nog geen gebruikers.</p>
  }

  return (
    <table className="users-table">
      <thead>
        <tr>
          <th scope="col">Naam</th>
          <th scope="col">E-mail</th>
          <th scope="col">Rollen</th>
          <th scope="col">Status</th>
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
                <span className="muted-text">Geen rollen</span>
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
                {user.isActive ? 'Actief' : 'Inactief'}
              </span>
              {user.isBlocked && <span className="status-text status-blocked">Geblokkeerd</span>}
            </td>
          </tr>
        ))}
      </tbody>
    </table>
  )
}
