import type { ReactNode } from 'react'
import { Link } from 'react-router-dom'
import { PageHeader } from '../layout/PageHeader'
import { useLocale } from '../../i18n/localeContext'
import './Feedback.css'

interface AccessDeniedStateProps {
  /** Extra way out next to "Naar het startscherm" (e.g. the user's own portal profile). */
  action?: ReactNode
}

/**
 * 403 as a page: the signed-in user lacks the rights for this screen. Says so plainly and offers
 * the start page (RootRedirect lands on the first screen the user may open) instead of rendering
 * a page shell full of "kon niet worden geladen" errors.
 */
export function AccessDeniedState({ action }: AccessDeniedStateProps) {
  const { t } = useLocale()
  return (
    <>
      <PageHeader title={t('ui.feedback.accessDeniedTitle')} />
      <p className="placeholder-text">{t('ui.feedback.accessDeniedMessage')}</p>
      <div className="route-error-actions">
        <Link className="ui-button ui-button-secondary" to="/">
          {t('ui.feedback.accessDeniedHome')}
        </Link>
        {action}
      </div>
    </>
  )
}
