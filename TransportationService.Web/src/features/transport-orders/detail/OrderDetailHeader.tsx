import { useEffect, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { FileText, Pencil } from 'lucide-react'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { BackButton } from '../../../components/ui/BackButton'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { ActionMenu, type ActionMenuItem } from '../../../components/ui/ActionMenu'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import { useLocale } from '../../../i18n/localeContext'
import { formatDate } from '../../../utils/dates'
import { downloadOrderDocument } from '../api/transportDocumentsApi'
import {
  downloadIssuedTransportDocumentPdf,
  listIssuedTransportDocuments,
  type IssuedTransportDocument,
  type IssuedTransportDocumentKind,
} from '../api/issuedDocumentsApi'
import { isPlaceholderCustomerName } from '../components/placeholderCustomer'
import { ORDER_STATUS_LABELS, ORDER_STATUS_TONE, ORDER_TRANSITION_LABELS, type TransportOrderStatus } from '../types'
import { useOrderDetail } from './orderDetailContext'

/** Tooltip of a document button that has no issued document yet. */
const CONCEPT_TITLE =
  'Voorbeeld uit de huidige ordergegevens, gemarkeerd als CONCEPT — geen officieel document. Geef het document uit via Documenten.'

/** Lifecycle steps that move an order forward, in order; the first allowed one is THE primary action. */
const ORDER_FORWARD_STEPS: TransportOrderStatus[] = ['Submitted', 'Confirmed', 'Planned', 'InProgress', 'Completed', 'Invoiced']

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

  // MD-B1 follow-up: the CMR / LB action opens THE official document when one is issued; without
  // one it renders a stamped CONCEPT from the live order data — and the button says which.
  const [issuedDocuments, setIssuedDocuments] = useState<IssuedTransportDocument[]>([])
  useEffect(() => {
    let mounted = true
    listIssuedTransportDocuments(order.id)
      .then((documents) => {
        if (mounted) setIssuedDocuments(documents)
      })
      .catch(() => {
        /* without the list the buttons fall back to the (stamped) concept */
      })
    return () => {
      mounted = false
    }
  }, [order.id])
  const activeIssued = (kind: IssuedTransportDocumentKind) =>
    issuedDocuments.find((doc) => doc.kind === kind && doc.isActive !== false) ?? null
  const issuedCmr = activeIssued('Cmr')
  const issuedDeliveryNote = activeIssued('DeliveryNote')

  const canTransition = hasAnyPermission(['orders.change_status', 'orders.manage'])
  const forwardTransitions = canTransition
    ? ORDER_FORWARD_STEPS.filter((step) => order.allowedTransitions.includes(step))
    : []
  const primaryTransition = forwardTransitions[0] ?? null
  const secondaryTransitions = forwardTransitions.slice(1)
  const menuItems: ActionMenuItem[] = []
  if (canTransition && order.allowedTransitions.includes('Draft')) {
    menuItems.push({ key: 'draft', label: t(ORDER_TRANSITION_LABELS.Draft), onSelect: () => onTransition('Draft') })
  }
  if (order.allowedCorrections.length > 0 && hasAnyPermission(['orders.correct_status', 'orders.manage'])) {
    menuItems.push({ key: 'correct', label: 'Status corrigeren', onSelect: onCorrectStatus })
  }
  if (hasAnyPermission(['orders.create', 'orders.manage'])) {
    menuItems.push({
      key: 'template',
      label: 'Gebruik als sjabloon',
      onSelect: () => navigate(`/transport-orders/new?template=${order.id}`),
    })
  }
  if (order.canCancel && hasAnyPermission(['orders.cancel', 'orders.manage'])) {
    menuItems.push({ key: 'cancel', label: 'Annuleren', onSelect: onCancel, danger: true })
  }
  if (deletable) {
    menuItems.push({ key: 'delete', label: 'Verwijderen', onSelect: onDelete, danger: true })
  }

  return (
    <header className="tod-header">
      {/* B12: an order that belongs to a dossier lives INSIDE it — the path and the way back say so. */}
      {order.dossierId && order.dossierNumber ? (
        <Breadcrumbs
          items={[
            { label: t('navigation.menu.dossiers'), to: '/dossiers' },
            { label: order.dossierNumber, to: `/dossiers/${order.dossierId}` },
            { label: order.orderNumber },
          ]}
        />
      ) : (
        <Breadcrumbs items={[{ label: 'Transportopdrachten', to: '/transport-orders' }, { label: order.orderNumber }]} />
      )}
      <div className="tod-header-row">
        <div className="tod-header-main">
          {order.dossierId && order.dossierNumber ? (
            <BackButton to={`/dossiers/${order.dossierId}/activiteiten`} label={t('transportOrders.detail.backToDossier', { number: order.dossierNumber })} />
          ) : (
            <BackButton to="/transport-orders" label="Terug naar opdrachten" />
          )}
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
          {/* B04: one primary (the next lifecycle step), secondary edit/document actions, and a
              "Meer" menu for the backward, corrective, template and destructive actions. */}
          <div className="tod-actions-row tod-actions-workflow">
            {editable && !editing && (
              <Button variant="secondary" onClick={onEdit} disabled={busy}>
                <Pencil size={15} aria-hidden /> Bewerken
              </Button>
            )}
            {secondaryTransitions.map((target) => (
              <Button key={target} variant="secondary" onClick={() => onTransition(target)} disabled={busy || editing}>
                {t(ORDER_TRANSITION_LABELS[target])}
              </Button>
            ))}
            {primaryTransition && (
              <Button onClick={() => onTransition(primaryTransition)} disabled={busy || editing}>
                {t(ORDER_TRANSITION_LABELS[primaryTransition])}
              </Button>
            )}
            <ActionMenu items={menuItems} disabled={busy || editing} size="sm" />
          </div>
          <div className="tod-actions-row tod-actions-documents">
            {/* MD-B1: an issued CMR / leveringsbon opens the immutable, numbered document; otherwise a stamped concept. */}
            <Button
              variant="secondary"
              title={issuedCmr ? undefined : CONCEPT_TITLE}
              onClick={() =>
                void (issuedCmr
                  ? downloadIssuedTransportDocumentPdf(issuedCmr)
                  : downloadOrderDocument(order.id, 'cmr', order.orderNumber)
                ).catch(() => showError('De CMR kon niet worden gegenereerd.'))
              }
              disabled={busy}
            >
              <FileText size={15} aria-hidden /> {issuedCmr ? `CMR ${issuedCmr.documentNumber}` : 'CMR (concept)'}
            </Button>
            <Button
              variant="secondary"
              title={issuedDeliveryNote ? undefined : CONCEPT_TITLE}
              onClick={() =>
                void (issuedDeliveryNote
                  ? downloadIssuedTransportDocumentPdf(issuedDeliveryNote)
                  : downloadOrderDocument(order.id, 'delivery-note', order.orderNumber)
                ).catch(() => showError('De leveringsbon kon niet worden gegenereerd.'))
              }
              disabled={busy}
            >
              <FileText size={15} aria-hidden />{' '}
              {issuedDeliveryNote ? `Leveringsbon ${issuedDeliveryNote.documentNumber}` : 'Leveringsbon (concept)'}
            </Button>
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
