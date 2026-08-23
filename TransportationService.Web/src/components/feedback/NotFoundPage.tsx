import { PageHeader } from '../layout/PageHeader'
import { useLocale } from '../../i18n/localeContext'

export function NotFoundPage() {
  const { t } = useLocale()
  return (
    <>
      <PageHeader title={t('ui.nav.notFoundTitle')} />
      <p className="placeholder-text">{t('ui.nav.notFoundMessage')}</p>
    </>
  )
}
