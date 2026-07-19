import { useEffect, useState, type FormEvent } from 'react'
import { ApiError } from '../../../api/apiClient'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { useToast } from '../../../components/ui/toastContext'
import { getTripPackageChecklist } from '../../packages/api/packagesApi'
import {
  PACKAGE_STATUS_LABELS,
  PACKAGE_STATUS_TONE,
  type TripPackageChecklistItem,
} from '../../packages/types'
import { finalizePod, uploadPodPhoto } from '../api/podApi'
import { SignaturePad } from './SignaturePad'
import { POD_OUTCOME_ICONS, POD_OUTCOME_LABELS, type PodOutcome, type PodPhotoCategory } from '../types'
import './pod.css'

interface PodDialogProps {
  tripId: string
  stopId: string
  stopLabel: string
  onClose: () => void
  onFinalized: () => void
}

const OUTCOMES: PodOutcome[] = ['Complete', 'Partial', 'Refused']

/**
 * Driver POD capture: recipient, outcome, damage/missing flags, signature and photos.
 * The proof finalises immutably server-side; photos upload afterwards (additive evidence).
 */
export function PodDialog({ tripId, stopId, stopLabel, onClose, onFinalized }: PodDialogProps) {
  const { showSuccess, showError } = useToast()

  const [recipientName, setRecipientName] = useState('')
  const [recipientRole, setRecipientRole] = useState('')
  const [outcome, setOutcome] = useState<PodOutcome>('Complete')
  const [damageReported, setDamageReported] = useState(false)
  const [missingReported, setMissingReported] = useState(false)
  const [notes, setNotes] = useState('')
  const [signature, setSignature] = useState<string | null>(null)
  const [deliveryFiles, setDeliveryFiles] = useState<File[]>([])
  const [documentFiles, setDocumentFiles] = useState<File[]>([])
  const [packages, setPackages] = useState<TripPackageChecklistItem[]>([])
  const [packagesAcknowledged, setPackagesAcknowledged] = useState(false)
  const [busy, setBusy] = useState(false)

  // The recipient confirms the per-package outcome list as part of the proof.
  useEffect(() => {
    let mounted = true
    getTripPackageChecklist(tripId, stopId)
      .then((checklist) => {
        if (mounted) setPackages((checklist.stops[0]?.packages ?? []).filter((p) => !p.isGroup))
      })
      .catch(() => {
        // No package block; the scan summary still freezes server-side.
      })
    return () => {
      mounted = false
    }
  }, [tripId, stopId])

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    if (!recipientName.trim()) {
      showError('De naam van de ontvanger is verplicht.')
      return
    }
    if (packages.length > 0 && !packagesAcknowledged) {
      showError('Bevestig de collilijst met de ontvanger voordat je de POD afrondt.')
      return
    }
    setBusy(true)
    try {
      const pod = await finalizePod(tripId, stopId, {
        recipientName: recipientName.trim(),
        recipientRole: recipientRole.trim() || null,
        outcome,
        damageReported,
        missingReported,
        notes: notes.trim() || null,
        signatureBase64: signature,
        latitude: null,
        longitude: null,
        packagesAcknowledged,
      })

      let photoFailures = 0
      const uploads: Array<[PodPhotoCategory, File[]]> = [
        ['Delivery', deliveryFiles],
        ['Document', documentFiles],
      ]
      for (const [category, files] of uploads) {
        for (const file of files) {
          try {
            await uploadPodPhoto(pod.id, category, file)
          } catch {
            photoFailures += 1
          }
        }
      }

      if (photoFailures > 0) {
        showError(`POD afgerond, maar ${photoFailures} foto('s) konden niet worden geüpload.`)
      } else {
        showSuccess('POD afgerond en vastgelegd.')
      }
      onFinalized()
      onClose()
    } catch (err) {
      showError(err instanceof ApiError ? err.message : 'De POD kon niet worden afgerond.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      title={`POD opnemen — ${stopLabel}`}
      onClose={onClose}
      busy={busy}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Annuleren
          </Button>
          <Button type="submit" form="pod-form" disabled={busy}>
            {busy ? 'Bezig…' : 'POD afronden'}
          </Button>
        </>
      }
    >
      <form id="pod-form" className="pod-form" onSubmit={handleSubmit} noValidate>
        <div className="pod-outcomes" role="radiogroup" aria-label="Resultaat van de levering">
          {OUTCOMES.map((option) => (
            <label key={option} className={`pod-outcome ${outcome === option ? 'pod-outcome-active' : ''}`}>
              <input
                type="radio"
                name="pod-outcome"
                value={option}
                checked={outcome === option}
                onChange={() => setOutcome(option)}
                disabled={busy}
              />
              <span className="pod-outcome-icon" aria-hidden="true">
                {POD_OUTCOME_ICONS[option]}
              </span>
              {POD_OUTCOME_LABELS[option]}
            </label>
          ))}
        </div>

        <FormField label="Naam ontvanger" htmlFor="pod-recipient" required>
          <input id="pod-recipient" value={recipientName} onChange={(e) => setRecipientName(e.target.value)} disabled={busy} maxLength={200} autoFocus />
        </FormField>
        <FormField label="Functie/rol" htmlFor="pod-role">
          <input id="pod-role" value={recipientRole} onChange={(e) => setRecipientRole(e.target.value)} disabled={busy} maxLength={100} placeholder="bv. magazijnier" />
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

        <FormField label="Opmerkingen" htmlFor="pod-notes">
          <textarea id="pod-notes" rows={2} value={notes} onChange={(e) => setNotes(e.target.value)} disabled={busy} maxLength={2000} />
        </FormField>

        {packages.length > 0 && (
          <div className="pod-packages">
            <h3>Colli op deze stop</h3>
            <ul>
              {packages.map((item) => (
                <li key={item.packageId}>
                  <span className="pod-package-number">{item.packageNumber}</span>
                  <span className="pod-package-desc">{item.description}</span>
                  <Badge tone={PACKAGE_STATUS_TONE[item.status]}>{PACKAGE_STATUS_LABELS[item.status]}</Badge>
                </li>
              ))}
            </ul>
            <label className="tof-checkbox">
              <input
                type="checkbox"
                checked={packagesAcknowledged}
                onChange={(e) => setPackagesAcknowledged(e.target.checked)}
                disabled={busy}
              />
              De ontvanger heeft de collilijst en uitkomsten bevestigd
            </label>
          </div>
        )}

        <FormField label="Handtekening ontvanger" htmlFor="pod-signature">
          <SignaturePad disabled={busy} onChange={setSignature} />
        </FormField>

        <FormField label="Foto's levering" htmlFor="pod-photos-delivery" hint="Afgeleverde goederen, losplaats…">
          <input
            id="pod-photos-delivery"
            type="file"
            accept="image/*"
            capture="environment"
            multiple
            onChange={(e) => setDeliveryFiles(Array.from(e.target.files ?? []))}
            disabled={busy}
          />
        </FormField>
        <FormField label="Foto's documenten" htmlFor="pod-photos-documents" hint="Getekende CMR, leverbon…">
          <input
            id="pod-photos-documents"
            type="file"
            accept="image/*"
            capture="environment"
            multiple
            onChange={(e) => setDocumentFiles(Array.from(e.target.files ?? []))}
            disabled={busy}
          />
        </FormField>
      </form>
    </Modal>
  )
}
