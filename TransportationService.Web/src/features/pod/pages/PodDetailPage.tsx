import { useEffect, useState, type FormEvent } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { LoadingState } from '../../../components/feedback/LoadingState'
import { ErrorState } from '../../../components/feedback/ErrorState'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import { ApiError } from '../../../api/apiClient'
import { correctPod, fetchPodPhotoUrl, fetchPodSignatureUrl, getPod } from '../api/podApi'
import { SignaturePad } from '../components/SignaturePad'
import {
  POD_OUTCOME_ICONS,
  POD_OUTCOME_LABELS,
  POD_OUTCOME_TONE,
  POD_PHOTO_CATEGORY_LABELS,
  type PodDetail,
  type PodOutcome,
} from '../types'
import '../components/pod.css'

function formatDateTime(value: string | null): string {
  return value ? value.slice(0, 16).replace('T', ' ') : '—'
}

const OUTCOMES: PodOutcome[] = ['Complete', 'Partial', 'Refused']

/** Back-office POD detail: immutable proof, version chain, photos, signature — print-ready. */
export function PodDetailPage() {
  const { id = '' } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const { showSuccess, showError } = useToast()
  const { hasPermission } = useAuth()

  const [pod, setPod] = useState<PodDetail | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [photoUrls, setPhotoUrls] = useState<Record<string, string>>({})
  const [signatureUrl, setSignatureUrl] = useState<string | null>(null)

  const [correcting, setCorrecting] = useState(false)
  const [recipientName, setRecipientName] = useState('')
  const [recipientRole, setRecipientRole] = useState('')
  const [outcome, setOutcome] = useState<PodOutcome>('Complete')
  const [damageReported, setDamageReported] = useState(false)
  const [missingReported, setMissingReported] = useState(false)
  const [notes, setNotes] = useState('')
  const [signature, setSignature] = useState<string | null>(null)
  const [reason, setReason] = useState('')

  const canCorrect = hasPermission('pod.correct')

  useEffect(() => {
    let mounted = true
    getPod(id)
      .then((data) => {
        if (!mounted) return
        setPod(data)
        setLoadError(null)
      })
      .catch(() => {
        if (mounted) setLoadError('De POD kon niet worden geladen.')
      })
    return () => {
      mounted = false
    }
  }, [id])

  useEffect(() => {
    if (!pod) return
    let mounted = true
    const urls: string[] = []
    void (async () => {
      if (pod.hasSignature && !signatureUrl) {
        try {
          const url = await fetchPodSignatureUrl(pod.id)
          urls.push(url)
          if (mounted) setSignatureUrl(url)
        } catch {
          // Signature stays hidden.
        }
      }
      for (const photo of pod.photos) {
        if (photoUrls[photo.id]) continue
        try {
          const url = await fetchPodPhotoUrl(pod.id, photo.id)
          urls.push(url)
          if (mounted) setPhotoUrls((prev) => ({ ...prev, [photo.id]: url }))
        } catch {
          // Photo stays hidden; the caption shows the filename.
        }
      }
    })()
    return () => {
      mounted = false
      urls.forEach((url) => URL.revokeObjectURL(url))
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [pod])

  async function handleCorrect(event: FormEvent) {
    event.preventDefault()
    if (!reason.trim()) {
      showError('Een reden is verplicht bij een correctie.')
      return
    }
    if (!recipientName.trim()) {
      showError('De naam van de ontvanger is verplicht.')
      return
    }
    setBusy(true)
    try {
      const corrected = await correctPod(id, {
        recipientName: recipientName.trim(),
        recipientRole: recipientRole.trim() || null,
        outcome,
        damageReported,
        missingReported,
        notes: notes.trim() || null,
        signatureBase64: signature,
        latitude: null,
        longitude: null,
        reason: reason.trim(),
      })
      showSuccess(`Correctie vastgelegd als versie ${corrected.version}.`)
      setCorrecting(false)
      navigate(`/pods/${corrected.id}`, { replace: true })
      setPod(corrected)
      setPhotoUrls({})
      setSignatureUrl(null)
    } catch (err) {
      showError(err instanceof ApiError ? err.message : 'De correctie kon niet worden vastgelegd.')
    } finally {
      setBusy(false)
    }
  }

  if (loadError) return <ErrorState message={loadError} />
  if (!pod) return <LoadingState message="POD laden..." />

  const photosByCategory = (['Delivery', 'Package', 'Document'] as const)
    .map((category) => ({ category, photos: pod.photos.filter((p) => p.category === category) }))
    .filter((group) => group.photos.length > 0)

  return (
    <div className="pod-detail">
      <div className="pod-no-print">
        <Breadcrumbs items={[{ label: 'Afleverbewijs' }, { label: `${pod.tripNumber} · v${pod.version}` }]} />
      </div>
      <PageHeader
        title={`Afleverbewijs ${pod.orderNumber ?? ''} — ${pod.stopLabel ?? pod.tripNumber}`}
        subtitle={`Versie ${pod.version}${pod.isCurrent ? ' (actueel)' : ' (vervangen)'} · geleverd ${formatDateTime(pod.deliveredAt)}`}
        action={
          <span className="pod-header-actions">
            <Badge tone={POD_OUTCOME_TONE[pod.outcome]}>
              {POD_OUTCOME_ICONS[pod.outcome]} {POD_OUTCOME_LABELS[pod.outcome]}
            </Badge>
            <span className="pod-no-print">
              <Button variant="secondary" onClick={() => window.print()}>
                🖨 Afdrukken
              </Button>
              {canCorrect && pod.isCurrent && (
                <Button
                  variant="secondary"
                  onClick={() => {
                    setRecipientName(pod.recipientName)
                    setRecipientRole(pod.recipientRole ?? '')
                    setOutcome(pod.outcome)
                    setDamageReported(pod.damageReported)
                    setMissingReported(pod.missingReported)
                    setNotes(pod.notes ?? '')
                    setSignature(null)
                    setReason('')
                    setCorrecting(true)
                  }}
                  disabled={busy}
                >
                  Corrigeren
                </Button>
              )}
            </span>
          </span>
        }
      />

      {!pod.isCurrent && (
        <p className="pod-superseded" role="note">
          ⚠ Deze versie is vervangen door een correctie. De actuele versie staat in de versiehistoriek hieronder.
        </p>
      )}

      <section className="to-section">
        <h2>Levering</h2>
        <dl className="to-facts">
          <div>
            <dt>Ontvanger</dt>
            <dd>
              {pod.recipientName}
              {pod.recipientRole && ` (${pod.recipientRole})`}
            </dd>
          </div>
          <div>
            <dt>Klant</dt>
            <dd>{pod.customerName ?? '—'}</dd>
          </div>
          <div>
            <dt>Opdracht</dt>
            <dd>
              <Link to={`/transport-orders/${pod.transportOrderId}`}>{pod.orderNumber ?? '—'}</Link>
            </dd>
          </div>
          <div>
            <dt>Rit</dt>
            <dd>
              <Link to={`/planning/${pod.tripId}`}>{pod.tripNumber}</Link>
            </dd>
          </div>
          <div>
            <dt>Chauffeur</dt>
            <dd>{pod.driverName ?? '—'}</dd>
          </div>
          <div>
            <dt>Vastgelegd door</dt>
            <dd>{pod.finalisedByName ?? '—'}</dd>
          </div>
          <div>
            <dt>Schade</dt>
            <dd>{pod.damageReported ? '⚠ Ja' : 'Nee'}</dd>
          </div>
          <div>
            <dt>Ontbrekende colli</dt>
            <dd>{pod.missingReported ? '⚠ Ja' : 'Nee'}</dd>
          </div>
          <div>
            <dt>GPS</dt>
            <dd>{pod.latitude !== null && pod.longitude !== null ? `${pod.latitude}, ${pod.longitude}` : '—'}</dd>
          </div>
        </dl>
        {pod.notes && <p className="to-notes">{pod.notes}</p>}
        {pod.correctionReason && (
          <p className="pod-correction-reason" role="note">
            Correctie t.o.v. vorige versie: {pod.correctionReason}
          </p>
        )}
      </section>

      {pod.scannedSummary.length > 0 && (
        <section className="to-section">
          <h2>Gescande goederen (bevroren bij afronding)</h2>
          <table className="to-stops-table">
            <thead>
              <tr>
                <th>Omschrijving</th>
                <th>Barcode</th>
                <th>Verwacht</th>
                <th>Gescand</th>
                <th>Schade</th>
                <th>Status</th>
              </tr>
            </thead>
            <tbody>
              {pod.scannedSummary.map((line, index) => (
                <tr key={index}>
                  <td>{line.description}</td>
                  <td>{line.barcode ? <code>{line.barcode}</code> : '—'}</td>
                  <td>{line.expectedQuantity}</td>
                  <td>{line.scannedQuantity}</td>
                  <td>{line.damagedQuantity > 0 ? line.damagedQuantity : '—'}</td>
                  <td>{line.state}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </section>
      )}

      {pod.packageSummary.length > 0 && (
        <section className="to-section">
          <h2>Colli (bevroren bij afronding)</h2>
          <p className="pod-ack-line">
            {pod.packagesAcknowledged
              ? '✓ De ontvanger heeft de collilijst bevestigd.'
              : 'De collilijst is niet expliciet bevestigd.'}
          </p>
          <table className="to-stops-table">
            <thead>
              <tr>
                <th>Collinummer</th>
                <th>Omschrijving</th>
                <th>Aantal</th>
                <th>Uitkomst</th>
                <th>Melding</th>
              </tr>
            </thead>
            <tbody>
              {pod.packageSummary.map((line) => (
                <tr key={line.packageNumber}>
                  <td><code>{line.packageNumber}</code></td>
                  <td>{line.description}</td>
                  <td>
                    {line.quantity} {line.unitType}
                  </td>
                  <td>{line.outcome}</td>
                  <td>{line.exceptionOpen ? 'Open melding' : '—'}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </section>
      )}

      {(signatureUrl || pod.hasSignature) && (
        <section className="to-section">
          <h2>Handtekening</h2>
          {signatureUrl ? (
            <img className="pod-signature-image" src={signatureUrl} alt={`Handtekening van ${pod.recipientName}`} />
          ) : (
            <p>Handtekening aanwezig (kon niet worden geladen).</p>
          )}
        </section>
      )}

      {photosByCategory.map((group) => (
        <section className="to-section" key={group.category}>
          <h2>
            Foto's — {POD_PHOTO_CATEGORY_LABELS[group.category]} ({group.photos.length})
          </h2>
          <div className="exc-photos">
            {group.photos.map((photo) => (
              <figure key={photo.id} className="exc-photo">
                {photoUrls[photo.id] ? (
                  <a href={photoUrls[photo.id]} target="_blank" rel="noreferrer">
                    <img src={photoUrls[photo.id]} alt={photo.fileName} />
                  </a>
                ) : (
                  <span className="exc-photo-placeholder">{photo.fileName}</span>
                )}
                <figcaption>{photo.fileName}</figcaption>
              </figure>
            ))}
          </div>
        </section>
      ))}

      <section className="to-section pod-no-print">
        <h2>Versiehistoriek</h2>
        <ul className="pod-versions">
          {pod.versions.map((version) => (
            <li key={version.id}>
              <Link to={`/pods/${version.id}`} className={version.id === pod.id ? 'pod-version-current-link' : ''}>
                Versie {version.version}
              </Link>{' '}
              · {formatDateTime(version.deliveredAt)} · {POD_OUTCOME_LABELS[version.outcome]}
              {version.isCurrent && <Badge tone="success">actueel</Badge>}
              {version.correctionReason && ` · ${version.correctionReason}`}
            </li>
          ))}
        </ul>
      </section>

      {correcting && (
        <Modal
          title="POD corrigeren (nieuwe versie)"
          onClose={() => setCorrecting(false)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setCorrecting(false)} disabled={busy}>
                Annuleren
              </Button>
              <Button type="submit" form="pod-correct-form" disabled={busy}>
                {busy ? 'Bezig…' : 'Correctie vastleggen'}
              </Button>
            </>
          }
        >
          <form id="pod-correct-form" className="pod-form" onSubmit={handleCorrect} noValidate>
            <FormField label="Reden van de correctie" htmlFor="pod-corr-reason" required hint="De originele versie blijft zichtbaar in de historiek.">
              <input id="pod-corr-reason" value={reason} onChange={(e) => setReason(e.target.value)} disabled={busy} maxLength={500} autoFocus />
            </FormField>
            <FormField label="Naam ontvanger" htmlFor="pod-corr-recipient" required>
              <input id="pod-corr-recipient" value={recipientName} onChange={(e) => setRecipientName(e.target.value)} disabled={busy} maxLength={200} />
            </FormField>
            <FormField label="Functie/rol" htmlFor="pod-corr-role">
              <input id="pod-corr-role" value={recipientRole} onChange={(e) => setRecipientRole(e.target.value)} disabled={busy} maxLength={100} />
            </FormField>
            <FormField label="Resultaat" htmlFor="pod-corr-outcome">
              <select id="pod-corr-outcome" value={outcome} onChange={(e) => setOutcome(e.target.value as PodOutcome)} disabled={busy}>
                {OUTCOMES.map((option) => (
                  <option key={option} value={option}>
                    {POD_OUTCOME_LABELS[option]}
                  </option>
                ))}
              </select>
            </FormField>
            <div className="pod-flags">
              <label className="tof-checkbox">
                <input type="checkbox" checked={damageReported} onChange={(e) => setDamageReported(e.target.checked)} disabled={busy} />
                Schade vastgesteld
              </label>
              <label className="tof-checkbox">
                <input type="checkbox" checked={missingReported} onChange={(e) => setMissingReported(e.target.checked)} disabled={busy} />
                Ontbrekende colli
              </label>
            </div>
            <FormField label="Opmerkingen" htmlFor="pod-corr-notes">
              <textarea id="pod-corr-notes" rows={2} value={notes} onChange={(e) => setNotes(e.target.value)} disabled={busy} maxLength={2000} />
            </FormField>
            <FormField label="Nieuwe handtekening (optioneel)" htmlFor="pod-corr-signature" hint="Zonder nieuwe handtekening blijft de originele behouden.">
              <SignaturePad disabled={busy} onChange={setSignature} />
            </FormField>
          </form>
        </Modal>
      )}
    </div>
  )
}
