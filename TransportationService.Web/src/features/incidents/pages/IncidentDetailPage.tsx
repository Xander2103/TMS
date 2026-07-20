import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { LoadingState } from '../../../components/feedback/LoadingState'
import { ErrorState } from '../../../components/feedback/ErrorState'
import { BackButton } from '../../../components/ui/BackButton'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { FormActions } from '../../../components/ui/FormActions'
import { FormField } from '../../../components/ui/FormField'
import { FormSection } from '../../../components/ui/FormSection'
import { Modal } from '../../../components/ui/Modal'
import { SearchableSelect, type SearchableSelectOption } from '../../../components/ui/SearchableSelect'
import { ValidationSummary } from '../../../components/ui/ValidationSummary'
import { useToast } from '../../../components/ui/toastContext'
import { describeApiError, getFieldError, type FieldErrors } from '../../../api/problemDetails'
import { useAuth } from '../../auth/authContextValue'
import { searchCustomers } from '../../customers/api/customersApi'
import { searchTransportOrders } from '../../transport-orders/api/transportOrdersApi'
import { getUsers } from '../../users/api/usersApi'
import { getVehicleOptions } from '../../vehicles/api/vehiclesApi'
import { listDossiers } from '../../dossiers/api/dossiersApi'
import { changeIncidentStatus, createIncident, getIncident, updateIncident } from '../api/incidentsApi'
import {
  INCIDENT_SEVERITY_LABELS,
  INCIDENT_STATUS_LABELS,
  INCIDENT_STATUS_TONE,
  INCIDENT_TYPE_LABELS,
  type IncidentDetail,
  type IncidentInput,
  type IncidentSeverity,
  type IncidentStatus,
  type IncidentType,
} from '../types'

interface FormState {
  title: string
  description: string
  incidentType: IncidentType
  customTypeName: string
  severity: IncidentSeverity
  cause: string
  dueDate: string
  responsibleUserId: string | null
  customerId: string | null
  dossierId: string | null
  transportOrderId: string | null
  vehicleId: string | null
  customerImpact: string
  operationalImpact: string
  financialImpact: string
  estimatedCost: string
  actualCost: string
}

const EMPTY_FORM: FormState = {
  title: '',
  description: '',
  incidentType: 'Damage',
  customTypeName: '',
  severity: 'Medium',
  cause: '',
  dueDate: '',
  responsibleUserId: null,
  customerId: null,
  dossierId: null,
  transportOrderId: null,
  vehicleId: null,
  customerImpact: '',
  operationalImpact: '',
  financialImpact: '',
  estimatedCost: '',
  actualCost: '',
}

function toForm(incident: IncidentDetail): FormState {
  return {
    title: incident.title,
    description: incident.description,
    incidentType: incident.incidentType,
    customTypeName: incident.customTypeName ?? '',
    severity: incident.severity,
    cause: incident.cause ?? '',
    dueDate: incident.dueDate ?? '',
    responsibleUserId: incident.responsibleUserId,
    customerId: incident.customerId,
    dossierId: incident.dossierId,
    transportOrderId: incident.transportOrderId,
    vehicleId: incident.vehicleId,
    customerImpact: incident.customerImpact ?? '',
    operationalImpact: incident.operationalImpact ?? '',
    financialImpact: incident.financialImpact ?? '',
    estimatedCost: incident.estimatedCost !== null ? String(incident.estimatedCost) : '',
    actualCost: incident.actualCost !== null ? String(incident.actualCost) : '',
  }
}

function toInput(form: FormState): IncidentInput {
  return {
    title: form.title,
    description: form.description,
    incidentType: form.incidentType,
    severity: form.severity,
    customTypeName: form.customTypeName || null,
    cause: form.cause || null,
    responsibleUserId: form.responsibleUserId,
    customerImpact: form.customerImpact || null,
    operationalImpact: form.operationalImpact || null,
    financialImpact: form.financialImpact || null,
    estimatedCost: form.estimatedCost === '' ? null : Number(form.estimatedCost),
    actualCost: form.actualCost === '' ? null : Number(form.actualCost),
    customerId: form.customerId,
    transportOrderId: form.transportOrderId,
    dossierId: form.dossierId,
    vehicleId: form.vehicleId,
    dueDate: form.dueDate || null,
  }
}

/** Eén incident: registreren (nieuw), bijwerken en de status opvolgen tot afhandeling. */
export function IncidentDetailPage() {
  const { id } = useParams<{ id: string }>()
  const [searchParams] = useSearchParams()
  const navigate = useNavigate()
  const toast = useToast()
  const { hasPermission } = useAuth()
  const canManage = hasPermission('incidents.manage')
  const isNew = id === undefined

  const [incident, setIncident] = useState<IncidentDetail | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [form, setForm] = useState<FormState>({ ...EMPTY_FORM, dossierId: searchParams.get('dossierId') })
  const [fieldErrors, setFieldErrors] = useState<FieldErrors>({})
  const [formError, setFormError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const [userOptions, setUserOptions] = useState<SearchableSelectOption[]>([])
  const [customerOptions, setCustomerOptions] = useState<SearchableSelectOption[]>([])
  const [dossierOptions, setDossierOptions] = useState<SearchableSelectOption[]>([])
  const [orderOptions, setOrderOptions] = useState<SearchableSelectOption[]>([])
  const [vehicleOptions, setVehicleOptions] = useState<SearchableSelectOption[]>([])

  const [resolveDialog, setResolveDialog] = useState(false)
  const [resolution, setResolution] = useState('')
  const [resolveError, setResolveError] = useState<string | null>(null)

  const load = useCallback(() => {
    if (!id) return
    getIncident(id)
      .then((data) => {
        setIncident(data)
        setForm(toForm(data))
        setLoadError(null)
      })
      .catch(() => setLoadError('Het incident kon niet worden geladen.'))
  }, [id])

  useEffect(() => {
    load()
  }, [load])

  // Reference data for the link selects (loaded once; the lists are tenant-scoped and modest).
  useEffect(() => {
    getUsers()
      .then((users) =>
        setUserOptions(users.filter((u) => u.isActive).map((u) => ({ value: u.id, label: `${u.firstName} ${u.lastName}` }))),
      )
      .catch(() => setUserOptions([]))
    searchCustomers({ page: 1, pageSize: 200 })
      .then((result) => setCustomerOptions(result.items.map((c) => ({ value: c.id, label: c.name }))))
      .catch(() => setCustomerOptions([]))
    listDossiers()
      .then((all) => setDossierOptions(all.map((d) => ({ value: d.id, label: d.dossierNumber, description: d.title }))))
      .catch(() => setDossierOptions([]))
    searchTransportOrders({ page: 1, pageSize: 100 })
      .then((result) =>
        setOrderOptions(result.items.map((o) => ({ value: o.id, label: o.orderNumber, description: o.customerName }))),
      )
      .catch(() => setOrderOptions([]))
    getVehicleOptions()
      .then((vehicles) =>
        setVehicleOptions(vehicles.map((v) => ({ value: v.id, label: `${v.internalNumber} · ${v.licensePlate}` }))),
      )
      .catch(() => setVehicleOptions([]))
  }, [])

  if (loadError) return <ErrorState message={loadError} />
  if (!isNew && !incident) return <LoadingState message="Incident laden..." />

  const editable = canManage && (isNew || incident!.status !== 'Cancelled')

  function set<K extends keyof FormState>(key: K, value: FormState[K]) {
    setForm((current) => ({ ...current, [key]: value }))
  }

  async function submit(event: FormEvent) {
    event.preventDefault()
    setBusy(true)
    setFormError(null)
    setFieldErrors({})
    try {
      if (isNew) {
        const created = await createIncident(toInput(form))
        toast.showSuccess('Incident geregistreerd.')
        navigate(`/incidents/${created.id}`, { replace: true })
      } else {
        const updated = await updateIncident(id!, toInput(form))
        setIncident(updated)
        setForm(toForm(updated))
        toast.showSuccess('Incident bijgewerkt.')
      }
    } catch (err) {
      const described = describeApiError(err, 'Het incident kon niet worden opgeslagen.')
      setFormError(described.message)
      setFieldErrors(described.fieldErrors)
    } finally {
      setBusy(false)
    }
  }

  async function changeStatus(status: IncidentStatus, resolutionText?: string) {
    setBusy(true)
    setResolveError(null)
    try {
      const updated = await changeIncidentStatus(id!, { status, resolution: resolutionText ?? null })
      setIncident(updated)
      setForm(toForm(updated))
      setResolveDialog(false)
      toast.showSuccess(`Status gewijzigd naar ${INCIDENT_STATUS_LABELS[status]}.`)
    } catch (err) {
      const described = describeApiError(err, 'De status kon niet worden gewijzigd.')
      if (resolutionText !== undefined) {
        setResolveError(described.message)
      } else {
        toast.showError(described.message)
      }
    } finally {
      setBusy(false)
    }
  }

  const statusActions = !isNew && canManage
    ? incident!.allowedStatusChanges.map((status) => (
        <Button
          key={status}
          variant="secondary"
          disabled={busy}
          onClick={() => {
            if (status === 'Resolved') {
              setResolution(incident!.resolution ?? '')
              setResolveError(null)
              setResolveDialog(true)
            } else {
              void changeStatus(status)
            }
          }}
        >
          {status === 'Resolved' ? 'Afhandelen...' : `Naar ${INCIDENT_STATUS_LABELS[status].toLowerCase()}`}
        </Button>
      ))
    : null

  return (
    <div>
      <BackButton to="/incidents" label="Terug naar incidenten" />
      <PageHeader
        title={isNew ? 'Nieuw incident' : incident!.title}
        subtitle={isNew ? 'Registreer een schade, vertraging of andere melding.' : undefined}
        action={statusActions ? <span style={{ display: 'inline-flex', gap: '0.5rem' }}>{statusActions}</span> : undefined}
      />

      {!isNew && (
        <p>
          <Badge tone={INCIDENT_STATUS_TONE[incident!.status]}>{INCIDENT_STATUS_LABELS[incident!.status]}</Badge>{' '}
          Gemeld op {incident!.createdAt.slice(0, 10)}
          {incident!.resolvedAt && <> · Afgehandeld op {incident!.resolvedAt.slice(0, 10)}</>}
        </p>
      )}
      {!isNew && incident!.resolution && (
        <FormSection title="Oplossing">
          <p>{incident!.resolution}</p>
        </FormSection>
      )}

      <form onSubmit={(event) => void submit(event)}>
        <ValidationSummary message={formError} fieldErrors={fieldErrors} />

        <FormSection title="Melding">
          <FormField label="Titel" htmlFor="inc-title" required error={getFieldError(fieldErrors, 'title')}>
            <input
              id="inc-title"
              value={form.title}
              onChange={(event) => set('title', event.target.value)}
              maxLength={200}
              disabled={!editable}
            />
          </FormField>
          <FormField label="Omschrijving" htmlFor="inc-description" required error={getFieldError(fieldErrors, 'description')}>
            <textarea
              id="inc-description"
              value={form.description}
              onChange={(event) => set('description', event.target.value)}
              rows={4}
              maxLength={4000}
              disabled={!editable}
            />
          </FormField>
          <FormField label="Type" htmlFor="inc-type" required error={getFieldError(fieldErrors, 'incidentType')}>
            <select
              id="inc-type"
              value={form.incidentType}
              onChange={(event) => set('incidentType', event.target.value as IncidentType)}
              disabled={!editable}
            >
              {Object.entries(INCIDENT_TYPE_LABELS).map(([value, label]) => (
                <option key={value} value={value}>
                  {label}
                </option>
              ))}
            </select>
          </FormField>
          {form.incidentType === 'Other' && (
            <FormField
              label="Typenaam"
              htmlFor="inc-custom-type"
              required
              error={getFieldError(fieldErrors, 'customTypeName')}
            >
              <input
                id="inc-custom-type"
                value={form.customTypeName}
                onChange={(event) => set('customTypeName', event.target.value)}
                maxLength={100}
                disabled={!editable}
              />
            </FormField>
          )}
          <FormField label="Ernst" htmlFor="inc-severity" required error={getFieldError(fieldErrors, 'severity')}>
            <select
              id="inc-severity"
              value={form.severity}
              onChange={(event) => set('severity', event.target.value as IncidentSeverity)}
              disabled={!editable}
            >
              {Object.entries(INCIDENT_SEVERITY_LABELS).map(([value, label]) => (
                <option key={value} value={value}>
                  {label}
                </option>
              ))}
            </select>
          </FormField>
          <FormField label="Oorzaak" htmlFor="inc-cause" error={getFieldError(fieldErrors, 'cause')}>
            <textarea
              id="inc-cause"
              value={form.cause}
              onChange={(event) => set('cause', event.target.value)}
              rows={2}
              maxLength={2000}
              disabled={!editable}
            />
          </FormField>
          <FormField label="Deadline" htmlFor="inc-due" error={getFieldError(fieldErrors, 'dueDate')}>
            <input
              id="inc-due"
              type="date"
              value={form.dueDate}
              onChange={(event) => set('dueDate', event.target.value)}
              disabled={!editable}
            />
          </FormField>
          <FormField
            label="Verantwoordelijke"
            htmlFor="inc-responsible"
            error={getFieldError(fieldErrors, 'responsibleUserId')}
          >
            <SearchableSelect
              id="inc-responsible"
              value={form.responsibleUserId}
              onChange={(value) => set('responsibleUserId', value)}
              options={userOptions}
              disabled={!editable}
            />
          </FormField>
        </FormSection>

        <FormSection title="Koppelingen" collapsible defaultOpen={isNew}>
          <FormField label="Klant" htmlFor="inc-customer" error={getFieldError(fieldErrors, 'customerId')}>
            <SearchableSelect
              id="inc-customer"
              value={form.customerId}
              onChange={(value) => set('customerId', value)}
              options={customerOptions}
              disabled={!editable}
            />
          </FormField>
          <FormField label="Dossier" htmlFor="inc-dossier" error={getFieldError(fieldErrors, 'dossierId')}>
            <SearchableSelect
              id="inc-dossier"
              value={form.dossierId}
              onChange={(value) => set('dossierId', value)}
              options={dossierOptions}
              disabled={!editable}
            />
          </FormField>
          <FormField
            label="Transportopdracht"
            htmlFor="inc-order"
            error={getFieldError(fieldErrors, 'transportOrderId')}
          >
            <SearchableSelect
              id="inc-order"
              value={form.transportOrderId}
              onChange={(value) => set('transportOrderId', value)}
              options={orderOptions}
              disabled={!editable}
            />
          </FormField>
          <FormField label="Voertuig" htmlFor="inc-vehicle" error={getFieldError(fieldErrors, 'vehicleId')}>
            <SearchableSelect
              id="inc-vehicle"
              value={form.vehicleId}
              onChange={(value) => set('vehicleId', value)}
              options={vehicleOptions}
              disabled={!editable}
            />
          </FormField>
          {!isNew && incident!.driverName && <p className="placeholder-text">Chauffeur: {incident!.driverName}</p>}
          {!isNew && incident!.tripNumber && <p className="placeholder-text">Rit: {incident!.tripNumber}</p>}
        </FormSection>

        <FormSection title="Impact en kosten" collapsible defaultOpen={false}>
          <FormField label="Impact op de klant" htmlFor="inc-customer-impact" error={getFieldError(fieldErrors, 'customerImpact')}>
            <textarea
              id="inc-customer-impact"
              value={form.customerImpact}
              onChange={(event) => set('customerImpact', event.target.value)}
              rows={2}
              maxLength={1000}
              disabled={!editable}
            />
          </FormField>
          <FormField
            label="Operationele impact"
            htmlFor="inc-operational-impact"
            error={getFieldError(fieldErrors, 'operationalImpact')}
          >
            <textarea
              id="inc-operational-impact"
              value={form.operationalImpact}
              onChange={(event) => set('operationalImpact', event.target.value)}
              rows={2}
              maxLength={1000}
              disabled={!editable}
            />
          </FormField>
          <FormField
            label="Financiële impact"
            htmlFor="inc-financial-impact"
            error={getFieldError(fieldErrors, 'financialImpact')}
          >
            <textarea
              id="inc-financial-impact"
              value={form.financialImpact}
              onChange={(event) => set('financialImpact', event.target.value)}
              rows={2}
              maxLength={1000}
              disabled={!editable}
            />
          </FormField>
          <FormField label="Geschatte kost (€)" htmlFor="inc-estimated" error={getFieldError(fieldErrors, 'estimatedCost')}>
            <input
              id="inc-estimated"
              type="number"
              min={0}
              step="0.01"
              value={form.estimatedCost}
              onChange={(event) => set('estimatedCost', event.target.value)}
              disabled={!editable}
            />
          </FormField>
          <FormField label="Werkelijke kost (€)" htmlFor="inc-actual" error={getFieldError(fieldErrors, 'actualCost')}>
            <input
              id="inc-actual"
              type="number"
              min={0}
              step="0.01"
              value={form.actualCost}
              onChange={(event) => set('actualCost', event.target.value)}
              disabled={!editable}
            />
          </FormField>
        </FormSection>

        {editable && (
          <FormActions>
            <Button type="submit" disabled={busy}>
              {isNew ? 'Incident registreren' : 'Wijzigingen opslaan'}
            </Button>
          </FormActions>
        )}
      </form>

      {resolveDialog && (
        <Modal
          title="Incident afhandelen"
          onClose={() => setResolveDialog(false)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setResolveDialog(false)} disabled={busy}>
                Annuleren
              </Button>
              <Button disabled={busy} onClick={() => void changeStatus('Resolved', resolution)}>
                Afhandelen
              </Button>
            </>
          }
        >
          <ValidationSummary message={resolveError} />
          <FormField label="Oplossing" htmlFor="inc-resolution" required>
            <textarea
              id="inc-resolution"
              value={resolution}
              onChange={(event) => setResolution(event.target.value)}
              rows={4}
              maxLength={4000}
              autoFocus
            />
          </FormField>
        </Modal>
      )}
    </div>
  )
}
