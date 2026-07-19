import { useEffect, useRef, useState, type FormEvent } from 'react'
import { ApiError } from '../../../api/apiClient'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
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

function outcomeLabel(outcome: string): string {
  return PACKAGE_SCAN_OUTCOME_LABELS[outcome as PackageScanOutcome] ?? outcome
}

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
        if (mounted) setLoadError('De scanstatus kon niet worden geladen.')
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
  }, [tripId, stopId])

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
      showError('De hoeveelheid moet groter dan nul zijn.')
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
      showSuccess(`Scan van ${code} in wachtrij; wordt verstuurd zodra er weer verbinding is.`)
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
        showSuccess(`Geen verbinding; scan van ${code} staat in de wachtrij.`)
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
      showSuccess(`${missingFor.packageNumber} als vermist gemeld; planning is op de hoogte.`)
      setMissingFor(null)
      setMissingNote('')
      refreshPackages()
    } catch (err) {
      showError(err instanceof ApiError ? err.message : 'Het colli kon niet als vermist worden gemeld.')
    } finally {
      setBusy(false)
    }
  }

  async function handleCorrection(event: FormEvent) {
    event.preventDefault()
    if (!correctionFor) return
    if (!correctionReason.trim()) {
      showError('Een reden is verplicht bij een correctie.')
      return
    }
    const qty = Number(correctionQty)
    if (correctionQty === '' || qty < 0) {
      showError('Geef de juiste totale hoeveelheid op.')
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
      showError(err instanceof ApiError ? err.message : 'De correctie kon niet worden doorgevoerd.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal title={`Scannen — ${stopLabel}`} onClose={onClose} busy={busy}>
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
                  {feedback.package ? outcomeLabel(feedback.package.outcome) : SCAN_RESULT_LABELS[feedback.result]}
                </strong>{' '}
                — {feedback.message}
                {feedback.replayed && <span className="scan-feedback-count"> (al verwerkt)</span>}
                {feedback.cargoItemId && (
                  <span className="scan-feedback-count">
                    {' '}
                    ({feedback.acceptedQuantity}/{feedback.expectedQuantity})
                  </span>
                )}
              </span>
            </>
          ) : (
            <span>Scan of typ een barcode om te beginnen.</span>
          )}
        </div>

        {feedback?.package && feedback.package.children.length > 0 && (
          <section className="scan-group-results" aria-label="Groepsresultaat">
            <h3>Groep {feedback.package.packageNumber}</h3>
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
          <div className="scan-mode-toggle" role="radiogroup" aria-label="Scanmodus">
            {([
              [scanType, scanType === 'Load' ? 'Laden' : 'Lossen'],
              ['Return', 'Retour'],
              ['Depot', 'Depot'],
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
              Barcode
            </label>
            <input
              id="scan-barcode"
              ref={barcodeRef}
              className="scan-barcode-input"
              value={barcode}
              onChange={(e) => setBarcode(e.target.value)}
              placeholder="Scan of typ…"
              autoFocus
              autoComplete="off"
              disabled={busy}
            />
            <div className="scan-controls">
              <div className="scan-qty" role="group" aria-label="Hoeveelheid">
                <button type="button" onClick={() => setQuantity((q) => String(Math.max(1, Number(q || '1') - 1)))} disabled={busy} aria-label="Minder">
                  −
                </button>
                <input
                  aria-label="Hoeveelheid"
                  inputMode="decimal"
                  value={quantity}
                  onChange={(e) => setQuantity(e.target.value)}
                  disabled={busy}
                />
                <button type="button" onClick={() => setQuantity((q) => String(Number(q || '0') + 1))} disabled={busy} aria-label="Meer">
                  +
                </button>
              </div>
              <label className="scan-damaged">
                <input type="checkbox" checked={damaged} onChange={(e) => setDamaged(e.target.checked)} disabled={busy} />
                Schade
              </label>
              <Button type="submit" className="scan-submit" disabled={busy}>
                {busy ? 'Bezig…' : 'Registreren'}
              </Button>
            </div>
            {damaged && (
              <FormField label="Schadenotitie" htmlFor="scan-damage-note">
                <input
                  id="scan-damage-note"
                  value={damageNote}
                  onChange={(e) => setDamageNote(e.target.value)}
                  disabled={busy}
                  maxLength={500}
                  placeholder="bv. hoek ingedeukt"
                />
              </FormField>
            )}
            {activeType === 'Unload' && (
              <div className="scan-unload-outcomes" role="group" aria-label="Afleveruitkomst">
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
                  Geweigerd
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
                  Gedeeltelijk
                </label>
              </div>
            )}
            {(refused || partial) && (
              <FormField label={refused ? 'Reden van weigering' : 'Toelichting gedeeltelijke levering'} htmlFor="scan-outcome-note">
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
          <section className="scan-queue" aria-label="Wachtrij">
            <h3>
              Wachtrij ({queued.length})
              {!navigator.onLine && <span className="scan-queue-offline"> · offline</span>}
            </h3>
            <ul>
              {queued.map((item) => (
                <li key={item.clientEventId} className={item.state === 'failed' ? 'scan-queue-failed' : ''}>
                  <code>{item.input.barcode}</code>
                  <span className="scan-queue-state">
                    {item.state === 'failed'
                      ? `Afgekeurd: ${item.lastError ?? 'onbekende fout'}`
                      : `Wacht op verbinding (${item.attempts} pogingen)`}
                  </span>
                  {item.state === 'failed' && (
                    <>
                      <button type="button" className="scan-correct-link" onClick={() => scanQueue.retry(item.clientEventId)}>
                        Opnieuw
                      </button>
                      <button type="button" className="scan-correct-link" onClick={() => scanQueue.remove(item.clientEventId)}>
                        Verwijderen
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
            <h3>Colli op deze stop</h3>
            <ul className="scan-items">
              {packages.map((item) => (
                <li key={item.packageId}>
                  <div className="scan-item-row">
                    <span className="scan-item-desc">
                      {item.packageNumber}
                      {item.isGroup && ' (groep)'}
                      {!item.isMandatory && ' (optioneel)'}
                      <code className="scan-item-code">{item.description}</code>
                    </span>
                    <Badge tone={PACKAGE_STATUS_TONE[item.status]}>{PACKAGE_STATUS_LABELS[item.status]}</Badge>
                    {item.exceptionState === 'Open' && <Badge tone="danger">Melding open</Badge>}
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
                          Ontbreekt
                        </button>
                      )}
                  </div>
                  {missingFor?.packageId === item.packageId && (
                    <form className="scan-correction" onSubmit={handleMarkMissing}>
                      <p className="scan-missing-hint">
                        Meld {item.packageNumber} als vermist. Planning ontvangt direct een melding.
                      </p>
                      <FormField label="Toelichting" htmlFor={`missing-note-${item.packageId}`}>
                        <input
                          id={`missing-note-${item.packageId}`}
                          value={missingNote}
                          onChange={(e) => setMissingNote(e.target.value)}
                          disabled={busy}
                          maxLength={500}
                          placeholder="bv. niet gevonden in het magazijn"
                        />
                      </FormField>
                      <div className="scan-correction-actions">
                        <Button variant="secondary" onClick={() => setMissingFor(null)} disabled={busy}>
                          Annuleren
                        </Button>
                        <Button type="submit" disabled={busy}>
                          Vermist melden
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
              Verwacht ({summary.scanType === 'Load' ? 'laden' : 'lossen'})
              {summary.unexpectedScanCount > 0 && (
                <span className="scan-unexpected"> · {summary.unexpectedScanCount} onbekende scan(s)</span>
              )}
            </h3>
            {summary.items.length === 0 && <p className="scan-empty">Geen goederenlijnen op deze opdracht.</p>}
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
                      {SCAN_STATE_ICONS[item.state]} {SCAN_STATE_LABELS[item.state]}
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
                        Corrigeren
                      </button>
                    )}
                  </div>
                  {item.damagedQuantity > 0 && (
                    <div className="scan-item-damage">⚠ {item.damagedQuantity} met schade</div>
                  )}
                  {correctionFor === item.cargoItemId && (
                    <form className="scan-correction" onSubmit={handleCorrection}>
                      <FormField label="Juiste totale hoeveelheid" htmlFor={`corr-qty-${item.cargoItemId}`} required>
                        <input
                          id={`corr-qty-${item.cargoItemId}`}
                          inputMode="decimal"
                          value={correctionQty}
                          onChange={(e) => setCorrectionQty(e.target.value)}
                          disabled={busy}
                        />
                      </FormField>
                      <FormField label="Reden" htmlFor={`corr-reason-${item.cargoItemId}`} required>
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
                          Annuleren
                        </Button>
                        <Button type="submit" disabled={busy}>
                          Corrigeren
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
            <h3>Recente scans</h3>
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
                    {entry.packageOutcome ? outcomeLabel(entry.packageOutcome) : SCAN_RESULT_LABELS[entry.result]}
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
