import { Link, useNavigate } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { TransportOrderForm } from '../components/TransportOrderForm'

export function NewTransportOrderPage() {
  const navigate = useNavigate()

  function handleCreated() {
    navigate('/transport-orders', { state: { created: true } })
  }

  return (
    <>
      <PageHeader title="Nieuwe opdracht" />
      <TransportOrderForm
        onCreated={handleCreated}
        secondaryAction={
          <Link to="/transport-orders" className="secondary-link">
            Annuleren
          </Link>
        }
      />
    </>
  )
}
