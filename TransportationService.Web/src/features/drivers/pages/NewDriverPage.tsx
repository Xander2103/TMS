import { useEffect, useState, type FormEvent } from 'react'
import { useNavigate } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { FormField } from '../../../components/ui/FormField'
import { Button } from '../../../components/ui/Button'
import { useToast } from '../../../components/ui/toastContext'
import { ApiError } from '../../../api/apiClient'
import { useLookupOptions } from '../../master-data/hooks/useLookupOptions'
import { searchEmployees } from '../../employees/api/employeesApi'
import type { EmployeeListItem } from '../../employees/types/employee'
import { createDriver } from '../api/driversApi'
import { AVAILABILITY_LABELS, type DriverAvailabilityStatus } from '../types'
import './driver-detail.css'

export function NewDriverPage() {
  const navigate = useNavigate()
  const { showSuccess, showError } = useToast()
  const { options: categories } = useLookupOptions('/api/driver-categories')

  const [employees, setEmployees] = useState<EmployeeListItem[]>([])
  const [employeeId, setEmployeeId] = useState('')
  const [categoryId, setCategoryId] = useState('')
  const [availability, setAvailability] = useState<DriverAvailabilityStatus>('Available')
  const [fixedVehicle, setFixedVehicle] = useState(false)
  const [notes, setNotes] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [submitting, setSubmitting] = useState(false)

  useEffect(() => {
    let mounted = true
    searchEmployees({ isActive: true, page: 1, pageSize: 200 })
      .then((result) => {
        if (mounted) setEmployees(result.items)
      })
      .catch(() => {
        if (mounted) showError('Medewerkers konden niet worden geladen.')
      })
    return () => {
      mounted = false
    }
  }, [showError])

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setError(null)
    if (!employeeId) {
      setError('Selecteer een medewerker.')
      return
    }
    setSubmitting(true)
    try {
      const driver = await createDriver({
        employeeId,
        driverCategoryId: categoryId || null,
        availabilityStatus: availability,
        fixedVehiclePreference: fixedVehicle,
        defaultVehicleId: null,
        preferredVehicleId: null,
        defaultTrailerId: null,
        notes: notes.trim() || null,
      })
      showSuccess(`Chauffeur ${driver.driverNumber} aangemaakt.`)
      navigate(`/drivers/${driver.id}`)
    } catch (err) {
      const message =
        err instanceof ApiError && err.status === 409
          ? 'Deze medewerker is al gekoppeld aan een chauffeur.'
          : 'Chauffeur kon niet worden aangemaakt.'
      setError(message)
      setSubmitting(false)
    }
  }

  return (
    <div>
      <Breadcrumbs items={[{ label: 'Chauffeurs', to: '/drivers' }, { label: 'Nieuw' }]} />
      <PageHeader title="Nieuwe chauffeur" />
      <form className="driver-form" onSubmit={handleSubmit} noValidate>
        {error && (
          <div className="driver-form-error" role="alert">
            {error}
          </div>
        )}

        <FormField label="Medewerker" htmlFor="driver-employee" required>
          <select id="driver-employee" value={employeeId} onChange={(e) => setEmployeeId(e.target.value)} disabled={submitting}>
            <option value="">— Selecteer een medewerker —</option>
            {employees.map((e) => (
              <option key={e.id} value={e.id}>
                {e.firstName} {e.lastName} ({e.employeeNumber})
              </option>
            ))}
          </select>
        </FormField>

        <FormField label="Chauffeurcategorie" htmlFor="driver-category">
          <select id="driver-category" value={categoryId} onChange={(e) => setCategoryId(e.target.value)} disabled={submitting}>
            <option value="">— Geen —</option>
            {categories.map((c) => (
              <option key={c.id} value={c.id}>
                {c.name}
              </option>
            ))}
          </select>
        </FormField>

        <FormField label="Beschikbaarheid" htmlFor="driver-availability">
          <select
            id="driver-availability"
            value={availability}
            onChange={(e) => setAvailability(e.target.value as DriverAvailabilityStatus)}
            disabled={submitting}
          >
            {(Object.keys(AVAILABILITY_LABELS) as DriverAvailabilityStatus[]).map((status) => (
              <option key={status} value={status}>
                {AVAILABILITY_LABELS[status]}
              </option>
            ))}
          </select>
        </FormField>

        <FormField label="Vaste voertuigvoorkeur" htmlFor="driver-fixed">
          <label className="driver-checkbox">
            <input id="driver-fixed" type="checkbox" checked={fixedVehicle} onChange={(e) => setFixedVehicle(e.target.checked)} disabled={submitting} />
            <span>Altijd hetzelfde voertuig toewijzen waar mogelijk</span>
          </label>
        </FormField>

        <FormField label="Notities" htmlFor="driver-notes">
          <textarea id="driver-notes" rows={3} value={notes} onChange={(e) => setNotes(e.target.value)} disabled={submitting} />
        </FormField>

        <div className="driver-form-actions">
          <Button type="button" variant="secondary" onClick={() => navigate('/drivers')} disabled={submitting}>
            Annuleren
          </Button>
          <Button type="submit" disabled={submitting}>
            {submitting ? 'Bezig…' : 'Chauffeur aanmaken'}
          </Button>
        </div>
      </form>
    </div>
  )
}
