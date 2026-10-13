import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { PageHeader } from '../../../components/layout/PageHeader'
import { useLocale } from '../../../i18n/localeContext'
import { UnitTypeMasterEditor } from '../../tarification/components/UnitTypeMasterEditor'

/** Stamgegevens → Eenheden: the single place where units and their physical defaults live. */
export function UnitTypesPage() {
  const { t } = useLocale()
  return (
    <div className="page">
      <Breadcrumbs items={[{ label: t('navigation.menu.groups.masterData') }, { label: t('navigation.menu.units') }]} />
      <PageHeader title={t('navigation.menu.units')} subtitle={t('masterData.units.subtitle')} />
      <UnitTypeMasterEditor />
    </div>
  )
}
