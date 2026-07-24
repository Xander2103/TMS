import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { useToast } from '../../../components/ui/toastContext'
import { ApiError } from '../../../api/apiClient'
import { describeApiError } from '../../../api/problemDetails'
import { searchDrivers } from '../../drivers/api/driversApi'
import type { DriverListItem } from '../../drivers/types'
import { PreparedFleetDocumentsEditor } from '../../fleet-documents/components/PreparedFleetDocumentsEditor'
import { FleetCreateFollowUpDialog } from '../../fleet-documents/components/FleetCreateFollowUpDialog'
import {
  uploadPreparedFleetDocuments,
  type FleetFollowUpResult,
  type PreparedFleetDocument,
} from '../../fleet-documents/utils/preparedFleetDocs'
import { createVehicle } from '../api/vehiclesApi'
import { VehicleForm } from '../components/VehicleForm'
import type { CreateVehicleInput } from '../types'
import './vehicle-form.css'

const EMPTY: CreateVehicleInput = {
  licensePlate: '',
  vin: null,
  categoryId: null,
  brand: null,
  model: null,
  year: null,
  firstRegistrationDate: null,
  fuelType: 'Diesel',
  emissionClass: null,
  grossVehicleWeightKg: null,
  payloadKg: null,
  lengthMeters: null,
  widthMeters: null,
  heightMeters: null,
  volumeM3: null,
  volumeIsManual: false,
  odometerKm: 0,
  consumptionLPer100Km: null,
  axleCount: 0,
  loadingMeters: 0,
  requiredLicenceCode: null,
  hasCrane: false,
  hasRefrigeration: false,
  hasTailLift: false,
  adrSuitable: false,
  ownershipType: 'Owned',
  operationalStatus: 'Available',
  statusReason: null,
  isActive: true,
  fixedDriverId: null,
  currentDriverId: null,
  notes: null,
}

export function NewVehiclePage() {
  const navigate = useNavigate()
  const { showSuccess, showError } = useToast()
  const [drivers, setDrivers] = useState<DriverListItem[]>([])
  const [preparedDocs, setPreparedDocs] = useState<PreparedFleetDocument[]>([])
  const [error, setError] = useState<string | null>(null)
  const [submitting, setSubmitting] = useState(false)
  const [followUp, setFollowUp] = useState<{ vehicleId: string; label: string; results: FleetFollowUpResult[] } | null>(null)
  const [retrying, setRetrying] = useState(false)

  useEffect(() => {
    let mounted = true
    searchDrivers({ isActive: true, page: 1, pageSize: 200 })
      .then((result) => {
        if (mounted) setDrivers(result.items)
      })
      .catch(() => {
        if (mounted) showError('Chauffeurs konden niet worden geladen.')
      })
    return () => {
      mounted = false
    }
  }, [showError])

  async function handleSubmit(values: CreateVehicleInput) {
    setError(null)
    setSubmitting(true)
    try {
      // The vehicle is created FIRST; document uploads only run against the created id, so
      // a failed creation never leaves orphaned document records.
      const vehicle = await createVehicle(values)
      const results = preparedDocs.length > 0
        ? await uploadPreparedFleetDocuments('vehicle', vehicle.id, preparedDocs)
        : []
      if (results.every((r) => r.ok)) {
        showSuccess(`Voertuig ${vehicle.internalNumber} aangemaakt.`)
        navigate(`/vehicles/${vehicle.id}`)
        return
      }

      // Remember created metadata records so a retry only re-uploads the file.
      setPreparedDocs((docs) =>
        docs.map((doc) => {
          const result = results.find((r) => r.key === doc.key)
          return result?.createdDocumentId ? { ...doc, createdDocumentId: result.createdDocumentId } : doc
        }),
      )
      setFollowUp({ vehicleId: vehicle.id, label: `Voertuig ${vehicle.internalNumber}`, results })
      setSubmitting(false)
    } catch (err) {
      setError(
        err instanceof ApiError && err.status === 409
          ? 'Er bestaat al een voertuig met dit kenteken.'
          : describeApiError(err, 'Voertuig kon niet worden aangemaakt.').message,
      )
      setSubmitting(false)
    }
  }

  async function retryFollowUps() {
    if (!followUp) return
    setRetrying(true)
    const failedKeys = new Set(followUp.results.filter((r) => !r.ok).map((r) => r.key))
    const retryDocs = preparedDocs.filter((doc) => failedKeys.has(doc.key))
    const retried = await uploadPreparedFleetDocuments('vehicle', followUp.vehicleId, retryDocs)
    const merged = followUp.results.map((r) => retried.find((n) => n.key === r.key) ?? r)
    setPreparedDocs((docs) =>
      docs.map((doc) => {
        const result = retried.find((r) => r.key === doc.key)
        return result?.createdDocumentId ? { ...doc, createdDocumentId: result.createdDocumentId } : doc
      }),
    )
    setRetrying(false)
    if (merged.every((r) => r.ok)) {
      showSuccess('Alle documenten zijn verwerkt.')
      navigate(`/vehicles/${followUp.vehicleId}`)
      return
    }
    setFollowUp({ ...followUp, results: merged })
  }

  return (
    <div>
      <Breadcrumbs items={[{ label: 'Voertuigen', to: '/vehicles' }, { label: 'Nieuw' }]} />
      <PageHeader title="Nieuw voertuig" />
      <VehicleForm
        mode="create"
        initial={EMPTY}
        isSubmitting={submitting}
        submitError={error}
        onSubmit={handleSubmit}
        onCancel={() => navigate('/vehicles')}
        drivers={drivers}
        documentsSection={<PreparedFleetDocumentsEditor value={preparedDocs} onChange={setPreparedDocs} />}
      />
      {followUp && (
        <FleetCreateFollowUpDialog
          entityLabel={followUp.label}
          results={followUp.results}
          busy={retrying}
          onRetry={retryFollowUps}
          onClose={() => navigate(`/vehicles/${followUp.vehicleId}`)}
        />
      )}
    </div>
  )
}
