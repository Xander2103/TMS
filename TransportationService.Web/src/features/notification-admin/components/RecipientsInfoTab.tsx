import { Link } from 'react-router-dom'
import { useLocale } from '../../../i18n/localeContext'
import { RECIPIENT_TYPE_LABELS } from '../types'

/**
 * Purely informational tab: explains how each recipient type resolves at send time, and points
 * to where per-customer communication routing actually lives. No backend of its own — the
 * routing logic is documented here, not duplicated.
 */
export function RecipientsInfoTab() {
  const { t } = useLocale()
  return (
    <div className="notification-admin-info">
      <h3>{t('notificationAdmin.recipientsInfo.heading')}</h3>
      <p>
        {t('notificationAdmin.recipientsInfo.intro1')} <strong>{t('notificationAdmin.recipientsInfo.introStrong')}</strong>{' '}
        {t('notificationAdmin.recipientsInfo.intro2')}
      </p>
      <dl className="notification-admin-recipient-glossary">
        <dt>{t(RECIPIENT_TYPE_LABELS.CustomerPrimaryContact)}</dt>
        <dd>{t('notificationAdmin.recipientsInfo.descPrimaryContact')}</dd>

        <dt>{t(RECIPIENT_TYPE_LABELS.CustomerCommunicationRule)}</dt>
        <dd>{t('notificationAdmin.recipientsInfo.descCommunicationRule')}</dd>

        <dt>{t(RECIPIENT_TYPE_LABELS.InternalPermission)}</dt>
        <dd>{t('notificationAdmin.recipientsInfo.descPermission')}</dd>

        <dt>{t(RECIPIENT_TYPE_LABELS.InternalRole)}</dt>
        <dd>{t('notificationAdmin.recipientsInfo.descRole')}</dd>

        <dt>{t(RECIPIENT_TYPE_LABELS.ExplicitEmail)}</dt>
        <dd>{t('notificationAdmin.recipientsInfo.descEmail')}</dd>

        <dt>{t(RECIPIENT_TYPE_LABELS.Driver)}</dt>
        <dd>{t('notificationAdmin.recipientsInfo.descDriver')}</dd>
      </dl>
      <p>
        {t('notificationAdmin.recipientsInfo.outro1')} <em>{t('notificationAdmin.recipientsInfo.outroEm')}</em>
        {t('notificationAdmin.recipientsInfo.outro2')}
      </p>
      <Link to="/customers" className="notification-admin-link-button">
        {t('notificationAdmin.recipientsInfo.toCustomers')}
      </Link>
    </div>
  )
}
