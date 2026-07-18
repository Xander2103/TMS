import { Link } from 'react-router-dom'
import type { EmployeeListItem } from '../types/employee'
import { EMPLOYEE_FUNCTION_LABELS, EMPLOYMENT_STATUS_LABELS } from '../types/employee'
import './EmployeesTable.css'

interface EmployeesTableProps {
  employees: EmployeeListItem[]
}

export function EmployeesTable({ employees }: EmployeesTableProps) {
  if (employees.length === 0) {
    return <p>Geen medewerkers gevonden.</p>
  }

  return (
    <table className="employees-table">
      <thead>
        <tr>
          <th scope="col">Nummer</th>
          <th scope="col">Naam</th>
          <th scope="col">Functie</th>
          <th scope="col">Status</th>
        </tr>
      </thead>
      <tbody>
        {employees.map((employee) => (
          <tr key={employee.id}>
            <td>{employee.employeeNumber}</td>
            <td>
              <Link to={`/employees/${employee.id}`}>
                {employee.firstName} {employee.lastName}
              </Link>
            </td>
            <td>{EMPLOYEE_FUNCTION_LABELS[employee.primaryFunction]}</td>
            <td>
              <span className={employee.isActive ? 'status-text status-active' : 'status-text status-inactive'}>
                {EMPLOYMENT_STATUS_LABELS[employee.employmentStatus]}
              </span>
            </td>
          </tr>
        ))}
      </tbody>
    </table>
  )
}
