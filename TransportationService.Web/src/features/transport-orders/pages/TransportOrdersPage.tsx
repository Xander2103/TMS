import { Link } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { LoadingState } from '../../../components/feedback/LoadingState'
import { ErrorState } from '../../../components/feedback/ErrorState'
import { TransportOrdersTable } from '../components/TransportOrdersTable'
import { useTransportOrders } from '../hooks/useTransportOrders'

export function TransportOrdersPage() {
  const { orders, isLoading, error } = useTransportOrders()

  return (
    <>
      <PageHeader
        title="Transportopdrachten"
        action={
          <Link to="/transport-orders/new" className="primary-button">
            Nieuwe opdracht
          </Link>
        }
      />

      {isLoading && <LoadingState message="Transportopdrachten laden..." />}
      {error && <ErrorState message={error} />}
      {!isLoading && !error && <TransportOrdersTable orders={orders} />}
    </>
  )
}
