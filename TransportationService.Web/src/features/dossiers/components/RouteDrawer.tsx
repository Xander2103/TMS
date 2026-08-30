import { useState } from 'react'
import { ApiError } from '../../../api/apiClient'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { ValidationSummary } from '../../../components/ui/ValidationSummary'
import { useLocale } from '../../../i18n/localeContext'
import { updateTransportOrder } from '../../transport-orders/api/transportOrdersApi'
import type { TransportOrderDetail } from '../../transport-orders/types'
import { RouteSection } from '../../transport-orders/components/sections/RouteSection'
import { useOrderFormData } from '../../transport-orders/components/sections/useOrderFormData'
import { buildSubmitPayload } from '../../transport-orders/components/sections/orderFormPayload'
import {
  cargoFromOrder,
  emptyStop,
  fieldErrorMap,
  stopsFromOrder,
  validateOrderForm,
  type CargoFormRow,
  type StopFormRow,
} from '../../transport-orders/components/sections/orderFormState'
import { useStopMutation } from '../../transport-orders/components/sections/useStopMutation'
import { orderValuesFromDetail } from './orderDrawerState'
import { SectionDrawer } from './SectionDrawer'
import '../../transport-orders/components/transport-order-form.css'

interface RouteDrawerProps {
  order: TransportOrderDetail
  onClose: () => void
  /** Receives the fresh order detail after a successful save. */
  onSaved: (order: TransportOrderDetail) => void
}

/**
 * §11 route drawer: hosts the Phase-6 RouteSection against ONE linked order. Only the stops
 * slice is edited; the rest of the order round-trips verbatim (incl. the version token —
 * a stale save surfaces the 409 rebase banner instead of silently losing work).
 */
export function RouteDrawer({ order, onClose, onSaved }: RouteDrawerProps) {
  const { t } = useLocale()
  const [baseOrder, setBaseOrder] = useState(order)
  const [stops, setStops] = useState<StopFormRow[]>(() => stopsFromOrder(order))
  // The drawer edits stops only, but the goods lines address those stops BY POSITION, so it has
  // to carry them and renumber their links alongside every stop mutation (A1a). Without it a
  // reorder silently moved every line to another stop, and a removal sent an out-of-range index
  // back as a 400 about a goods field this drawer does not even render.
  const [cargoItems, setCargoItems] = useState<CargoFormRow[]>(() => cargoFromOrder(order))
  const [dirty, setDirty] = useState(false)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [conflict, setConflict] = useState<TransportOrderDetail | null>(null)
  const [refreshTarget, setRefreshTarget] = useState<string | null>(null)
  const { locationHours, serviceOptions } = useOrderFormData(baseOrder.customerId, stops)

  // A1a + N-1: one write path for the stop list — it renumbers the goods links in the same step
  // and guarantees that two mutations in a single tick both apply. See `useStopMutation`.
  const mutateStops = useStopMutation(stops, setStops, setCargoItems, () => setDirty(true))

  function setStop(key: string, patch: Partial<StopFormRow>) {
    mutateStops((rows) => rows.map((row) => (row.key === key ? { ...row, ...patch } : row)))
  }

  function moveStop(index: number, delta: number) {
    mutateStops((rows) => {
      const target = index + delta
      if (target < 0 || target >= rows.length) return rows
      const next = [...rows]
      ;[next[index], next[target]] = [next[target], next[index]]
      return next
    })
  }

  async function save() {
    const values = { ...orderValuesFromDetail(baseOrder, serviceOptions), stops, cargoItems }
    // Only this drawer's section blocks locally; other sections stay untouched anyway.
    const routeErrors = validateOrderForm(values).filter((e) => e.section === 'route')
    setErrors(fieldErrorMap(routeErrors))
    if (routeErrors.length > 0) {
      setError(t('dossiers.orderDrawer.checkStops'))
      return
    }
    setSaving(true)
    setError(null)
    try {
      const updated = await updateTransportOrder(baseOrder.id, buildSubmitPayload(values))
      onSaved(updated)
      onClose()
    } catch (err) {
      if (err instanceof ApiError && err.status === 409 && err.body && typeof err.body === 'object' && 'stops' in err.body) {
        setConflict(err.body as TransportOrderDetail)
      } else {
        setError(err instanceof Error ? err.message : t('dossiers.orderDrawer.routeSaveFailed'))
      }
    } finally {
      setSaving(false)
    }
  }

  function reloadFromConflict() {
    if (!conflict) return
    setBaseOrder(conflict)
    setStops(stopsFromOrder(conflict))
    setCargoItems(cargoFromOrder(conflict))
    setDirty(false)
    setConflict(null)
    setError(null)
  }

  return (
    <SectionDrawer
      title={t('dossiers.orderDrawer.routeTitle', { orderNumber: baseOrder.orderNumber })}
      dirty={dirty}
      busy={saving}
      onClose={onClose}
      onSave={() => void save()}
    >
      {conflict && (
        <div className="dossier-conflict-banner" role="alert">
          <span>{t('dossiers.orderDrawer.conflict')}</span>
          <Button variant="secondary" onClick={reloadFromConflict}>
            {t('dossiers.orderDrawer.reload')}
          </Button>
        </div>
      )}
      <ValidationSummary message={error} />
      <div className="tof">
        <RouteSection
          stops={stops}
          customerId={baseOrder.customerId}
          saving={saving}
          locationHours={locationHours}
          errors={errors}
          onAddStop={(stopType) => mutateStops((rows) => [...rows, emptyStop(stopType)])}
          setStop={setStop}
          moveStop={moveStop}
          onRemoveStop={(key) => mutateStops((rows) => rows.filter((row) => row.key !== key))}
          onRequestRefresh={setRefreshTarget}
        />
      </div>

      {refreshTarget && (
        <ConfirmDialog
          title={t('transportOrders.form.refreshTitle')}
          message={t('transportOrders.form.refreshMessage')}
          confirmLabel={t('transportOrders.form.refreshConfirm')}
          onConfirm={() => {
            setStop(refreshTarget, { refreshSnapshot: true })
            setRefreshTarget(null)
          }}
          onCancel={() => setRefreshTarget(null)}
        />
      )}
    </SectionDrawer>
  )
}
