import { Link, useNavigate } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { UserForm } from '../components/UserForm'

export function NewUserPage() {
  const navigate = useNavigate()

  function handleSaved() {
    navigate('/users', { state: { created: true } })
  }

  return (
    <>
      <PageHeader title="Nieuwe gebruiker" />
      <UserForm
        onSaved={handleSaved}
        secondaryAction={
          <Link to="/users" className="secondary-link">
            Annuleren
          </Link>
        }
      />
    </>
  )
}
