import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { PageHeader } from '../../../components/layout/PageHeader'
import { UnitTypeMasterEditor } from '../../tarification/components/UnitTypeMasterEditor'

/** Stamgegevens → Eenheden: the single place where units and their physical defaults live. */
export function UnitTypesPage() {
  return (
    <div className="page">
      <Breadcrumbs items={[{ label: 'Stamgegevens' }, { label: 'Eenheden' }]} />
      <PageHeader
        title="Eenheden"
        subtitle="Globale eenheden met code, categorie, afmetingen en fysieke standaardwaarden. Klant-specifieke voorkeuren en tarieven staan op de klantenfiche."
      />
      <UnitTypeMasterEditor />
    </div>
  )
}
