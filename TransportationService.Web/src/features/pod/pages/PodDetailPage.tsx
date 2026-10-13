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
import { useLocale } from '../../../i18n/localeContext'
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
import { formatDateTime as formatDateTimeIso } from '../../../utils/dates'
import '../components/pod.css'

function formatDateTime(value: string | null): string {
  return value ? formatDateTimeIso(value) : '—'
}

const OUTCOMES: PodOutcome[] = ['Complete', 'Partial', 'Refused']

/** Back-office POD detail: immutable proof, version chain, photos, signature — print-ready. */
export function PodDetailPage() {
  const { id = '' } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const { showSuccess, showError } = useToast()
  const { hasPermission } = useAuth()
  const { t } = useLocale()

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
        if (mounted) setLoadError(t('pod.detail.loadError'))
      })
    return () => {
      mounted = false
    }
  }, [id, t])

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
      showError(t('pod.detail.reasonRequired'))
      return
    }
    if (!recipientName.trim()) {
      showError(t('pod.dialog.recipientRequired'))
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
      showSuccess(t('pod.detail.corrected', { version: corrected.version }))
      setCorrecting(false)
      navigate(`/pods/${corrected.id}`, { replace: true })
      setPod(corrected)
      setPhotoUrls({})
      setSignatureUrl(null)
    } catch (err) {
      showError(err instanceof ApiError ? err.message : t('pod.detail.correctFailed'))
    } finally {
      setBusy(false)
    }
  }

  if (loadError) return <ErrorState message={loadError} />
  if (!pod) return <LoadingState message={t('pod.detail.loading')} />

  const photosByCategory = (['Delivery', 'Package', 'Document'] as const)
    .map((category) => ({ category, photos: pod.photos.filter((p) => p.category === category) }))
    .filter((group) => group.photos.length > 0)

  return (
    <div className="pod-detail">
      <div className="pod-no-print">
        <Breadcrumbs items={[{ label: t('pod.detail.breadcrumb') }, { label: `${pod.tripNumber} · v${pod.version}` }]} />
      </div>
      <PageHeader
        title={`${t('pod.detail.titlePrefix')} ${pod.orderNumber ?? ''} — ${pod.stopLabel ?? pod.tripNumber}`}
        subtitle={`${t('pod.detail.version', { version: pod.version })} ${pod.isCurrent ? t('pod.detail.currentSuffix') : t('pod.detail.supersededSuffix')} · ${t('pod.detail.deliveredAt', { date: formatDateTime(pod.deliveredAt) })}`}
        action={
          <span className="pod-header-actions">
            <Badge tone={POD_OUTCOME_TONE[pod.outcome]}>
              {POD_OUTCOME_ICONS[pod.outcome]} {t(POD_OUTCOME_LABELS[pod.outcome])}
            </Badge>
            <span className="pod-no-print">
              <Button variant="secondary" onClick={() => window.print()}>
                🖨 {t('pod.detail.print')}
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
                  {t('pod.detail.correct')}
                </Button>
              )}
            </span>
          </span>
        }
      />

      {!pod.isCurrent && (
        <p className="pod-superseded" role="note">
          ⚠ {t('pod.detail.supersededNote')}
        </p>
      )}

      <section className="to-section">
        <h2>{t('pod.detail.deliveryTitle')}</h2>
        <dl className="to-facts">
          <div>
            <dt>{t('pod.detail.recipient')}</dt>
            <dd>
              {pod.recipientName}
              {pod.recipientRole && ` (${pod.recipientRole})`}
            </dd>
          </div>
          <div>
            <dt>{t('pod.detail.customer')}</dt>
            <dd>{pod.customerName ?? '—'}</dd>
          </div>
          <div>
            <dt>{t('pod.detail.order')}</dt>
            <dd>
              <Link to={`/transport-orders/${pod.transportOrderId}`}>{pod.orderNumber ?? '—'}</Link>
            </dd>
          </div>
          <div>
            <dt>{t('pod.detail.trip')}</dt>
            <dd>
              <Link to={`/planning/${pod.tripId}`}>{pod.tripNumber}</Link>
            </dd>
          </div>
          <div>
            <dt>{t('pod.detail.driver')}</dt>
            <dd>{pod.driverName ?? '—'}</dd>
          </div>
          <div>
            <dt>{t('pod.detail.finalisedBy')}</dt>
            <dd>{pod.finalisedByName ?? '—'}</dd>
          </div>
          <div>
            <dt>{t('pod.detail.damage')}</dt>
            <dd>{pod.damageReported ? `⚠ ${t('pod.detail.yes')}` : t('pod.detail.no')}</dd>
          </div>
          <div>
            <dt>{t('pod.detail.missingPackages')}</dt>
            <dd>{pod.missingReported ? `⚠ ${t('pod.detail.yes')}` : t('pod.detail.no')}</dd>
          </div>
          <div>
            <dt>{t('pod.detail.gps')}</dt>
            <dd>{pod.latitude !== null && pod.longitude !== null ? `${pod.latitude}, ${pod.longitude}` : '—'}</dd>
          </div>
        </dl>
        {pod.notes && <p className="to-notes">{pod.notes}</p>}
        {pod.correctionReason && (
          <p className="pod-correction-reason" role="note">
            {t('pod.detail.correctionReason', { reason: pod.correctionReason })}
          </p>
        )}
      </section>

      {pod.scannedSummary.length > 0 && (
        <section className="to-section">
          <h2>{t('pod.detail.scannedTitle')}</h2>
          <table className="to-stops-table">
            <thead>
              <tr>
                <th>{t('pod.detail.colDescription')}</th>
                <th>{t('pod.detail.colBarcode')}</th>
                <th>{t('pod.detail.colExpected')}</th>
                <th>{t('pod.detail.colScanned')}</th>
                <th>{t('pod.detail.colDamage')}</th>
                <th>{t('pod.detail.colStatus')}</th>
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
          <h2>{t('pod.detail.packagesTitle')}</h2>
          <p className="pod-ack-line">
            {pod.packagesAcknowledged
              ? `✓ ${t('pod.detail.ackYes')}`
              : t('pod.detail.ackNo')}
          </p>
          <table className="to-stops-table">
            <thead>
              <tr>
                <th>{t('pod.detail.colPackageNumber')}</th>
                <th>{t('pod.detail.colDescription')}</th>
                <th>{t('pod.detail.colQuantity')}</th>
                <th>{t('pod.detail.colOutcome')}</th>
                <th>{t('pod.detail.colException')}</th>
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
                  <td>{line.exceptionOpen ? t('pod.detail.exceptionOpen') : '—'}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </section>
      )}

      {(signatureUrl || pod.hasSignature) && (
        <section className="to-section">
          <h2>{t('pod.detail.signatureTitle')}</h2>
          {signatureUrl ? (
            <img className="pod-signature-image" src={signatureUrl} alt={t('pod.detail.signatureAlt', { name: pod.recipientName })} />
          ) : (
            <p>{t('pod.detail.signaturePresent')}</p>
          )}
        </section>
      )}

      {photosByCategory.map((group) => (
        <section className="to-section" key={group.category}>
          <h2>
            {t('pod.detail.photosTitle', { category: t(POD_PHOTO_CATEGORY_LABELS[group.category]), count: group.photos.length })}
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
        <h2>{t('pod.detail.versionsTitle')}</h2>
        <ul className="pod-versions">
          {pod.versions.map((version) => (
            <li key={version.id}>
              <Link to={`/pods/${version.id}`} className={version.id === pod.id ? 'pod-version-current-link' : ''}>
                {t('pod.detail.versionItem', { version: version.version })}
              </Link>{' '}
              · {formatDateTime(version.deliveredAt)} · {t(POD_OUTCOME_LABELS[version.outcome])}
              {version.isCurrent && <Badge tone="success">{t('pod.detail.currentBadge')}</Badge>}
              {version.correctionReason && ` · ${version.correctionReason}`}
            </li>
          ))}
        </ul>
      </section>

      {correcting && (
        <Modal
          title={t('pod.detail.correctTitle')}
          onClose={() => setCorrecting(false)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setCorrecting(false)} disabled={busy}>
                {t('pod.detail.cancel')}
              </Button>
              <Button type="submit" form="pod-correct-form" disabled={busy}>
                {busy ? t('pod.detail.busy') : t('pod.detail.correctSubmit')}
              </Button>
            </>
          }
        >
          <form id="pod-correct-form" className="pod-form" onSubmit={handleCorrect} noValidate>
            <FormField label={t('pod.detail.correctReasonLabel')} htmlFor="pod-corr-reason" required hint={t('pod.detail.correctReasonHint')}>
              <input id="pod-corr-reason" value={reason} onChange={(e) => setReason(e.target.value)} disabled={busy} maxLength={500} autoFocus />
            </FormField>
            <FormField label={t('pod.detail.recipientLabel')} htmlFor="pod-corr-recipient" required>
              <input id="pod-corr-recipient" value={recipientName} onChange={(e) => setRecipientName(e.target.value)} disabled={busy} maxLength={200} />
            </FormField>
            <FormField label={t('pod.detail.roleLabel')} htmlFor="pod-corr-role">
              <input id="pod-corr-role" value={recipientRole} onChange={(e) => setRecipientRole(e.target.value)} disabled={busy} maxLength={100} />
            </FormField>
            <FormField label={t('pod.detail.outcomeLabel')} htmlFor="pod-corr-outcome">
              <select id="pod-corr-outcome" value={outcome} onChange={(e) => setOutcome(e.target.value as PodOutcome)} disabled={busy}>
                {OUTCOMES.map((option) => (
                  <option key={option} value={option}>
                    {t(POD_OUTCOME_LABELS[option])}
                  </option>
                ))}
              </select>
            </FormField>
            <div className="pod-flags">
              <label className="tof-checkbox">
                <input type="checkbox" checked={damageReported} onChange={(e) => setDamageReported(e.target.checked)} disabled={busy} />
                {t('pod.detail.damageFlag')}
              </label>
              <label className="tof-checkbox">
                <input type="checkbox" checked={missingReported} onChange={(e) => setMissingReported(e.target.checked)} disabled={busy} />
                {t('pod.detail.missingFlag')}
              </label>
            </div>
            <FormField label={t('pod.detail.notesLabel')} htmlFor="pod-corr-notes">
              <textarea id="pod-corr-notes" rows={2} value={notes} onChange={(e) => setNotes(e.target.value)} disabled={busy} maxLength={2000} />
            </FormField>
            <FormField label={t('pod.detail.newSignatureLabel')} htmlFor="pod-corr-signature" hint={t('pod.detail.newSignatureHint')}>
              <SignaturePad disabled={busy} onChange={setSignature} />
            </FormField>
          </form>
        </Modal>
      )}
    </div>
  )
}
