import { useEffect, useState, type FormEvent } from 'react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { FormField } from '../../../components/ui/FormField'
import { Button } from '../../../components/ui/Button'
import { SearchableSelect } from '../../../components/ui/SearchableSelect'
import { useToast } from '../../../components/ui/toastContext'
import { ApiError } from '../../../api/apiClient'
import { LookupSelect } from '../../master-data/components/LookupSelect'
import { searchEmployees } from '../../employees/api/employeesApi'
import type { EmployeeListItem } from '../../employees/types/employee'
import { createDriver } from '../api/driversApi'
import { AVAILABILITY_LABELS, type DriverAvailabilityStatus } from '../types'
import './driver-detail.css'

export function NewDriverPage() {
  const navigate = useNavigate()
  const [searchParams] = useSearchParams()
  const { showSuccess, showError } = useToast()

  const [employees, setEmployees] = useState<EmployeeListItem[]>([])
  const [employeeId, setEmployeeId] = useState(searchParams.get('employeeId') ?? '')
  const [categoryId, setCategoryId] = useState<string | null>(null)
  const [availability, setAvailability] = useState<DriverAvailabilityStatus>('Available')
  const [notes, setNotes] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [submitting, setSubmitting] = useState(false)

  useEffect(() => {
    let mounted = true
    // Only employees without an existing driver profile can be linked.
    searchEmployees({ isActive: true, excludeDrivers: true, page: 1, pageSize: 200 })
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
        driverCategoryId: categoryId,
        availabilityStatus: availability,
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

        <FormField
          label="Medewerker"
          htmlFor="driver-employee"
          required
          hint="Alleen medewerkers zonder bestaand chauffeursprofiel; persoonsgegevens komen van het personeelsdossier."
        >
          <SearchableSelect
            id="driver-employee"
            value={employeeId || null}
            onChange={(v) => setEmployeeId(v ?? '')}
            options={employees.map((e) => ({
              value: e.id,
              label: `${e.firstName} ${e.lastName}`,
              description: e.employeeNumber,
              keywords: e.employeeNumber,
            }))}
            placeholder="— Selecteer een medewerker —"
            disabled={submitting}
          />
        </FormField>

        <FormField label="Chauffeurcategorie" htmlFor="driver-category">
          <LookupSelect
            id="driver-category"
            basePath="/api/driver-categories"
            managePermission="driver_categories.manage"
            singular="chauffeurcategorie"
            value={categoryId}
            onChange={setCategoryId}
            placeholder="— Geen —"
            disabled={submitting}
          />
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
