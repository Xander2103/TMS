import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { useToast } from '../../../components/ui/toastContext'
import { ApiError } from '../../../api/apiClient'
import { localizeApiError } from '../../../api/problemDetails'
import { useLocale } from '../../../i18n/localeContext'
import { PreparedFleetDocumentsEditor } from '../../fleet-documents/components/PreparedFleetDocumentsEditor'
import { FleetCreateFollowUpDialog } from '../../fleet-documents/components/FleetCreateFollowUpDialog'
import {
  uploadPreparedFleetDocuments,
  type FleetFollowUpResult,
  type PreparedFleetDocument,
} from '../../fleet-documents/utils/preparedFleetDocs'
import { createTrailer } from '../api/trailersApi'
import { TrailerForm } from '../components/TrailerForm'
import type { TrailerInput } from '../types'
import './trailer-form.css'

const EMPTY: TrailerInput = {
  licensePlate: '',
  vin: null,
  categoryId: null,
  brand: null,
  model: null,
  year: null,
  firstRegistrationDate: null,
  capacityKg: null,
  lengthMeters: null,
  widthMeters: null,
  heightMeters: null,
  volumeM3: null,
  volumeIsManual: false,
  axleCount: 0,
  loadingMeters: 0,
  hasRefrigeration: false,
  adrSuitable: false,
  ownershipType: 'Owned',
  operationalStatus: 'Available',
  statusReason: null,
  isActive: true,
  notes: null,
}

export function NewTrailerPage() {
  const navigate = useNavigate()
  const { t } = useLocale()
  const { showSuccess } = useToast()
  const [preparedDocs, setPreparedDocs] = useState<PreparedFleetDocument[]>([])
  const [error, setError] = useState<string | null>(null)
  const [submitting, setSubmitting] = useState(false)
  const [followUp, setFollowUp] = useState<{ trailerId: string; label: string; results: FleetFollowUpResult[] } | null>(null)
  const [retrying, setRetrying] = useState(false)

  async function handleSubmit(values: TrailerInput) {
    setError(null)
    setSubmitting(true)
    try {
      // Trailer FIRST, documents second: a failed creation never leaves orphaned records.
      const trailer = await createTrailer(values)
      const results = preparedDocs.length > 0
        ? await uploadPreparedFleetDocuments('trailer', trailer.id, preparedDocs)
        : []
      if (results.every((r) => r.ok)) {
        showSuccess(t('trailers.new.created', { number: trailer.internalNumber }))
        navigate(`/trailers/${trailer.id}`)
        return
      }

      setPreparedDocs((docs) =>
        docs.map((doc) => {
          const result = results.find((r) => r.key === doc.key)
          return result?.createdDocumentId ? { ...doc, createdDocumentId: result.createdDocumentId } : doc
        }),
      )
      setFollowUp({ trailerId: trailer.id, label: t('trailers.new.followUpLabel', { number: trailer.internalNumber }), results })
      setSubmitting(false)
    } catch (err) {
      setError(
        err instanceof ApiError && err.status === 409
          ? t('trailers.new.duplicate')
          : localizeApiError(t, err, t('trailers.new.createFailed')),
      )
      setSubmitting(false)
    }
  }

  async function retryFollowUps() {
    if (!followUp) return
    setRetrying(true)
    const failedKeys = new Set(followUp.results.filter((r) => !r.ok).map((r) => r.key))
    const retryDocs = preparedDocs.filter((doc) => failedKeys.has(doc.key))
    const retried = await uploadPreparedFleetDocuments('trailer', followUp.trailerId, retryDocs)
    const merged = followUp.results.map((r) => retried.find((n) => n.key === r.key) ?? r)
    setPreparedDocs((docs) =>
      docs.map((doc) => {
        const result = retried.find((r) => r.key === doc.key)
        return result?.createdDocumentId ? { ...doc, createdDocumentId: result.createdDocumentId } : doc
      }),
    )
    setRetrying(false)
    if (merged.every((r) => r.ok)) {
      showSuccess(t('fleet.docs.followUp.allProcessed'))
      navigate(`/trailers/${followUp.trailerId}`)
      return
    }
    setFollowUp({ ...followUp, results: merged })
  }

  return (
    <div>
      <Breadcrumbs items={[{ label: t('navigation.menu.trailers'), to: '/trailers' }, { label: t('fleet.common.new') }]} />
      <PageHeader title={t('trailers.list.new')} />
      <TrailerForm
        mode="create"
        initial={EMPTY}
        isSubmitting={submitting}
        submitError={error}
        onSubmit={handleSubmit}
        onCancel={() => navigate('/trailers')}
        documentsSection={<PreparedFleetDocumentsEditor value={preparedDocs} onChange={setPreparedDocs} />}
      />
      {followUp && (
        <FleetCreateFollowUpDialog
          entityLabel={followUp.label}
          results={followUp.results}
          busy={retrying}
          onRetry={retryFollowUps}
          onClose={() => navigate(`/trailers/${followUp.trailerId}`)}
        />
      )}
    </div>
  )
}
