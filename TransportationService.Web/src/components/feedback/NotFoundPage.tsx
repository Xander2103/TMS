import { useNavigate } from 'react-router-dom'
import { PageHeader } from '../layout/PageHeader'
import { Button } from '../ui/Button'
import { useLocale } from '../../i18n/localeContext'
import './Feedback.css'

/** 404: says what happened and offers a way out instead of a dead end. */
export function NotFoundPage() {
  const { t } = useLocale()
  const navigate = useNavigate()
  return (
    <>
      <PageHeader title={t('ui.nav.notFoundTitle')} />
      <p className="placeholder-text">{t('ui.nav.notFoundMessage')}</p>
      <div className="route-error-actions">
        <Button variant="secondary" onClick={() => navigate(-1)}>{t('ui.nav.notFoundBack')}</Button>
        <Button variant="ghost" onClick={() => navigate('/')}>{t('ui.nav.notFoundHome')}</Button>
      </div>
    </>
  )
}
