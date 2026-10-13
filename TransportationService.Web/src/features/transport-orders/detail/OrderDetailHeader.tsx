import { Link, useNavigate } from 'react-router-dom'
import { FileText, Pencil, Trash2, X } from 'lucide-react'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { BackButton } from '../../../components/ui/BackButton'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import { useLocale } from '../../../i18n/localeContext'
import { formatDate } from '../../../utils/dates'
import { downloadOrderDocument } from '../api/transportDocumentsApi'
import { isPlaceholderCustomerName } from '../components/placeholderCustomer'
import { ORDER_STATUS_LABELS, ORDER_STATUS_TONE, ORDER_TRANSITION_LABELS, type TransportOrderStatus } from '../types'
import { useOrderDetail } from './orderDetailContext'

interface OrderDetailHeaderProps {
  onEdit: () => void
  onDelete: () => void
  onCancel: () => void
  onCorrectStatus: () => void
  onTransition: (target: TransportOrderStatus) => void
}

/**
 * Redesign 2026-09-12: compact identity block (number — customer, date/ref, status chips,
 * customer + invoicing entity) and grouped actions: workflow actions on the first row with the
 * destructive one set apart, document/template actions calmly on the second row.
 */
export function OrderDetailHeader({ onEdit, onDelete, onCancel, onCorrectStatus, onTransition }: OrderDetailHeaderProps) {
  const { t } = useLocale()
  const navigate = useNavigate()
  const { showError } = useToast()
  const { hasPermission, hasAnyPermission } = useAuth()
  const ws = useOrderDetail()
  const { order, busy, editing, entities, editable, deletable, priceDisplay } = ws
  const showPriceChip = Boolean(order.pricingSnapshot) || ws.totalPrice !== null

  return (
    <header className="tod-header">
      <Breadcrumbs items={[{ label: 'Transportopdrachten', to: '/transport-orders' }, { label: order.orderNumber }]} />
      <div className="tod-header-row">
        <div className="tod-header-main">
          <BackButton to="/transport-orders" label="Terug naar opdrachten" />
          <div className="tod-header-title">
            <h1>
              {order.orderNumber} — {order.customerName}
            </h1>
            <span className="tod-header-chips">
              <code>{order.orderNumber}</code>
              <Badge tone={ORDER_STATUS_TONE[order.status]}>{t(ORDER_STATUS_LABELS[order.status])}</Badge>
              {showPriceChip && <Badge tone={priceDisplay.tone}>{t(priceDisplay.labelKey)}</Badge>}
              {order.dossierId && order.dossierNumber && (
                <Link to={`/dossiers/${order.dossierId}`} className="ui-badge ui-badge-info" title="Open het dossier">
                  {order.dossierNumber}
                </Link>
              )}
            </span>
          </div>
          <p className="tod-header-sub">
            Opdracht van {formatDate(order.orderDate)}
            {order.customerReference ? ` · ref. ${order.customerReference}` : ''}
          </p>
          <p className="to-commercial-bar tod-commercial-bar" data-testid="order-commercial-bar">
            <span>
              {t('transportOrders.commercial.customerLabel')}:{' '}
              <Link to={`/customers/${order.customerId}`}>
                <strong>{order.customerName}</strong>
              </Link>
              {isPlaceholderCustomerName(order.customerName) && (
                <>
                  {' '}
                  <Badge tone="warning">{t('transportOrders.commercial.placeholderCustomer')}</Badge>
                </>
              )}
              {hasPermission('orders.edit') && (
                <button
                  type="button"
                  className="to-inline-action"
                  onClick={ws.openCustomerChange}
                  disabled={busy || editing}
                  aria-label={t('transportOrders.commercial.changeCustomerAria')}
                >
                  {t('transportOrders.commercial.change')}
                </button>
              )}
            </span>
            <span>
              {t('transportOrders.commercial.entityLabel')}:{' '}
              <strong>
                {order.legalEntityId
                  ? (entities.find((e) => e.id === order.legalEntityId)?.displayName ?? '…')
                  : t('transportOrders.commercial.customerDefault')}
              </strong>
              {hasPermission('orders.edit') && (
                <button
                  type="button"
                  className="to-inline-action"
                  onClick={ws.openEntityChange}
                  disabled={busy || editing}
                  aria-label={t('transportOrders.commercial.changeEntityAria')}
                >
                  {t('transportOrders.commercial.change')}
                </button>
              )}
            </span>
          </p>
        </div>

        <div className="tod-header-actions">
          <div className="tod-actions-row tod-actions-workflow">
            {editable && !editing && (
              <Button variant="secondary" onClick={onEdit} disabled={busy}>
                <Pencil size={15} aria-hidden /> Bewerken
              </Button>
            )}
            {hasAnyPermission(['orders.change_status', 'orders.manage']) &&
              order.allowedTransitions.map((target) => (
                <Button key={target} onClick={() => onTransition(target)} disabled={busy || editing}>
                  {t(ORDER_TRANSITION_LABELS[target])}
                </Button>
              ))}
            {order.canCancel && hasAnyPermission(['orders.cancel', 'orders.manage']) && (
              <Button variant="secondary" onClick={onCancel} disabled={busy || editing}>
                <X size={15} aria-hidden /> Annuleren
              </Button>
            )}
            {order.allowedCorrections.length > 0 && hasAnyPermission(['orders.correct_status', 'orders.manage']) && (
              <Button variant="secondary" onClick={onCorrectStatus} disabled={busy || editing}>
                Status corrigeren
              </Button>
            )}
            {deletable && (
              <Button variant="danger" className="tod-action-destructive" onClick={onDelete} disabled={busy || editing}>
                <Trash2 size={15} aria-hidden /> Verwijderen
              </Button>
            )}
          </div>
          <div className="tod-actions-row tod-actions-documents">
            {/* Wave 9: leveringsbon/CMR uit de bevroren ordergegevens. */}
            <Button
              variant="secondary"
              onClick={() => void downloadOrderDocument(order.id, 'cmr', order.orderNumber)
                .catch(() => showError('De CMR kon niet worden gegenereerd.'))}
              disabled={busy}
            >
              <FileText size={15} aria-hidden /> CMR
            </Button>
            <Button
              variant="secondary"
              onClick={() => void downloadOrderDocument(order.id, 'delivery-note', order.orderNumber)
                .catch(() => showError('De leveringsbon kon niet worden gegenereerd.'))}
              disabled={busy}
            >
              <FileText size={15} aria-hidden /> Leveringsbon
            </Button>
            {hasAnyPermission(['orders.create', 'orders.manage']) && (
              <Button
                variant="secondary"
                onClick={() => navigate(`/transport-orders/new?template=${order.id}`)}
                disabled={busy || editing}
              >
                Gebruik als sjabloon
              </Button>
            )}
          </div>
        </div>
      </div>
      {isPlaceholderCustomerName(order.customerName) && (
        <p className="customer-form-muted" role="note">
          {t('transportOrders.commercial.placeholderHint')}
        </p>
      )}
    </header>
  )
}
