import { Link, useNavigate } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { useLocale } from '../../../i18n/localeContext'
import { UserForm } from '../components/UserForm'

export function NewUserPage() {
  const { t } = useLocale()
  const navigate = useNavigate()

  function handleSaved() {
    navigate('/users', { state: { created: true } })
  }

  return (
    <>
      <PageHeader title={t('usersRoles.users.page.newUser')} />
      <UserForm
        onSaved={handleSaved}
        secondaryAction={
          <Link to="/users" className="secondary-link">
            {t('ui.actions.cancel')}
          </Link>
        }
      />
    </>
  )
}
