import { useEffect, useState, type FormEvent } from 'react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { FormField } from '../../../components/ui/FormField'
import { Button } from '../../../components/ui/Button'
import { SearchableSelect } from '../../../components/ui/SearchableSelect'
import { useToast } from '../../../components/ui/toastContext'
import { ApiError } from '../../../api/apiClient'
import { useLocale } from '../../../i18n/localeContext'
import { LookupSelect } from '../../master-data/components/LookupSelect'
import { searchEmployees } from '../../employees/api/employeesApi'
import type { EmployeeListItem } from '../../employees/types/employee'
import { createDriver } from '../api/driversApi'
import { AVAILABILITY_LABELS, type DriverAvailabilityStatus } from '../types'
import './driver-detail.css'

export function NewDriverPage() {
  const navigate = useNavigate()
  const [searchParams] = useSearchParams()
  const { t } = useLocale()
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
        if (mounted) showError(t('driversAdmin.newPage.employeesLoadFailed'))
      })
    return () => {
      mounted = false
    }
  }, [showError, t])

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setError(null)
    if (!employeeId) {
      setError(t('driversAdmin.newPage.selectEmployee'))
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
      showSuccess(t('driversAdmin.newPage.created', { number: driver.driverNumber }))
      navigate(`/drivers/${driver.id}`)
    } catch (err) {
      const message =
        err instanceof ApiError && err.status === 409
          ? t('driversAdmin.newPage.duplicate')
          : t('driversAdmin.newPage.createFailed')
      setError(message)
      setSubmitting(false)
    }
  }

  return (
    <div>
      <Breadcrumbs items={[{ label: t('driversAdmin.newPage.breadcrumb'), to: '/drivers' }, { label: t('fleet.common.new') }]} />
      <PageHeader title={t('driversAdmin.newPage.title')} />
      <form className="driver-form" onSubmit={handleSubmit} noValidate>
        {error && (
          <div className="driver-form-error" role="alert">
            {error}
          </div>
        )}

        <FormField
          label={t('driversAdmin.newPage.employee')}
          htmlFor="driver-employee"
          required
          hint={t('driversAdmin.newPage.employeeHint')}
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
            placeholder={t('driversAdmin.newPage.employeePlaceholder')}
            disabled={submitting}
          />
        </FormField>

        <FormField label={t('driversAdmin.newPage.category')} htmlFor="driver-category">
          <LookupSelect
            id="driver-category"
            basePath="/api/driver-categories"
            managePermission="driver_categories.manage"
            singular="masterData.singular.driver-categories"
            value={categoryId}
            onChange={setCategoryId}
            placeholder={t('fleet.form.none')}
            disabled={submitting}
          />
        </FormField>

        <FormField label={t('driversAdmin.fields.availability')} htmlFor="driver-availability">
          <select
            id="driver-availability"
            value={availability}
            onChange={(e) => setAvailability(e.target.value as DriverAvailabilityStatus)}
            disabled={submitting}
          >
            {(Object.keys(AVAILABILITY_LABELS) as DriverAvailabilityStatus[]).map((status) => (
              <option key={status} value={status}>
                {t(AVAILABILITY_LABELS[status])}
              </option>
            ))}
          </select>
        </FormField>

        <FormField label={t('driversAdmin.fields.notes')} htmlFor="driver-notes">
          <textarea id="driver-notes" rows={3} value={notes} onChange={(e) => setNotes(e.target.value)} disabled={submitting} />
        </FormField>

        <div className="driver-form-actions">
          <Button type="button" variant="secondary" onClick={() => navigate('/drivers')} disabled={submitting}>
            {t('ui.actions.cancel')}
          </Button>
          <Button type="submit" disabled={submitting}>
            {submitting ? t('fleet.common.busy') : t('driversAdmin.newPage.create')}
          </Button>
        </div>
      </form>
    </div>
  )
}
