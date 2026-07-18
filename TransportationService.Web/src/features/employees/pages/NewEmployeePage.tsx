import { Link, useNavigate } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { EmployeeForm } from '../components/EmployeeForm'

export function NewEmployeePage() {
  const navigate = useNavigate()

  function handleSaved() {
    navigate('/employees', { state: { created: true } })
  }

  return (
    <>
      <PageHeader title="Nieuwe medewerker" />
      <EmployeeForm
        onSaved={handleSaved}
        secondaryAction={
          <Link to="/employees" className="secondary-link">
            Annuleren
          </Link>
        }
      />
    </>
  )
}
