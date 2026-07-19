import { useEffect, useRef, useState, type FormEvent } from 'react'
import { ApiError } from '../../../api/apiClient'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import { correctScan, getStopScanSummary, listScans, submitScan } from '../api/scanningApi'
import { deviceScanSignal } from '../deviceFeedback'
import {
  SCAN_RESULT_ICONS,
  SCAN_RESULT_LABELS,
  SCAN_STATE_ICONS,
  SCAN_STATE_LABELS,
  SCAN_STATE_TONE,
  type ScanEventEntry,
  type ScanFeedback,
  type ScanType,
  type StopScanSummary,
} from '../types'
import './scanning.css'

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
  const { showError } = useToast()
  const { hasPermission } = useAuth()

  const [summary, setSummary] = useState<StopScanSummary | null>(null)
  const [recent, setRecent] = useState<ScanEventEntry[]>([])
  const [feedback, setFeedback] = useState<ScanFeedback | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)

  const [barcode, setBarcode] = useState('')
  const [quantity, setQuantity] = useState('1')
  const [damaged, setDamaged] = useState(false)
  const [damageNote, setDamageNote] = useState('')
  const [busy, setBusy] = useState(false)

  const [correctionFor, setCorrectionFor] = useState<string | null>(null)
  const [correctionQty, setCorrectionQty] = useState('')
  const [correctionReason, setCorrectionReason] = useState('')

  const barcodeRef = useRef<HTMLInputElement>(null)

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
    return () => {
      mounted = false
    }
  }, [tripId, stopId])

  const executable = hasPermission('scanning.execute')

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    const code = barcode.trim()
    if (!code) {
      barcodeRef.current?.focus()
      return
    }
    const qty = Number(quantity)
    if (!qty || qty <= 0) {
      showError('De hoeveelheid moet groter dan nul zijn.')
      return
    }
    setBusy(true)
    try {
      const result = await submitScan(tripId, stopId, {
        scanType,
        barcode: code,
        quantity: qty,
        damaged,
        damageNote: damaged ? damageNote.trim() || null : null,
        deviceInfo: 'web-portal',
      })
      setFeedback(result)
      setSummary(result.summary)
      setRecent((prev) =>
        [
          {
            id: result.scanEventId,
            transportOrderStopId: stopId,
            cargoItemId: result.cargoItemId,
            cargoDescription: result.cargoDescription,
            scanType,
            result: result.result,
            barcode: code,
            quantity: qty,
            damaged,
            damageNote: damaged ? damageNote.trim() || null : null,
            correctionReason: null,
            deviceInfo: 'web-portal',
            userName: null,
            occurredAt: new Date().toISOString(),
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
    } catch (err) {
      deviceScanSignal.warning()
      showError(err instanceof ApiError ? err.message : 'De scan kon niet worden geregistreerd.')
    } finally {
      setBusy(false)
      barcodeRef.current?.focus()
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
                <strong>{SCAN_RESULT_LABELS[feedback.result]}</strong> — {feedback.message}
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

        {executable && (
          <form className="scan-form" onSubmit={handleSubmit} noValidate>
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
          </form>
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
                    {entry.cargoDescription ?? entry.barcode ?? '—'} ×{entry.quantity}
                  </span>
                  <span className="scan-recent-result">{SCAN_RESULT_LABELS[entry.result]}</span>
                </li>
              ))}
            </ul>
          </section>
        )}
      </div>
    </Modal>
  )
}
