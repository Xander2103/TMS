import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { PageHeader } from '../../../components/layout/PageHeader'
import { useLocale } from '../../../i18n/localeContext'
import { ServiceOptionsEditor } from '../../tarification/components/ServiceOptionsEditor'

/** Stamgegevens → Services & toeslagen: the global defaults behind order services. */
export function ServiceOptionsPage() {
  const { t } = useLocale()
  return (
    <div className="page">
      <Breadcrumbs items={[{ label: t('navigation.menu.groups.masterData') }, { label: t('navigation.menu.servicesSurcharges') }]} />
      <PageHeader title={t('navigation.menu.servicesSurcharges')} subtitle={t('masterData.services.subtitle')} />
      <ServiceOptionsEditor />
    </div>
  )
}
