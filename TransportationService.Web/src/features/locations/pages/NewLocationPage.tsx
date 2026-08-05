import { useState } from 'react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { useToast } from '../../../components/ui/toastContext'
import { ApiError } from '../../../api/apiClient'
import { createLocation } from '../api/locationsApi'
import { LocationForm } from '../components/LocationForm'
import { EMPTY_LOCATION_INPUT, type LocationInput } from '../types'
import './location-form.css'

export function NewLocationPage() {
  const navigate = useNavigate()
  const [searchParams] = useSearchParams()
  const { showSuccess } = useToast()
  // Arriving from a customer's Locations tab prefills the link and returns there afterwards.
  const prefilledCustomerId = searchParams.get('customerId')
  const [initial] = useState<LocationInput>(() =>
    prefilledCustomerId ? { ...EMPTY_LOCATION_INPUT, customerId: prefilledCustomerId, type: 'CustomerLocation' } : EMPTY_LOCATION_INPUT,
  )
  const [error, setError] = useState<string | null>(null)
  const [submitting, setSubmitting] = useState(false)

  async function handleSubmit(value: LocationInput) {
    setError(null)
    setSubmitting(true)
    try {
      const location = await createLocation(value)
      showSuccess(`Locatie ${location.code} aangemaakt.`)
      navigate(prefilledCustomerId ? `/customers/${prefilledCustomerId}` : `/locations/${location.id}`)
    } catch (err) {
      const message =
        err instanceof ApiError && err.status === 409
          ? 'Er bestaat al een locatie met deze code.'
          : 'Locatie kon niet worden aangemaakt.'
      setError(message)
      setSubmitting(false)
    }
  }

  return (
    <div>
      <Breadcrumbs items={[{ label: 'Locaties', to: '/locations' }, { label: 'Nieuw' }]} />
      <PageHeader title="Nieuwe locatie" />
      <LocationForm
        mode="create"
        initial={initial}
        submitting={submitting}
        error={error}
        onSubmit={(value) => void handleSubmit(value)}
        onCancel={() => navigate('/locations')}
      />
    </div>
  )
}
