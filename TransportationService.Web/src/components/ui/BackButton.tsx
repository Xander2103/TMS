import { Link } from 'react-router-dom'
import { useLocale } from '../../i18n/localeContext'
import './BackButton.css'

interface BackButtonProps {
  /** Destination (usually the list page). Deterministic on purpose — never history-dependent. */
  to: string
  label?: string
}

/** Clearly visible back action for detail/edit pages, so users never depend on browser back. */
export function BackButton({ to, label }: BackButtonProps) {
  const { t } = useLocale()
  return (
    <Link to={to} className="ui-back-button">
      <span aria-hidden="true">←</span> {label ?? t('ui.actions.back')}
    </Link>
  )
}
