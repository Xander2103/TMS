import type { TransportOrder } from '../types/transportOrder'
import { TransportStatusBadge } from './TransportStatusBadge'
import './TransportOrdersTable.css'

interface TransportOrdersTableProps {
  orders: TransportOrder[]
}

export function TransportOrdersTable({ orders }: TransportOrdersTableProps) {
  if (orders.length === 0) {
    return <p>Er zijn nog geen transportopdrachten.</p>
  }

  return (
    <table className="orders-table">
      <thead>
        <tr>
          <th scope="col">Referentie</th>
          <th scope="col">Klant</th>
          <th scope="col">Ophaling</th>
          <th scope="col">Levering</th>
          <th scope="col">Status</th>
        </tr>
      </thead>
      <tbody>
        {orders.map((order) => (
          <tr key={order.id}>
            <td>{order.reference}</td>
            <td>{order.customerName}</td>
            <td>{order.pickupAddress}</td>
            <td>{order.deliveryAddress}</td>
            <td>
              <TransportStatusBadge status={order.status} />
            </td>
          </tr>
        ))}
      </tbody>
    </table>
  )
}
