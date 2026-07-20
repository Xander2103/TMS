import { useNavigate } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { BackButton } from '../../../components/ui/BackButton'
import { useToast } from '../../../components/ui/toastContext'
import { createTransportOrder } from '../api/transportOrdersApi'
import { TransportOrderForm } from '../components/TransportOrderForm'

export function NewTransportOrderPage() {
  const navigate = useNavigate()
  const { showSuccess } = useToast()

  return (
    <div>
      <Breadcrumbs items={[{ label: 'Transportopdrachten', to: '/transport-orders' }, { label: 'Nieuwe opdracht' }]} />
      <BackButton to="/transport-orders" label="Terug naar opdrachten" />
      <PageHeader title="Nieuwe transportopdracht" subtitle="De opdracht start als concept en kan daarna worden bevestigd." />
      <TransportOrderForm
        submitLabel="Opdracht aanmaken"
        onCancel={() => navigate('/transport-orders')}
        onSubmit={async (input) => {
          const created = await createTransportOrder(input)
          showSuccess(`Opdracht ${created.orderNumber} aangemaakt.`)
          navigate(`/transport-orders/${created.id}`)
        }}
      />
    </div>
  )
}
