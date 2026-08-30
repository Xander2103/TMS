import { useEffect, useMemo, useState, type DragEvent, type FormEvent } from 'react'
import { ApiError } from '../../../api/apiClient'
import { localizeApiError } from '../../../api/problemDetails'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { Modal } from '../../../components/ui/Modal'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import { useLocale } from '../../../i18n/localeContext'
// A13: `arrivedAt` is a genuine instant (the server stamps UtcNow), so it keeps the tenant-zone
// formatter. The PLANNED pair is a stored wall clock and uses this file's own helpers instead —
// see `dockWallClockTime` for the reasoning and the Wave-2 migration note.
import { formatTime } from '../../../utils/dates'
import { ORDER_PRIORITY_LABELS } from '../../transport-orders/types'
import {
  changeDockAppointmentStatus, createDockAppointment, deleteDockAppointment, getDockBoard,
  getWarehouseDashboard, listWarehouses, updateDockAppointment,
} from '../api/warehousingApi'
import {
  DOCK_STATUS_LABELS, DOCK_STATUS_TONE, OPERATION_LABELS, dockBlockPosition,
  dockWallClockInput, dockWallClockTime,
  type DockAppointment, type DockAppointmentInput, type DockBoard, type DockConflict,
  type Warehouse, type WarehouseDashboard,
} from '../types'
import './warehousing.css'

const APPOINTMENT_MIME = 'application/x-ts-dock'

function todayIso(): string {
  return new Date().toISOString().slice(0, 10)
}

interface ConflictPrompt {
  message: string
  conflicts: DockConflict[]
  retry: (reason: string) => Promise<unknown>
}

/** The dock planning board: docks as rows, the day horizontally, appointments as blocks. */
export function DockPlanningPage() {
  const { t } = useLocale()
  const { showError, showSuccess } = useToast()
  const { hasPermission } = useAuth()
  const canSchedule = hasPermission('warehouse.schedule')
  const canOverride = hasPermission('warehouse.conflict_override')

  const [warehouses, setWarehouses] = useState<Warehouse[]>([])
  const [warehouseId, setWarehouseId] = useState<string>('')
  const [date, setDate] = useState(todayIso)
  const [board, setBoard] = useState<DockBoard | null>(null)
  const [dashboard, setDashboard] = useState<WarehouseDashboard | null>(null)
  const [reloadToken, setReloadToken] = useState(0)
  const [busy, setBusy] = useState(false)
  const [selected, setSelected] = useState<DockAppointment | null>(null)
  const [editing, setEditing] = useState<DockAppointmentInput & { id?: string } | null>(null)
  const [conflictPrompt, setConflictPrompt] = useState<ConflictPrompt | null>(null)
  const [overrideReason, setOverrideReason] = useState('')

  useEffect(() => {
    listWarehouses()
      .then((data) => {
        setWarehouses(data)
        setWarehouseId((current) => current || (data[0]?.id ?? ''))
      })
      .catch((error: unknown) => showError(localizeApiError(t, error, t('warehousing.warehouses.loadFailed'))))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  useEffect(() => {
    if (!warehouseId) return
    let cancelled = false
    getDockBoard(warehouseId, date)
      .then((data) => {
        if (!cancelled) setBoard(data)
      })
      .catch((error: unknown) => showError(localizeApiError(t, error, t('warehousing.dockPlanning.loadBoardFailed'))))
    getWarehouseDashboard(warehouseId, date)
      .then((data) => {
        if (!cancelled) setDashboard(data)
      })
      .catch(() => undefined)
    return () => {
      cancelled = true
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [warehouseId, date, reloadToken])

  const reload = () => setReloadToken((token) => token + 1)
  const appointmentsByDock = useMemo(() => {
    const map = new Map<string, DockAppointment[]>()
    for (const appointment of board?.appointments ?? []) {
      if (!appointment.dockId) continue
      const list = map.get(appointment.dockId) ?? []
      list.push(appointment)
      map.set(appointment.dockId, list)
    }
    return map
  }, [board])

  async function run(action: () => Promise<unknown>, success?: string, retry?: (reason: string) => Promise<unknown>) {
    setBusy(true)
    try {
      await action()
      if (success) showSuccess(success)
      setSelected(null)
      reload()
    } catch (error) {
      if (error instanceof ApiError && error.status === 409) {
        const body = (error.body ?? {}) as { message?: string; conflicts?: DockConflict[]; staleVersion?: boolean }
        if (body.staleVersion) {
          showError(t('warehousing.dockPlanning.staleVersion'))
          reload()
        } else if (body.conflicts && retry) {
          setOverrideReason('')
          setConflictPrompt({
            message: body.message ?? t('warehousing.dockPlanning.conflictFallback'),
            conflicts: body.conflicts,
            retry,
          })
        } else {
          showError(body.message ?? t('warehousing.dockPlanning.actionRefused'))
          reload()
        }
      } else {
        showError(localizeApiError(t, error, t('warehousing.dockPlanning.actionFailed')))
      }
    } finally {
      setBusy(false)
    }
  }

  function allowDrop(event: DragEvent) {
    if (event.dataTransfer.types.includes(APPOINTMENT_MIME)) {
      event.preventDefault()
    }
  }

  function handleDropOnDock(dockId: string | null, event: DragEvent) {
    event.preventDefault()
    const raw = event.dataTransfer.getData(APPOINTMENT_MIME)
    if (!raw) return
    const payload = JSON.parse(raw) as { id: string }
    const appointment = board?.appointments.find((a) => a.id === payload.id)
      ?? board?.queue.find((a) => a.id === payload.id)
    if (!appointment || appointment.dockId === dockId) return
    const input = toInput(appointment, { dockId })
    void run(
      () => updateDockAppointment(appointment.id, input),
      dockId ? t('warehousing.dockPlanning.movedToDock') : t('warehousing.dockPlanning.movedToQueue'),
      (reason) => updateDockAppointment(appointment.id, { ...input, override: true, overrideReason: reason }),
    )
  }

  const activeWarehouse = warehouses.find((w) => w.id === warehouseId)

  return (
    <div className="dp-page">
      <header className="dp-header">
        <h1>{t('warehousing.dockPlanning.title')}</h1>
        <div className="dp-controls">
          <select value={warehouseId} onChange={(event) => setWarehouseId(event.target.value)} aria-label={t('warehousing.dockPlanning.warehouseAria')}>
            {warehouses.map((warehouse) => (
              <option key={warehouse.id} value={warehouse.id}>{warehouse.name}</option>
            ))}
          </select>
          <input type="date" value={date} onChange={(event) => setDate(event.target.value || date)} aria-label={t('warehousing.dockPlanning.dateAria')} />
          {canSchedule && activeWarehouse && (
            <Button onClick={() => setEditing({
              warehouseId, dockId: null, operationType: 'Loading',
              plannedStart: `${date}T08:00`, plannedEnd: `${date}T09:00`,
            })}>
              {t('warehousing.dockPlanning.newAppointment')}
            </Button>
          )}
        </div>
      </header>

      {dashboard && (
        <div className="dp-kpis">
          <span>{t('warehousing.dockPlanning.kpiExpected')}: <strong>{dashboard.expectedToday}</strong></span>
          <span>{t('warehousing.dockPlanning.kpiQueue')}: <strong>{dashboard.waiting}</strong></span>
          <span>{t('warehousing.dockPlanning.kpiInProgress')}: <strong>{dashboard.inProgress}</strong></span>
          <span>{t('warehousing.dockPlanning.kpiCompleted')}: <strong>{dashboard.completed}</strong></span>
          <span className={dashboard.delayed > 0 ? 'dp-alert' : ''}>{t('warehousing.dockPlanning.kpiDelayed')}: <strong>{dashboard.delayed}</strong></span>
          <span className={dashboard.noShows > 0 ? 'dp-alert' : ''}>{t('warehousing.dockPlanning.kpiNoShow')}: <strong>{dashboard.noShows}</strong></span>
        </div>
      )}

      <div className="dp-layout">
        <section className="dp-board" aria-label={t('warehousing.dockPlanning.timelineAria')}>
          <div className="dp-board-scale">
            <span>{board?.opensAt?.slice(0, 5) ?? '06:00'}</span>
            <span>{board?.closesAt?.slice(0, 5) ?? '20:00'}</span>
          </div>
          {(board?.docks ?? []).map((dock) => (
            <div key={dock.id} className={`dp-row${dock.isActive ? '' : ' dp-row-inactive'}`}
                 onDragOver={allowDrop} onDrop={(event) => handleDropOnDock(dock.id, event)}>
              <div className="dp-row-label">
                <strong>{dock.code}</strong>
                <span className="dp-muted">
                  {dashboard?.utilization.find((u) => u.dockId === dock.id)?.utilizationPct ?? 0}%
                </span>
              </div>
              <div className="dp-row-track">
                {(appointmentsByDock.get(dock.id) ?? []).map((appointment) => {
                  const pos = dockBlockPosition(appointment.plannedStart, appointment.plannedEnd, board?.opensAt ?? null, board?.closesAt ?? null)
                  return (
                    <button
                      key={appointment.id}
                      type="button"
                      draggable={canSchedule && !['Completed', 'Cancelled', 'NoShow'].includes(appointment.status)}
                      onDragStart={(event) => {
                        event.dataTransfer.setData(APPOINTMENT_MIME, JSON.stringify({ id: appointment.id }))
                      }}
                      className={`dp-block dp-block-${appointment.status.toLowerCase()}`}
                      style={{ left: `${pos.leftPct}%`, width: `${pos.widthPct}%` }}
                      onClick={() => setSelected(appointment)}
                      aria-label={t('warehousing.dockPlanning.appointmentAria', {
                        reference: appointment.orderNumber ?? appointment.tripNumber ?? appointment.reference ?? '',
                      })}
                    >
                      <span className="dp-block-title">
                        {t(OPERATION_LABELS[appointment.operationType])} · {appointment.orderNumber ?? appointment.tripNumber ?? appointment.vehicleNumber ?? '—'}
                      </span>
                      {appointment.packageCount > 0 && (
                        <span className="dp-block-progress">{appointment.packagesHandled}/{appointment.packageCount}</span>
                      )}
                    </button>
                  )
                })}
              </div>
            </div>
          ))}
        </section>

        <aside className="dp-queue" aria-label={t('warehousing.dockPlanning.queueTitle')} onDragOver={allowDrop}
               onDrop={(event) => handleDropOnDock(null, event)}>
          <h2>{t('warehousing.dockPlanning.queueTitle')}</h2>
          {(board?.queue ?? []).map((appointment) => (
            <button key={appointment.id} type="button" className="dp-queue-item"
                    draggable={canSchedule}
                    onDragStart={(event) => {
                      event.dataTransfer.setData(APPOINTMENT_MIME, JSON.stringify({ id: appointment.id }))
                    }}
                    onClick={() => setSelected(appointment)}>
              <strong>{appointment.orderNumber ?? appointment.tripNumber ?? appointment.vehicleNumber ?? t('warehousing.dockPlanning.appointmentFallback')}</strong>
              <span className="dp-muted">
                {t(ORDER_PRIORITY_LABELS[appointment.priority])} ·{' '}
                {t('warehousing.dockPlanning.arrived', { time: appointment.arrivedAt ? formatTime(appointment.arrivedAt) : '—' })}
              </span>
            </button>
          ))}
          {(board?.queue.length ?? 0) === 0 && <p className="dp-muted">{t('warehousing.dockPlanning.queueEmpty')}</p>}
        </aside>
      </div>

      {selected && (
        <Modal
          title={t('warehousing.dockPlanning.detailTitle', { reference: selected.orderNumber ?? selected.tripNumber ?? '' })}
          onClose={() => setSelected(null)}
          busy={busy}
        >
          <div className="dp-detail">
            <p>
              <Badge tone={DOCK_STATUS_TONE[selected.status]}>{t(DOCK_STATUS_LABELS[selected.status])}</Badge>{' '}
              {t(OPERATION_LABELS[selected.operationType])} · {selected.dockCode ?? t('warehousing.dockPlanning.queueLower')} ·{' '}
              {dockWallClockTime(selected.plannedStart)}–{dockWallClockTime(selected.plannedEnd)}
            </p>
            {selected.customerName && <p>{selected.customerName}</p>}
            {selected.packageCount > 0 && (
              <p>
                {t('warehousing.dockPlanning.scanProgress', { handled: selected.packagesHandled, total: selected.packageCount })}
                {selected.hasOpenDiscrepancies && <Badge tone="danger"> {t('warehousing.dockPlanning.openDiscrepancy')}</Badge>}
              </p>
            )}
            {selected.remarks && <p className="dp-muted">{selected.remarks}</p>}
            {canSchedule && (
              <div className="dp-detail-actions">
                {selected.allowedTransitions.map((target) => (
                  <Button key={target} variant="secondary" disabled={busy}
                          onClick={() => void run(
                            () => changeDockAppointmentStatus(selected.id, target, selected.version),
                            t('warehousing.dockPlanning.statusChanged', { status: t(DOCK_STATUS_LABELS[target]) }),
                          )}>
                    {t(DOCK_STATUS_LABELS[target])}
                  </Button>
                ))}
                <Button variant="secondary" disabled={busy} onClick={() => {
                  setEditing({
                    id: selected.id, ...toInput(selected, {}),
                    plannedStart: dockWallClockInput(selected.plannedStart),
                    plannedEnd: dockWallClockInput(selected.plannedEnd),
                  })
                  setSelected(null)
                }}>
                  {t('warehousing.dockPlanning.edit')}
                </Button>
                {['Planned', 'Expected'].includes(selected.status) && (
                  <Button variant="secondary" disabled={busy}
                          onClick={() => void run(
                            () => deleteDockAppointment(selected.id, selected.version),
                            t('warehousing.dockPlanning.deleted'))}>
                    {t('warehousing.dockPlanning.delete')}
                  </Button>
                )}
              </div>
            )}
          </div>
        </Modal>
      )}

      {editing && (
        <Modal
          title={editing.id ? t('warehousing.dockPlanning.editTitle') : t('warehousing.dockPlanning.newAppointment')}
          onClose={() => setEditing(null)}
          busy={busy}
        >
          <form className="wh-form" onSubmit={(event: FormEvent) => {
            event.preventDefault()
            const { id, ...input } = editing
            void run(
              () => (id ? updateDockAppointment(id, input) : createDockAppointment(input)),
              t('warehousing.dockPlanning.saved'),
              (reason) => (id
                ? updateDockAppointment(id, { ...input, override: true, overrideReason: reason })
                : createDockAppointment({ ...input, override: true, overrideReason: reason })),
            ).then(() => setEditing(null))
          }}>
            <div className="wh-form-row">
              <label>
                {t('warehousing.dockPlanning.dock')}
                <select value={editing.dockId ?? ''} onChange={(event) => setEditing({ ...editing, dockId: event.target.value || null })}>
                  <option value="">{t('warehousing.dockPlanning.queueNoDock')}</option>
                  {(activeWarehouse?.docks ?? []).filter((d) => d.isActive).map((dock) => (
                    <option key={dock.id} value={dock.id}>{dock.code}</option>
                  ))}
                </select>
              </label>
              <label>
                {t('warehousing.dockPlanning.operationLabel')}
                <select value={editing.operationType}
                        onChange={(event) => setEditing({ ...editing, operationType: event.target.value as 'Loading' | 'Unloading' })}>
                  <option value="Loading">{t(OPERATION_LABELS.Loading)}</option>
                  <option value="Unloading">{t(OPERATION_LABELS.Unloading)}</option>
                </select>
              </label>
            </div>
            <div className="wh-form-row">
              <label>
                {t('warehousing.dockPlanning.start')}
                <input type="datetime-local" value={editing.plannedStart} required
                       onChange={(event) => setEditing({ ...editing, plannedStart: event.target.value })} />
              </label>
              <label>
                {t('warehousing.dockPlanning.end')}
                <input type="datetime-local" value={editing.plannedEnd} required
                       onChange={(event) => setEditing({ ...editing, plannedEnd: event.target.value })} />
              </label>
            </div>
            <label>
              {t('warehousing.dockPlanning.reference')}
              <input value={editing.reference ?? ''} maxLength={100}
                     onChange={(event) => setEditing({ ...editing, reference: event.target.value || null })} />
            </label>
            <label>
              {t('warehousing.dockPlanning.remarks')}
              <textarea rows={2} value={editing.remarks ?? ''}
                        onChange={(event) => setEditing({ ...editing, remarks: event.target.value || null })} />
            </label>
            <div className="wh-form-actions">
              <Button variant="secondary" type="button" onClick={() => setEditing(null)} disabled={busy}>
                {t('warehousing.dockPlanning.cancel')}
              </Button>
              <Button type="submit" disabled={busy}>{t('warehousing.dockPlanning.save')}</Button>
            </div>
          </form>
        </Modal>
      )}

      {conflictPrompt && (
        <Modal title={t('warehousing.dockPlanning.conflictsTitle')} onClose={() => setConflictPrompt(null)} busy={busy}>
          <p>{conflictPrompt.message}</p>
          <ul className="dp-conflicts">
            {conflictPrompt.conflicts.map((conflict, index) => (
              <li key={index}>
                <Badge tone={conflict.severity === 'Blocking' ? 'danger' : conflict.severity === 'Warning' ? 'warning' : 'info'}>
                  {conflict.severity === 'Blocking'
                    ? t('warehousing.dockPlanning.severityBlocking')
                    : conflict.severity === 'Warning'
                      ? t('warehousing.dockPlanning.severityWarning')
                      : t('warehousing.dockPlanning.severityInfo')}
                </Badge>
                <span>{conflict.description}</span>
              </li>
            ))}
          </ul>
          {canOverride && conflictPrompt.conflicts.filter((c) => c.severity === 'Blocking').every((c) => c.overrideAllowed) && (
            <>
              <label className="dp-override-label" htmlFor="dp-override-reason">{t('warehousing.dockPlanning.overrideReason')}</label>
              <textarea id="dp-override-reason" rows={2} value={overrideReason}
                        onChange={(event) => setOverrideReason(event.target.value)} />
            </>
          )}
          <div className="wh-form-actions">
            <Button variant="secondary" onClick={() => setConflictPrompt(null)} disabled={busy}>
              {t('warehousing.dockPlanning.close')}
            </Button>
            {canOverride && conflictPrompt.conflicts.filter((c) => c.severity === 'Blocking').every((c) => c.overrideAllowed) && (
              <Button disabled={busy || overrideReason.trim().length === 0} onClick={() => {
                const prompt = conflictPrompt
                setConflictPrompt(null)
                void run(() => prompt.retry(overrideReason.trim()), t('warehousing.dockPlanning.overridden'))
              }}>
                {t('warehousing.dockPlanning.overrideConfirm')}
              </Button>
            )}
          </div>
        </Modal>
      )}
    </div>
  )
}

function toInput(appointment: DockAppointment, changes: Partial<DockAppointmentInput>): DockAppointmentInput {
  return {
    warehouseId: appointment.warehouseId,
    dockId: appointment.dockId,
    operationType: appointment.operationType,
    plannedStart: appointment.plannedStart,
    plannedEnd: appointment.plannedEnd,
    tripId: appointment.tripId,
    transportOrderId: appointment.transportOrderId,
    vehicleId: appointment.vehicleId,
    trailerId: appointment.trailerId,
    driverId: appointment.driverId,
    priority: appointment.priority,
    reference: appointment.reference,
    remarks: appointment.remarks,
    version: appointment.version,
    ...changes,
  }
}
