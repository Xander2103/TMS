import { useEffect, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { LoadingState } from '../../../components/feedback/LoadingState'
import { ErrorState } from '../../../components/feedback/ErrorState'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import { ApiError } from '../../../api/apiClient'
import {
  cancelTransportOrder,
  changeTransportOrderStatus,
  deleteTransportOrder,
  getTransportOrder,
  updateTransportOrder,
} from '../api/transportOrdersApi'
import { TransportOrderForm } from '../components/TransportOrderForm'
import {
  ORDER_STATUS_LABELS,
  ORDER_STATUS_TONE,
  ORDER_TRANSITION_LABELS,
  STOP_TYPE_LABELS,
  type TransportOrderDetail,
  type TransportOrderStatus,
} from '../types'
import './transport-orders.css'

function formatWindow(from: string | null, to: string | null): string {
  const fmt = (value: string) => value.slice(0, 16).replace('T', ' ')
  if (from && to) return `${fmt(from)} – ${fmt(to)}`
  if (from) return `vanaf ${fmt(from)}`
  if (to) return `tot ${fmt(to)}`
  return '—'
}

export function TransportOrderDetailPage() {
  const { id = '' } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const { showSuccess, showError } = useToast()
  const { hasPermission, hasAnyPermission } = useAuth()

  const [order, setOrder] = useState<TransportOrderDetail | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [editing, setEditing] = useState(false)
  const [busy, setBusy] = useState(false)
  const [confirmDelete, setConfirmDelete] = useState(false)
  const [cancelDialogOpen, setCancelDialogOpen] = useState(false)
  const [cancelReason, setCancelReason] = useState('')

  useEffect(() => {
    let mounted = true
    getTransportOrder(id)
      .then((data) => {
        if (!mounted) return
        setOrder(data)
        setLoadError(null)
      })
      .catch(() => {
        if (mounted) setLoadError('De transportopdracht kon niet worden geladen.')
      })
    return () => {
      mounted = false
    }
  }, [id])

  async function applyTransition(target: TransportOrderStatus) {
    setBusy(true)
    try {
      const updated = await changeTransportOrderStatus(id, target)
      setOrder(updated)
      showSuccess(`Status gewijzigd naar ${ORDER_STATUS_LABELS[target]}.`)
    } catch {
      showError('De status kon niet worden gewijzigd.')
    } finally {
      setBusy(false)
    }
  }

  async function handleCancel() {
    if (!cancelReason.trim()) {
      showError('Een reden is verplicht bij het annuleren.')
      return
    }
    setBusy(true)
    try {
      const updated = await cancelTransportOrder(id, cancelReason.trim())
      setOrder(updated)
      setCancelDialogOpen(false)
      setCancelReason('')
      showSuccess('Opdracht geannuleerd.')
    } catch (err) {
      showError(err instanceof ApiError ? err.message : 'De opdracht kon niet worden geannuleerd.')
    } finally {
      setBusy(false)
    }
  }

  async function handleDelete() {
    try {
      await deleteTransportOrder(id)
      showSuccess('Opdracht verwijderd.')
      navigate('/transport-orders')
    } catch {
      showError('De opdracht kon niet worden verwijderd.')
      setConfirmDelete(false)
    }
  }

  if (loadError) return <ErrorState message={loadError} />
  if (!order) return <LoadingState message="Opdracht laden..." />

  const editable =
    (order.status === 'Draft' || order.status === 'Confirmed') && hasPermission('orders.edit')
  const deletable =
    (order.status === 'Draft' || order.status === 'Cancelled') && hasPermission('orders.delete')

  return (
    <div>
      <Breadcrumbs items={[{ label: 'Transportopdrachten', to: '/transport-orders' }, { label: order.orderNumber }]} />
      <PageHeader
        title={`${order.orderNumber} — ${order.customerName}`}
        subtitle={`Opdracht van ${order.orderDate}${order.customerReference ? ` · ref. ${order.customerReference}` : ''}`}
        action={
          <span className="to-header-actions">
            <Badge tone={ORDER_STATUS_TONE[order.status]}>{ORDER_STATUS_LABELS[order.status]}</Badge>
            {hasAnyPermission(['orders.change_status', 'orders.manage']) &&
              order.allowedTransitions.map((target) => (
                <Button key={target} onClick={() => void applyTransition(target)} disabled={busy || editing}>
                  {ORDER_TRANSITION_LABELS[target]}
                </Button>
              ))}
            {order.canCancel && hasAnyPermission(['orders.cancel', 'orders.manage']) && (
              <Button variant="secondary" onClick={() => setCancelDialogOpen(true)} disabled={busy || editing}>
                Annuleren
              </Button>
            )}
          </span>
        }
      />

      {order.status === 'Cancelled' && order.cancellationReason && (
        <p className="to-cancel-reason" role="note">
          Geannuleerd: {order.cancellationReason}
        </p>
      )}

      {editing ? (
        <TransportOrderForm
          order={order}
          submitLabel="Wijzigingen opslaan"
          onCancel={() => setEditing(false)}
          onSubmit={async (input) => {
            const updated = await updateTransportOrder(id, input)
            setOrder(updated)
            setEditing(false)
            showSuccess('Opdracht bijgewerkt.')
          }}
        />
      ) : (
        <>
          <section className="to-section">
            <h2>Lading</h2>
            <dl className="to-facts">
              <div>
                <dt>Goederen</dt>
                <dd>{order.goodsDescription}</dd>
              </div>
              <div>
                <dt>Aantal</dt>
                <dd>{order.quantity !== null ? `${order.quantity} ${order.quantityUnit ?? ''}`.trim() : '—'}</dd>
              </div>
              <div>
                <dt>Gewicht</dt>
                <dd>{order.weightKg !== null ? `${order.weightKg.toLocaleString('nl-BE')} kg` : '—'}</dd>
              </div>
              <div>
                <dt>Volume</dt>
                <dd>{order.volumeM3 !== null ? `${order.volumeM3.toLocaleString('nl-BE')} m³` : '—'}</dd>
              </div>
              <div>
                <dt>Paletten</dt>
                <dd>{order.palletCount ?? '—'}</dd>
              </div>
              <div>
                <dt>Prijs</dt>
                <dd>
                  {order.agreedPrice !== null
                    ? order.agreedPrice.toLocaleString('nl-BE', { style: 'currency', currency: 'EUR' })
                    : '—'}
                </dd>
              </div>
              <div>
                <dt>Kenmerken</dt>
                <dd>
                  {order.adrRequired && <Badge tone="danger">ADR</Badge>}
                  {order.craneRequired && <Badge tone="info">Kraan</Badge>}
                  {!order.adrRequired && !order.craneRequired && '—'}
                </dd>
              </div>
            </dl>
            {order.notes && <p className="to-notes">{order.notes}</p>}
          </section>

          <section className="to-section">
            <h2>Stops</h2>
            <table className="to-stops-table">
              <thead>
                <tr>
                  <th>#</th>
                  <th>Type</th>
                  <th>Locatie</th>
                  <th>Adres</th>
                  <th>Tijdvenster</th>
                  <th>Referentie</th>
                </tr>
              </thead>
              <tbody>
                {order.stops.map((stop) => (
                  <tr key={stop.id}>
                    <td>{stop.sequence}</td>
                    <td>
                      <Badge tone={stop.stopType === 'Loading' ? 'info' : 'success'}>{STOP_TYPE_LABELS[stop.stopType]}</Badge>
                    </td>
                    <td title={stop.instructions ?? undefined}>
                      {stop.locationName}
                      {stop.locationCode && <span className="to-loc-code"> ({stop.locationCode})</span>}
                    </td>
                    <td>{[stop.address, [stop.postalCode, stop.city].filter(Boolean).join(' ')].filter(Boolean).join(', ') || '—'}</td>
                    <td>{formatWindow(stop.plannedFrom, stop.plannedTo)}</td>
                    <td>{stop.reference ?? '—'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </section>

          <div className="to-detail-actions">
            {editable && (
              <Button variant="secondary" onClick={() => setEditing(true)} disabled={busy}>
                Bewerken
              </Button>
            )}
            {deletable && (
              <Button variant="secondary" onClick={() => setConfirmDelete(true)} disabled={busy}>
                Verwijderen
              </Button>
            )}
          </div>
        </>
      )}

      {cancelDialogOpen && (
        <Modal
          title={`Opdracht ${order.orderNumber} annuleren`}
          onClose={() => setCancelDialogOpen(false)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setCancelDialogOpen(false)} disabled={busy}>
                Terug
              </Button>
              <Button variant="danger" onClick={() => void handleCancel()} disabled={busy}>
                {busy ? 'Annuleren…' : 'Opdracht annuleren'}
              </Button>
            </>
          }
        >
          <FormField label="Reden" htmlFor="cancel-reason" required hint="De reden wordt vastgelegd in de historiek.">
            <textarea
              id="cancel-reason"
              rows={3}
              value={cancelReason}
              onChange={(e) => setCancelReason(e.target.value)}
              maxLength={500}
              autoFocus
            />
          </FormField>
        </Modal>
      )}

      {confirmDelete && (
        <ConfirmDialog
          title="Opdracht verwijderen"
          message={`Weet je zeker dat je opdracht ${order.orderNumber} wilt verwijderen?`}
          confirmLabel="Verwijderen"
          destructive
          onConfirm={handleDelete}
          onCancel={() => setConfirmDelete(false)}
        />
      )}
    </div>
  )
}
