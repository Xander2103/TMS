import { useEffect, useRef, useState, type FormEvent } from 'react'
import { ApiError } from '../../../api/apiClient'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import { useLocale } from '../../../i18n/localeContext'
import { getTripPackageChecklist, markPackageMissing } from '../../packages/api/packagesApi'
import {
  PACKAGE_STATUS_LABELS,
  PACKAGE_STATUS_TONE,
  type TripPackageChecklistItem,
} from '../../packages/types'
import { correctScan, getStopScanSummary, listScans, submitScan } from '../api/scanningApi'
import { deviceScanSignal } from '../deviceFeedback'
import { scanQueue, type QueuedScan } from '../scanQueue'
import { CameraScanner } from './CameraScanner'
import {
  PACKAGE_SCAN_OUTCOME_LABELS,
  SCAN_RESULT_ICONS,
  SCAN_RESULT_LABELS,
  SCAN_STATE_ICONS,
  SCAN_STATE_LABELS,
  SCAN_STATE_TONE,
  type PackageScanOutcome,
  type ScanEventEntry,
  type ScanFeedback,
  type ScanType,
  type StopScanSummary,
} from '../types'
import './scanning.css'

const PRE_TRANSIT: string[] = ['Created', 'Labelled', 'AwaitingLoading']
const RETURN_PHASE: string[] = ['ReturnPending', 'Refused', 'DeliveryFailed', 'ReturnLoaded']

const RECENT_LIMIT = 8

interface ScanPanelProps {
  tripId: string
  stopId: string
  stopLabel: string
  scanType: ScanType
  canCorrect: boolean
  onClose: () => void
}

/**
 * Mobile-first scan surface for one stop: manual barcode entry (hardware scanners type +
 * enter into the same field), quantity, damage flag, instant classified feedback with
 * haptics, expected-vs-scanned summary and recent scans. The server classifies every scan.
 */
export function ScanPanel({ tripId, stopId, stopLabel, scanType, canCorrect, onClose }: ScanPanelProps) {
  const { showError, showSuccess } = useToast()
  const { hasPermission } = useAuth()
  const { t } = useLocale()

  /** Outcome via de vertaalsleutelmap; onbekende (nieuwe) outcomes tonen hun code. */
  function outcomeLabel(outcome: string): string {
    const key = PACKAGE_SCAN_OUTCOME_LABELS[outcome as PackageScanOutcome]
    return key ? t(key) : outcome
  }

  const [summary, setSummary] = useState<StopScanSummary | null>(null)
  const [recent, setRecent] = useState<ScanEventEntry[]>([])
  const [feedback, setFeedback] = useState<ScanFeedback | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [packages, setPackages] = useState<TripPackageChecklistItem[]>([])

  const [barcode, setBarcode] = useState('')
  const [quantity, setQuantity] = useState('1')
  const [damaged, setDamaged] = useState(false)
  const [damageNote, setDamageNote] = useState('')
  const [refused, setRefused] = useState(false)
  const [partial, setPartial] = useState(false)
  const [outcomeNote, setOutcomeNote] = useState('')
  const [busy, setBusy] = useState(false)

  const [correctionFor, setCorrectionFor] = useState<string | null>(null)
  const [correctionQty, setCorrectionQty] = useState('')
  const [correctionReason, setCorrectionReason] = useState('')

  const [missingFor, setMissingFor] = useState<TripPackageChecklistItem | null>(null)
  const [missingNote, setMissingNote] = useState('')

  // Return-phase packages unlock the retour/depot scan modes on top of the stop's own mode.
  const [activeType, setActiveType] = useState<ScanType>(scanType)

  const [queued, setQueued] = useState<QueuedScan[]>([])

  const barcodeRef = useRef<HTMLInputElement>(null)

  // Offline queue: subscribe for this stop's items and replay when the network returns.
  useEffect(() => {
    const unsubscribe = scanQueue.subscribe((items) =>
      setQueued(items.filter((i) => i.tripId === tripId && i.stopId === stopId)))
    const replayNow = () => {
      void scanQueue.replay(submitScan).then((outcome) => {
        if (outcome.succeeded > 0) {
          getStopScanSummary(tripId, stopId).then(setSummary).catch(() => {})
          refreshPackages()
        }
      })
    }
    window.addEventListener('online', replayNow)
    if (navigator.onLine) {
      replayNow()
    }
    return () => {
      unsubscribe()
      window.removeEventListener('online', replayNow)
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tripId, stopId])

  useEffect(() => {
    let mounted = true
    Promise.all([getStopScanSummary(tripId, stopId), listScans(tripId, stopId)])
      .then(([sum, events]) => {
        if (!mounted) return
        setSummary(sum)
        setRecent(events.slice(0, RECENT_LIMIT))
      })
      .catch(() => {
        if (mounted) setLoadError(t('scanning.panel.loadError'))
      })
    getTripPackageChecklist(tripId, stopId)
      .then((checklist) => {
        if (mounted) setPackages(checklist.stops[0]?.packages ?? [])
      })
      .catch(() => {
        // Package checklist is additive; the cargo summary above still works without it.
      })
    return () => {
      mounted = false
    }
  }, [tripId, stopId, t])

  const executable = hasPermission('scanning.execute')

  function refreshPackages() {
    getTripPackageChecklist(tripId, stopId)
      .then((checklist) => setPackages(checklist.stops[0]?.packages ?? []))
      .catch(() => {})
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    await doSubmit(barcode)
  }

  async function doSubmit(rawCode: string) {
    const code = rawCode.trim()
    if (!code) {
      barcodeRef.current?.focus()
      return
    }
    const qty = Number(quantity)
    if (!qty || qty <= 0) {
      showError(t('scanning.panel.qtyPositive'))
      return
    }
    const input = {
      scanType: activeType,
      barcode: code,
      quantity: qty,
      damaged,
      damageNote: damaged ? damageNote.trim() || null : null,
      deviceInfo: 'web-portal',
      clientEventId: crypto.randomUUID(),
      refused: activeType === 'Unload' ? refused : false,
      partial: activeType === 'Unload' ? partial : false,
      note: refused || partial ? outcomeNote.trim() || null : null,
    }

    // Known-offline: queue directly with honest feedback; replay is idempotent server-side.
    if (!navigator.onLine) {
      scanQueue.enqueue(tripId, stopId, input)
      deviceScanSignal.warning()
      showSuccess(t('scanning.panel.queuedOffline', { code }))
      setBarcode('')
      barcodeRef.current?.focus()
      return
    }

    setBusy(true)
    try {
      const result = await submitScan(tripId, stopId, input)
      setFeedback(result)
      setSummary(result.summary)
      if (result.package) {
        refreshPackages()
      }
      setRecent((prev) =>
        [
          {
            id: result.scanEventId,
            transportOrderStopId: stopId,
            cargoItemId: result.cargoItemId,
            cargoDescription: result.cargoDescription,
            scanType: activeType,
            result: result.result,
            barcode: code,
            quantity: qty,
            damaged,
            damageNote: damaged ? damageNote.trim() || null : null,
            correctionReason: null,
            deviceInfo: 'web-portal',
            userName: null,
            occurredAt: new Date().toISOString(),
            packageId: result.package?.packageId ?? null,
            packageNumber: result.package?.packageNumber ?? null,
            packageOutcome: result.package?.outcome ?? null,
          },
          ...prev,
        ].slice(0, RECENT_LIMIT),
      )
      if (result.level === 'Success') {
        deviceScanSignal.success()
      } else {
        deviceScanSignal.warning()
      }
      setBarcode('')
      setQuantity('1')
      setDamaged(false)
      setDamageNote('')
      setRefused(false)
      setPartial(false)
      setOutcomeNote('')
    } catch (err) {
      deviceScanSignal.warning()
      if (err instanceof ApiError) {
        showError(err.message)
      } else {
        // Network dropped mid-request: queue it — the ClientEventId makes the retry safe
        // even if the original request did reach the server.
        scanQueue.enqueue(tripId, stopId, input)
        showSuccess(t('scanning.panel.queuedDropped', { code }))
        setBarcode('')
      }
    } finally {
      setBusy(false)
      barcodeRef.current?.focus()
    }
  }

  async function handleMarkMissing(event: FormEvent) {
    event.preventDefault()
    if (!missingFor) return
    setBusy(true)
    try {
      await markPackageMissing(tripId, missingFor.packageId, stopId, missingNote.trim() || null)
      showSuccess(t('scanning.panel.markedMissing', { packageNumber: missingFor.packageNumber }))
      setMissingFor(null)
      setMissingNote('')
      refreshPackages()
    } catch (err) {
      showError(err instanceof ApiError ? err.message : t('scanning.panel.markMissingFailed'))
    } finally {
      setBusy(false)
    }
  }

  async function handleCorrection(event: FormEvent) {
    event.preventDefault()
    if (!correctionFor) return
    if (!correctionReason.trim()) {
      showError(t('scanning.panel.correctionReasonRequired'))
      return
    }
    const qty = Number(correctionQty)
    if (correctionQty === '' || qty < 0) {
      showError(t('scanning.panel.correctionQtyRequired'))
      return
    }
    setBusy(true)
    try {
      const result = await correctScan(tripId, stopId, {
        cargoItemId: correctionFor,
        scanType,
        quantity: qty,
        reason: correctionReason.trim(),
      })
      setFeedback(result)
      setSummary(result.summary)
      setCorrectionFor(null)
      setCorrectionQty('')
      setCorrectionReason('')
    } catch (err) {
      showError(err instanceof ApiError ? err.message : t('scanning.panel.correctionFailed'))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal title={t('scanning.panel.title', { stop: stopLabel })} onClose={onClose} busy={busy}>
      <div className="scan-panel">
        {loadError && (
          <p className="scan-error" role="alert">
            {loadError}
          </p>
        )}

        <div
          className={`scan-feedback ${feedback ? `scan-feedback-${feedback.level.toLowerCase()}` : ''}`}
          role="status"
          aria-live="assertive"
        >
          {feedback ? (
            <>
              <span className="scan-feedback-icon" aria-hidden="true">
                {SCAN_RESULT_ICONS[feedback.result]}
              </span>
              <span>
                <strong>
                  {feedback.package ? outcomeLabel(feedback.package.outcome) : t(SCAN_RESULT_LABELS[feedback.result])}
                </strong>{' '}
                — {feedback.message}
                {feedback.replayed && <span className="scan-feedback-count"> {t('scanning.panel.replayed')}</span>}
                {feedback.cargoItemId && (
                  <span className="scan-feedback-count">
                    {' '}
                    ({feedback.acceptedQuantity}/{feedback.expectedQuantity})
                  </span>
                )}
              </span>
            </>
          ) : (
            <span>{t('scanning.panel.startHint')}</span>
          )}
        </div>

        {feedback?.package && feedback.package.children.length > 0 && (
          <section className="scan-group-results" aria-label={t('scanning.panel.groupResultLabel')}>
            <h3>{t('scanning.panel.groupTitle', { number: feedback.package.packageNumber })}</h3>
            <ul>
              {feedback.package.children.map((child) => (
                <li key={child.packageId} className={child.succeeded ? 'scan-child-ok' : 'scan-child-failed'}>
                  <span aria-hidden="true">{child.succeeded ? '✓' : '✗'}</span>
                  <span className="scan-child-number">{child.packageNumber}</span>
                  <span className="scan-child-message">
                    {outcomeLabel(child.outcome)} — {child.message}
                  </span>
                </li>
              ))}
            </ul>
          </section>
        )}

        {executable && packages.some((p) => RETURN_PHASE.includes(p.status)) && (
          <div className="scan-mode-toggle" role="radiogroup" aria-label={t('scanning.panel.scanModeLabel')}>
            {([
              [scanType, scanType === 'Load' ? t('scanning.panel.modeLoad') : t('scanning.panel.modeUnload')],
              ['Return', t('scanning.panel.modeReturn')],
              ['Depot', t('scanning.panel.modeDepot')],
            ] as Array<[ScanType, string]>).map(([mode, label]) => (
              <label key={mode} className={`scan-mode ${activeType === mode ? 'scan-mode-active' : ''}`}>
                <input
                  type="radio"
                  name="scan-mode"
                  checked={activeType === mode}
                  onChange={() => setActiveType(mode)}
                  disabled={busy}
                />
                {label}
              </label>
            ))}
          </div>
        )}

        {executable && (
          <form className="scan-form" onSubmit={handleSubmit} noValidate>
            <CameraScanner disabled={busy} onDetected={(value) => void doSubmit(value)} />
            <label className="scan-barcode-label" htmlFor="scan-barcode">
              {t('scanning.panel.barcodeLabel')}
            </label>
            <input
              id="scan-barcode"
              ref={barcodeRef}
              className="scan-barcode-input"
              value={barcode}
              onChange={(e) => setBarcode(e.target.value)}
              placeholder={t('scanning.panel.barcodePlaceholder')}
              autoFocus
              autoComplete="off"
              disabled={busy}
            />
            <div className="scan-controls">
              <div className="scan-qty" role="group" aria-label={t('scanning.panel.quantityLabel')}>
                <button type="button" onClick={() => setQuantity((q) => String(Math.max(1, Number(q || '1') - 1)))} disabled={busy} aria-label={t('scanning.panel.less')}>
                  −
                </button>
                <input
                  aria-label={t('scanning.panel.quantityLabel')}
                  inputMode="decimal"
                  value={quantity}
                  onChange={(e) => setQuantity(e.target.value)}
                  disabled={busy}
                />
                <button type="button" onClick={() => setQuantity((q) => String(Number(q || '0') + 1))} disabled={busy} aria-label={t('scanning.panel.more')}>
                  +
                </button>
              </div>
              <label className="scan-damaged">
                <input type="checkbox" checked={damaged} onChange={(e) => setDamaged(e.target.checked)} disabled={busy} />
                {t('scanning.panel.damage')}
              </label>
              <Button type="submit" className="scan-submit" disabled={busy}>
                {busy ? t('scanning.panel.busy') : t('scanning.panel.submit')}
              </Button>
            </div>
            {damaged && (
              <FormField label={t('scanning.panel.damageNoteLabel')} htmlFor="scan-damage-note">
                <input
                  id="scan-damage-note"
                  value={damageNote}
                  onChange={(e) => setDamageNote(e.target.value)}
                  disabled={busy}
                  maxLength={500}
                  placeholder={t('scanning.panel.damageNotePlaceholder')}
                />
              </FormField>
            )}
            {activeType === 'Unload' && (
              <div className="scan-unload-outcomes" role="group" aria-label={t('scanning.panel.unloadOutcomeLabel')}>
                <label className="scan-damaged">
                  <input
                    type="checkbox"
                    checked={refused}
                    onChange={(e) => {
                      setRefused(e.target.checked)
                      if (e.target.checked) setPartial(false)
                    }}
                    disabled={busy}
                  />
                  {t('scanning.panel.refused')}
                </label>
                <label className="scan-damaged">
                  <input
                    type="checkbox"
                    checked={partial}
                    onChange={(e) => {
                      setPartial(e.target.checked)
                      if (e.target.checked) setRefused(false)
                    }}
                    disabled={busy}
                  />
                  {t('scanning.panel.partial')}
                </label>
              </div>
            )}
            {(refused || partial) && (
              <FormField label={refused ? t('scanning.panel.refusedReasonLabel') : t('scanning.panel.partialNoteLabel')} htmlFor="scan-outcome-note">
                <input
                  id="scan-outcome-note"
                  value={outcomeNote}
                  onChange={(e) => setOutcomeNote(e.target.value)}
                  disabled={busy}
                  maxLength={500}
                />
              </FormField>
            )}
          </form>
        )}

        {queued.length > 0 && (
          <section className="scan-queue" aria-label={t('scanning.panel.queueLabel')}>
            <h3>
              {t('scanning.panel.queueTitle', { count: queued.length })}
              {!navigator.onLine && <span className="scan-queue-offline"> · {t('scanning.panel.offline')}</span>}
            </h3>
            <ul>
              {queued.map((item) => (
                <li key={item.clientEventId} className={item.state === 'failed' ? 'scan-queue-failed' : ''}>
                  <code>{item.input.barcode}</code>
                  <span className="scan-queue-state">
                    {item.state === 'failed'
                      ? t('scanning.panel.queueFailed', { error: item.lastError ?? t('scanning.panel.unknownError') })
                      : t('scanning.panel.queuePending', { count: item.attempts })}
                  </span>
                  {item.state === 'failed' && (
                    <>
                      <button type="button" className="scan-correct-link" onClick={() => scanQueue.retry(item.clientEventId)}>
                        {t('scanning.panel.retry')}
                      </button>
                      <button type="button" className="scan-correct-link" onClick={() => scanQueue.remove(item.clientEventId)}>
                        {t('scanning.panel.remove')}
                      </button>
                    </>
                  )}
                </li>
              ))}
            </ul>
          </section>
        )}

        {packages.length > 0 && (
          <section className="scan-packages">
            <h3>{t('scanning.panel.packagesTitle')}</h3>
            <ul className="scan-items">
              {packages.map((item) => (
                <li key={item.packageId}>
                  <div className="scan-item-row">
                    <span className="scan-item-desc">
                      {item.packageNumber}
                      {item.isGroup && ` ${t('scanning.panel.groupSuffix')}`}
                      {!item.isMandatory && ` ${t('scanning.panel.optionalSuffix')}`}
                      <code className="scan-item-code">{item.description}</code>
                    </span>
                    <Badge tone={PACKAGE_STATUS_TONE[item.status]}>{t(PACKAGE_STATUS_LABELS[item.status])}</Badge>
                    {item.exceptionState === 'Open' && <Badge tone="danger">{t('scanning.panel.exceptionOpen')}</Badge>}
                    {executable &&
                      scanType === 'Load' &&
                      !item.isGroup &&
                      PRE_TRANSIT.includes(item.status) && (
                        <button
                          type="button"
                          className="scan-correct-link"
                          onClick={() => {
                            setMissingFor(item)
                            setMissingNote('')
                          }}
                          disabled={busy}
                        >
                          {t('scanning.panel.markMissing')}
                        </button>
                      )}
                  </div>
                  {missingFor?.packageId === item.packageId && (
                    <form className="scan-correction" onSubmit={handleMarkMissing}>
                      <p className="scan-missing-hint">
                        {t('scanning.panel.missingHint', { packageNumber: item.packageNumber })}
                      </p>
                      <FormField label={t('scanning.panel.noteLabel')} htmlFor={`missing-note-${item.packageId}`}>
                        <input
                          id={`missing-note-${item.packageId}`}
                          value={missingNote}
                          onChange={(e) => setMissingNote(e.target.value)}
                          disabled={busy}
                          maxLength={500}
                          placeholder={t('scanning.panel.missingNotePlaceholder')}
                        />
                      </FormField>
                      <div className="scan-correction-actions">
                        <Button variant="secondary" onClick={() => setMissingFor(null)} disabled={busy}>
                          {t('scanning.panel.cancel')}
                        </Button>
                        <Button type="submit" disabled={busy}>
                          {t('scanning.panel.reportMissing')}
                        </Button>
                      </div>
                    </form>
                  )}
                </li>
              ))}
            </ul>
          </section>
        )}

        {summary && (
          <section className="scan-summary">
            <h3>
              {summary.scanType === 'Load' ? t('scanning.panel.expectedLoad') : t('scanning.panel.expectedUnload')}
              {summary.unexpectedScanCount > 0 && (
                <span className="scan-unexpected"> · {t('scanning.panel.unexpectedScans', { count: summary.unexpectedScanCount })}</span>
              )}
            </h3>
            {summary.items.length === 0 && <p className="scan-empty">{t('scanning.panel.noCargoLines')}</p>}
            <ul className="scan-items">
              {summary.items.map((item) => (
                <li key={item.cargoItemId}>
                  <div className="scan-item-row">
                    <span className="scan-item-desc">
                      {item.description}
                      {item.barcode && <code className="scan-item-code">{item.barcode}</code>}
                    </span>
                    <span className="scan-item-count">
                      {item.scannedQuantity}/{item.expectedQuantity} {item.quantityUnit ?? ''}
                    </span>
                    <Badge tone={SCAN_STATE_TONE[item.state]}>
                      {SCAN_STATE_ICONS[item.state]} {t(SCAN_STATE_LABELS[item.state])}
                    </Badge>
                    {canCorrect && (
                      <button
                        type="button"
                        className="scan-correct-link"
                        onClick={() => {
                          setCorrectionFor(item.cargoItemId)
                          setCorrectionQty(String(item.scannedQuantity))
                          setCorrectionReason('')
                        }}
                        disabled={busy}
                      >
                        {t('scanning.panel.correct')}
                      </button>
                    )}
                  </div>
                  {item.damagedQuantity > 0 && (
                    <div className="scan-item-damage">⚠ {t('scanning.panel.withDamage', { count: item.damagedQuantity })}</div>
                  )}
                  {correctionFor === item.cargoItemId && (
                    <form className="scan-correction" onSubmit={handleCorrection}>
                      <FormField label={t('scanning.panel.correctionQtyLabel')} htmlFor={`corr-qty-${item.cargoItemId}`} required>
                        <input
                          id={`corr-qty-${item.cargoItemId}`}
                          inputMode="decimal"
                          value={correctionQty}
                          onChange={(e) => setCorrectionQty(e.target.value)}
                          disabled={busy}
                        />
                      </FormField>
                      <FormField label={t('scanning.panel.reasonLabel')} htmlFor={`corr-reason-${item.cargoItemId}`} required>
                        <input
                          id={`corr-reason-${item.cargoItemId}`}
                          value={correctionReason}
                          onChange={(e) => setCorrectionReason(e.target.value)}
                          disabled={busy}
                          maxLength={500}
                        />
                      </FormField>
                      <div className="scan-correction-actions">
                        <Button variant="secondary" onClick={() => setCorrectionFor(null)} disabled={busy}>
                          {t('scanning.panel.cancel')}
                        </Button>
                        <Button type="submit" disabled={busy}>
                          {t('scanning.panel.correct')}
                        </Button>
                      </div>
                    </form>
                  )}
                </li>
              ))}
            </ul>
          </section>
        )}

        {recent.length > 0 && (
          <section className="scan-recent">
            <h3>{t('scanning.panel.recentTitle')}</h3>
            <ul>
              {recent.map((entry) => (
                <li key={entry.id}>
                  <span className="scan-recent-icon" aria-hidden="true">
                    {SCAN_RESULT_ICONS[entry.result]}
                  </span>
                  <span className="scan-recent-time">{entry.occurredAt.slice(11, 16)}</span>
                  <span className="scan-recent-desc">
                    {entry.packageNumber ?? entry.cargoDescription ?? entry.barcode ?? '—'} ×{entry.quantity}
                  </span>
                  <span className="scan-recent-result">
                    {entry.packageOutcome ? outcomeLabel(entry.packageOutcome) : t(SCAN_RESULT_LABELS[entry.result])}
                  </span>
                </li>
              ))}
            </ul>
          </section>
        )}
      </div>
    </Modal>
  )
}
