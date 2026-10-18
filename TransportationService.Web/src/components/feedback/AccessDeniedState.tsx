import type { ReactNode } from 'react'
import { Link } from 'react-router-dom'
import { PageHeader } from '../layout/PageHeader'
import { useLocale } from '../../i18n/localeContext'
import './Feedback.css'

interface AccessDeniedStateProps {
  /** Extra way out next to "Naar het startscherm" (e.g. the user's own portal profile). */
  action?: ReactNode
  /**
   * Inside a page that already has its own header (a table or panel whose load answered 403):
   * a section heading instead of a page header, so the page title and browser tab stay put.
   */
  inline?: boolean
}

/**
 * 403 as a page: the signed-in user lacks the rights for this screen. Says so plainly and offers
 * the start page (RootRedirect lands on the first screen the user may open) instead of rendering
 * a page shell full of "kon niet worden geladen" errors.
 */
export function AccessDeniedState({ action, inline = false }: AccessDeniedStateProps) {
  const { t } = useLocale()
  const actions = (
    <div className="route-error-actions">
      <Link className="ui-button ui-button-secondary" to="/">
        {t('ui.feedback.accessDeniedHome')}
      </Link>
      {action}
    </div>
  )
  if (inline) {
    return (
      <div className="error-state" role="alert">
        <h3 className="error-state-title">{t('ui.feedback.accessDeniedTitle')}</h3>
        <p className="placeholder-text">{t('ui.feedback.accessDeniedMessage')}</p>
        {actions}
      </div>
    )
  }
  return (
    <>
      <PageHeader title={t('ui.feedback.accessDeniedTitle')} />
      <p className="placeholder-text">{t('ui.feedback.accessDeniedMessage')}</p>
      {actions}
    </>
  )
}
