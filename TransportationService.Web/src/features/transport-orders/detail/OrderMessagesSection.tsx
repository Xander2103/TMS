import { MessageSquare } from 'lucide-react'
import { CustomerMessagesPanel } from '../../customers/components/CustomerMessagesPanel'
import { useOrderDetail } from './orderDetailContext'

/** Berichten workspace: the customer-portal conversation of this order, or a deliberate empty state. */
export function OrderMessagesSection() {
  const { order, canViewMessages } = useOrderDetail()
  return (
    <div className="tod-section" id="sectie-berichten">
      <section className="tod-panel">
        <h2>Berichten (klantportaal)</h2>
        {canViewMessages ? (
          <CustomerMessagesPanel customerId={order.customerId} orderId={order.id} />
        ) : (
          <p className="tod-empty-chat">
            <MessageSquare size={16} aria-hidden /> Je hebt geen toegang tot de portaalberichten van deze opdracht.
          </p>
        )}
      </section>
    </div>
  )
}
