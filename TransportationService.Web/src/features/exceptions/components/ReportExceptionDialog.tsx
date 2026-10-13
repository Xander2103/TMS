import { useState, type FormEvent } from 'react'
import { ApiError } from '../../../api/apiClient'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { useToast } from '../../../components/ui/toastContext'
import { useLocale } from '../../../i18n/localeContext'
import { reportException, uploadExceptionPhoto } from '../api/exceptionsApi'
import {
  EXCEPTION_SEVERITIES,
  EXCEPTION_SEVERITY_LABELS,
  EXCEPTION_TYPES,
  EXCEPTION_TYPE_LABELS,
  type ExceptionSeverity,
  type ExecutionExceptionType,
} from '../types'
import './exceptions.css'

interface ReportExceptionDialogProps {
  tripId: string
  stopId: string | null
  stopLabel: string | null
  cargoOptions?: Array<{ id: string; label: string }>
  onClose: () => void
  onReported: () => void
}

/**
 * Mobile-first driver report: pick a type, describe, optionally attach photos straight from
 * the camera. Photos upload after the report is created; a failed photo never loses the report.
 */
export function ReportExceptionDialog({ tripId, stopId, stopLabel, cargoOptions, onClose, onReported }: ReportExceptionDialogProps) {
  const { showSuccess, showError } = useToast()
  const { t } = useLocale()

  const [type, setType] = useState<ExecutionExceptionType>('DamagedPackage')
  const [severity, setSeverity] = useState<ExceptionSeverity>('Medium')
  const [description, setDescription] = useState('')
  const [cargoItemId, setCargoItemId] = useState('')
  const [quantity, setQuantity] = useState('')
  const [files, setFiles] = useState<File[]>([])
  const [busy, setBusy] = useState(false)

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    if (!description.trim()) {
      showError(t('exceptions.report.descriptionRequired'))
      return
    }
    setBusy(true)
    try {
      const created = await reportException(tripId, {
        type,
        severity,
        description: description.trim(),
        transportOrderStopId: stopId,
        cargoItemId: cargoItemId || null,
        quantity: quantity === '' ? null : Number(quantity),
        latitude: null,
        longitude: null,
      })
      let photoFailures = 0
      for (const file of files) {
        try {
          await uploadExceptionPhoto(created.id, file)
        } catch {
          photoFailures += 1
        }
      }
      if (photoFailures > 0) {
        showError(t('exceptions.report.photosFailed', { count: photoFailures }))
      } else {
        showSuccess(t('exceptions.report.reported'))
      }
      onReported()
      onClose()
    } catch (err) {
      showError(err instanceof ApiError ? err.message : t('exceptions.report.createFailed'))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      title={stopLabel ? t('exceptions.report.titleWithStop', { stop: stopLabel }) : t('exceptions.report.titleTrip')}
      onClose={onClose}
      busy={busy}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            {t('exceptions.report.cancel')}
          </Button>
          <Button type="submit" form="exc-report-form" disabled={busy}>
            {busy ? t('exceptions.report.busy') : t('exceptions.report.submit')}
          </Button>
        </>
      }
    >
      <form id="exc-report-form" className="exc-form" onSubmit={handleSubmit} noValidate>
        <FormField label={t('exceptions.report.typeLabel')} htmlFor="exc-type" required>
          <select id="exc-type" value={type} onChange={(e) => setType(e.target.value as ExecutionExceptionType)} disabled={busy}>
            {EXCEPTION_TYPES.map((option) => (
              <option key={option} value={option}>
                {t(EXCEPTION_TYPE_LABELS[option])}
              </option>
            ))}
          </select>
        </FormField>
        <FormField label={t('exceptions.report.severityLabel')} htmlFor="exc-severity" required>
          <select id="exc-severity" value={severity} onChange={(e) => setSeverity(e.target.value as ExceptionSeverity)} disabled={busy}>
            {EXCEPTION_SEVERITIES.map((s) => (
              <option key={s} value={s}>
                {t(EXCEPTION_SEVERITY_LABELS[s])}
              </option>
            ))}
          </select>
        </FormField>
        <FormField label={t('exceptions.report.descriptionLabel')} htmlFor="exc-description" required>
          <textarea
            id="exc-description"
            rows={3}
            value={description}
            onChange={(e) => setDescription(e.target.value)}
            disabled={busy}
            maxLength={2000}
            placeholder={t('exceptions.report.descriptionPlaceholder')}
            autoFocus
          />
        </FormField>
        {cargoOptions && cargoOptions.length > 0 && (
          <FormField label={t('exceptions.report.cargoLabel')} htmlFor="exc-cargo">
            <select id="exc-cargo" value={cargoItemId} onChange={(e) => setCargoItemId(e.target.value)} disabled={busy}>
              <option value="">—</option>
              {cargoOptions.map((c) => (
                <option key={c.id} value={c.id}>
                  {c.label}
                </option>
              ))}
            </select>
          </FormField>
        )}
        <FormField label={t('exceptions.report.quantityLabel')} htmlFor="exc-quantity">
          <input
            id="exc-quantity"
            inputMode="decimal"
            value={quantity}
            onChange={(e) => setQuantity(e.target.value)}
            disabled={busy}
          />
        </FormField>
        <FormField label={t('exceptions.report.photosLabel')} htmlFor="exc-photos" hint={t('exceptions.report.photosHint')}>
          <input
            id="exc-photos"
            type="file"
            accept="image/*"
            capture="environment"
            multiple
            onChange={(e) => setFiles(Array.from(e.target.files ?? []))}
            disabled={busy}
          />
        </FormField>
        {files.length > 0 && <p className="exc-photo-count">{t('exceptions.report.photosSelected', { count: files.length })}</p>}
      </form>
    </Modal>
  )
}
