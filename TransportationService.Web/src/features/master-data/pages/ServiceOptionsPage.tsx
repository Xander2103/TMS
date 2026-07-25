import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { PageHeader } from '../../../components/layout/PageHeader'
import { ServiceOptionsEditor } from '../../tarification/components/ServiceOptionsEditor'

/** Stamgegevens → Services & toeslagen: the global defaults behind order services. */
export function ServiceOptionsPage() {
  return (
    <div className="page">
      <Breadcrumbs items={[{ label: 'Stamgegevens' }, { label: 'Services & toeslagen' }]} />
      <PageHeader
        title="Services & toeslagen"
        subtitle="Globale diensten en toeslagen met berekeningswijze en standaardprijs. Klant-specifieke overrides beheer je op de klantenfiche."
      />
      <ServiceOptionsEditor />
    </div>
  )
}
