import { useCallback, useEffect, useMemo, useState, type FormEvent, type ReactNode } from 'react'
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { LoadingState } from '../../../components/feedback/LoadingState'
import { ErrorState } from '../../../components/feedback/ErrorState'
import { isNotFoundError } from '../../../api/notFound'
import { ActionMenu, type ActionMenuItem } from '../../../components/ui/ActionMenu'
import { AttentionList, type AttentionItem } from '../../../components/ui/AttentionList'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { DetailCard, DetailGrid } from '../../../components/ui/DetailCard'
import { FactList } from '../../../components/ui/FactList'
import { FormActions } from '../../../components/ui/FormActions'
import { UnsavedChangesGuard } from '../../../components/ui/UnsavedChangesGuard'
import { FormField } from '../../../components/ui/FormField'
import { FormSection } from '../../../components/ui/FormSection'
import { Modal } from '../../../components/ui/Modal'
import { SearchableSelect, type SearchableSelectOption } from '../../../components/ui/SearchableSelect'
import { ValidationSummary } from '../../../components/ui/ValidationSummary'
import { useToast } from '../../../components/ui/toastContext'
import { getFieldError, localizeApiError, describeApiError, type FieldErrors } from '../../../api/problemDetails'
import { useAuth } from '../../auth/authContextValue'
import { useLocale } from '../../../i18n/localeContext'
import { AuditHistoryPanel } from '../../auditing/components/AuditHistoryPanel'
import { searchCustomers } from '../../customers/api/customersApi'
import { searchTransportOrders } from '../../transport-orders/api/transportOrdersApi'
import { getVehicleOptions } from '../../vehicles/api/vehiclesApi'
import { listDossiers } from '../../dossiers/api/dossiersApi'
import {
  changeIncidentStatus,
  createIncident,
  createIncidentDamageReport,
  getAssignableUsers,
  getIncident,
  updateIncident,
} from '../api/incidentsApi'
import { IncidentChargePanel } from '../components/IncidentChargePanel'
import { useIncidentAuditLabels } from '../incidentAuditLabels'
import {
  INCIDENT_SEVERITY_LABELS,
  INCIDENT_SEVERITY_TONE,
  INCIDENT_STATUS_LABELS,
  INCIDENT_STATUS_TONE,
  INCIDENT_TYPE_LABELS,
  RESPONSIBLE_PARTY_LABELS,
  DAMAGE_SEVERITIES,
  incidentTypeLabel,
  suggestedDamageSeverity,
  type IncidentDetail,
  type IncidentInput,
  type IncidentSeverity,
  type IncidentStatus,
  type IncidentType,
  type IncidentUpdate,
  type LinkedDamageSeverity,
} from '../types'
import { formatDate } from '../../../utils/dates'
import { formatCurrency } from '../../../utils/numbers'
import '../components/incidents.css'

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
  /** Wave 6 §1. */
  responsibleParty: string
  responsibilityNotes: string
  /** Klantgerichte tekst (de omschrijving blijft intern). */
  customerMessage: string
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
  responsibleParty: 'Unknown',
  responsibilityNotes: '',
  customerMessage: '',
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
    responsibleParty: incident.responsibleParty,
    responsibilityNotes: incident.responsibilityNotes ?? '',
    customerMessage: incident.customerMessage ?? '',
  }
}

function toInput(form: FormState, tripId: string | null): IncidentInput {
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
    responsibleParty: form.responsibleParty,
    responsibilityNotes: form.responsibilityNotes || null,
    tripId,
    customerMessage: form.customerMessage || null,
  }
}

/**
 * EX-01: only the edited card's section travels. Relations the form does not show (trip, driver,
 * trailer) are sent unchanged — and the server never lets an office edit clear or move them.
 */
function toUpdate(section: EditSection, form: FormState, incident: IncidentDetail): IncidentUpdate {
  const input = toInput(form, null)
  switch (section) {
    case 'report':
      return {
        report: {
          title: input.title, description: input.description, incidentType: input.incidentType, severity: input.severity,
          customTypeName: input.customTypeName, cause: input.cause, responsibleUserId: input.responsibleUserId, dueDate: input.dueDate,
        },
      }
    case 'links':
      return {
        links: {
          customerId: input.customerId, dossierId: input.dossierId, transportOrderId: input.transportOrderId, vehicleId: input.vehicleId,
          trailerId: incident.trailerId, tripId: incident.tripId, driverId: incident.driverId,
        },
      }
    case 'impact':
      return {
        impact: {
          customerImpact: input.customerImpact, operationalImpact: input.operationalImpact, financialImpact: input.financialImpact,
          estimatedCost: input.estimatedCost, actualCost: input.actualCost,
        },
      }
    case 'responsibility':
      return { responsibility: { responsibleParty: input.responsibleParty ?? 'Unknown', responsibilityNotes: input.responsibilityNotes ?? null } }
    case 'communication':
      return { communication: { customerMessage: input.customerMessage ?? null } }
  }
}

function stableJson(value: object): string {
  return JSON.stringify(value, Object.keys(value).sort())
}

/** Local calendar date as yyyy-mm-dd (the deadline is a date without time). */
function todayIso(): string {
  const now = new Date()
  return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-${String(now.getDate()).padStart(2, '0')}`
}

/** B24: the blocks a user edits one at a time; EX-01: a save sends only that block. */
type EditSection = 'report' | 'links' | 'impact' | 'responsibility' | 'communication'

const OPEN_STATUSES: IncidentStatus[] = ['New', 'InProgress']

/**
 * One incident. Existing incidents open READ-first (B24): who/what/where at a glance, linked
 * entities clickable, one sensible next status step in the header and the rest in Meer ▾.
 * [Bewerken] turns a single card into its form; leaving with unsaved edits asks first. A new
 * incident keeps the full registration form.
 */
export function IncidentDetailPage() {
  const { id } = useParams<{ id: string }>()
  const [searchParams] = useSearchParams()
  const navigate = useNavigate()
  const toast = useToast()
  const { hasPermission } = useAuth()
  const { t } = useLocale()
  const incidentAuditLabels = useIncidentAuditLabels()
  const canManage = hasPermission('incidents.manage')
  // The linked damage report is a problem-handler command (the fleet reports on the vehicle itself).
  const canCreateDamage = canManage
  const isNew = id === undefined
  const contextTripId = searchParams.get('tripId')

  const [incident, setIncident] = useState<IncidentDetail | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [loadNotFound, setLoadNotFound] = useState(false)
  const [form, setForm] = useState<FormState>({
    ...EMPTY_FORM,
    dossierId: searchParams.get('dossierId'),
    transportOrderId: searchParams.get('transportOrderId'),
    vehicleId: searchParams.get('vehicleId'),
  })
  const [fieldErrors, setFieldErrors] = useState<FieldErrors>({})
  const [formError, setFormError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [editing, setEditing] = useState<EditSection | null>(null)

  const [userOptions, setUserOptions] = useState<SearchableSelectOption[]>([])
  const [customerOptions, setCustomerOptions] = useState<SearchableSelectOption[]>([])
  const [dossierOptions, setDossierOptions] = useState<SearchableSelectOption[]>([])
  const [orderOptions, setOrderOptions] = useState<SearchableSelectOption[]>([])
  const [vehicleOptions, setVehicleOptions] = useState<SearchableSelectOption[]>([])

  const [resolveDialog, setResolveDialog] = useState(false)
  const [resolution, setResolution] = useState('')
  const [resolveError, setResolveError] = useState<string | null>(null)
  const [confirmCancel, setConfirmCancel] = useState(false)
  const [damageDialog, setDamageDialog] = useState(false)
  const [damageSeverity, setDamageSeverity] = useState<LinkedDamageSeverity>('Moderate')
  const [damageLocation, setDamageLocation] = useState('')
  const [damageError, setDamageError] = useState<string | null>(null)

  const load = useCallback(() => {
    if (!id) return
    getIncident(id)
      .then((data) => {
        setIncident(data)
        setForm(toForm(data))
        setLoadError(null)
        // Another incident opened in the same page instance: never carry an edit or dialog over.
        setEditing(null)
        setResolveDialog(false)
        setConfirmCancel(false)
        setDamageDialog(false)
      })
      .catch((err: unknown) => {
        setLoadNotFound(isNotFoundError(err))
        setLoadError(t('incidents.detail.loadError'))
      })
  }, [id, t])

  useEffect(() => {
    load()
  }, [load])

  // Reference data for the selects: only once a form is actually on screen (registering, or a
  // card in edit mode) — reading an incident never pays for five option lists.
  const needsUsers = isNew || editing === 'report'
  const needsLinks = isNew || editing === 'links'
  const [usersRequested, setUsersRequested] = useState(false)
  const [linksRequested, setLinksRequested] = useState(false)
  if (needsUsers && !usersRequested) setUsersRequested(true)
  if (needsLinks && !linksRequested) setLinksRequested(true)

  // EX-07: the responsible picker reads the dedicated, safe endpoint — no general users.view needed.
  useEffect(() => {
    if (!usersRequested) return
    getAssignableUsers()
      .then((users) =>
        setUserOptions(users.map((u) => ({ value: u.id, label: u.displayName, description: u.roleName ?? undefined }))),
      )
      .catch(() => setUserOptions([]))
  }, [usersRequested])

  useEffect(() => {
    if (!linksRequested) return
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
  }, [linksRequested])

  // The currently linked records stay selectable (and named) even before the option lists load.
  const linkedOptions = useMemo(() => {
    const own = (value: string | null, label: string | null): SearchableSelectOption[] =>
      value && label ? [{ value, label }] : []
    return {
      responsible: own(incident?.responsibleUserId ?? null, incident?.responsibleName ?? null),
      customer: own(incident?.customerId ?? null, incident?.customerName ?? null),
      dossier: own(incident?.dossierId ?? null, incident?.dossierNumber ?? null),
      order: own(incident?.transportOrderId ?? null, incident?.transportOrderNumber ?? null),
      vehicle: own(incident?.vehicleId ?? null, incident?.vehicleLabel ?? null),
    }
  }, [incident])

  if (loadError && loadNotFound) return <ErrorState message={t('incidents.detail.notFound')} backTo="/incidents" />
  if (loadError) return <ErrorState message={loadError} onRetry={load} backTo="/incidents" />
  if (!isNew && !incident) return <LoadingState message={t('incidents.detail.loading')} />

  const editable = canManage && (isNew || incident!.status !== 'Cancelled')
  // B05: compare against what was loaded (or the empty form), key order independent.
  const baselineForm: FormState = incident
    ? toForm(incident)
    : { ...EMPTY_FORM, dossierId: searchParams.get('dossierId'), transportOrderId: searchParams.get('transportOrderId'), vehicleId: searchParams.get('vehicleId') }
  const dirty = stableJson(form) !== stableJson(baselineForm)

  function set<K extends keyof FormState>(key: K, value: FormState[K]) {
    setForm((current) => ({ ...current, [key]: value }))
  }

  function withLinked(options: SearchableSelectOption[], linked: SearchableSelectOption[]): SearchableSelectOption[] {
    return linked.length > 0 && !options.some((o) => o.value === linked[0].value) ? [...linked, ...options] : options
  }

  function startEdit(section: EditSection) {
    if (incident) setForm(toForm(incident))
    setFormError(null)
    setFieldErrors({})
    setEditing(section)
  }

  function cancelEdit() {
    if (incident) setForm(toForm(incident))
    setFormError(null)
    setFieldErrors({})
    setEditing(null)
  }

  async function submit(event: FormEvent) {
    event.preventDefault()
    setBusy(true)
    setFormError(null)
    setFieldErrors({})
    try {
      if (isNew) {
        const created = await createIncident(toInput(form, contextTripId))
        toast.showSuccess(t('incidents.detail.created'))
        navigate(`/incidents/${created.id}`, { replace: true })
      } else {
        const updated = await updateIncident(id!, toUpdate(editing ?? 'report', form, incident!))
        setIncident(updated)
        setForm(toForm(updated))
        setEditing(null)
        toast.showSuccess(t('incidents.detail.updated'))
      }
    } catch (err) {
      const described = describeApiError(err, t('incidents.detail.saveFailed'))
      setFormError(localizeApiError(t, err, t('incidents.detail.saveFailed')))
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
      setConfirmCancel(false)
      toast.showSuccess(t('incidents.detail.statusChanged', { status: t(INCIDENT_STATUS_LABELS[status]) }))
    } catch (err) {
      const message = localizeApiError(t, err, t('incidents.detail.statusChangeFailed'))
      if (resolutionText !== undefined) {
        setResolveError(message)
      } else {
        toast.showError(message)
      }
    } finally {
      setBusy(false)
    }
  }

  function openResolve() {
    setResolution(incident!.resolution ?? '')
    setResolveError(null)
    setResolveDialog(true)
  }

  function submitResolve() {
    // Same rule as the server (resolution required): say so in the dialog, no round trip.
    if (!resolution.trim()) {
      setResolveError(t('incidents.detail.resolutionRequired'))
      return
    }
    void changeStatus('Resolved', resolution.trim())
  }

  function openDamage() {
    setDamageSeverity(suggestedDamageSeverity(incident!.severity))
    setDamageLocation('')
    setDamageError(null)
    setDamageDialog(true)
  }

  async function submitDamage() {
    setBusy(true)
    setDamageError(null)
    try {
      const updated = await createIncidentDamageReport(id!, { severity: damageSeverity, location: damageLocation.trim() || null })
      setIncident(updated)
      setForm(toForm(updated))
      setDamageDialog(false)
      toast.showSuccess(t('incidents.fleet.created'))
    } catch (err) {
      setDamageError(localizeApiError(t, err, t('incidents.fleet.createFailed')))
    } finally {
      setBusy(false)
    }
  }

  // ---- form fields per card (also used, all together, by the registration form) ----

  const reportFields = (
    <>
      <FormField label={t('incidents.detail.titleLabel')} htmlFor="inc-title" required error={getFieldError(fieldErrors, 'title')} className="inc-edit-wide">
        <input id="inc-title" value={form.title} onChange={(event) => set('title', event.target.value)} maxLength={200} disabled={busy} />
      </FormField>
      <FormField
        label={t('incidents.detail.descriptionLabel')}
        htmlFor="inc-description"
        required
        error={getFieldError(fieldErrors, 'description')}
        className="inc-edit-wide"
      >
        <textarea
          id="inc-description"
          value={form.description}
          onChange={(event) => set('description', event.target.value)}
          rows={4}
          maxLength={4000}
          disabled={busy}
        />
      </FormField>
      <FormField label={t('incidents.detail.typeLabel')} htmlFor="inc-type" required error={getFieldError(fieldErrors, 'incidentType')}>
        <select id="inc-type" value={form.incidentType} onChange={(event) => set('incidentType', event.target.value as IncidentType)} disabled={busy}>
          {Object.entries(INCIDENT_TYPE_LABELS).map(([value, labelKey]) => (
            <option key={value} value={value}>
              {t(labelKey)}
            </option>
          ))}
        </select>
      </FormField>
      {form.incidentType === 'Other' && (
        <FormField label={t('incidents.detail.customTypeLabel')} htmlFor="inc-custom-type" required error={getFieldError(fieldErrors, 'customTypeName')}>
          <input id="inc-custom-type" value={form.customTypeName} onChange={(event) => set('customTypeName', event.target.value)} maxLength={100} disabled={busy} />
        </FormField>
      )}
      <FormField label={t('incidents.detail.severityLabel')} htmlFor="inc-severity" required error={getFieldError(fieldErrors, 'severity')}>
        <select id="inc-severity" value={form.severity} onChange={(event) => set('severity', event.target.value as IncidentSeverity)} disabled={busy}>
          {Object.entries(INCIDENT_SEVERITY_LABELS).map(([value, labelKey]) => (
            <option key={value} value={value}>
              {t(labelKey)}
            </option>
          ))}
        </select>
      </FormField>
      <FormField label={t('incidents.detail.dueLabel')} htmlFor="inc-due" error={getFieldError(fieldErrors, 'dueDate')}>
        <input id="inc-due" type="date" value={form.dueDate} onChange={(event) => set('dueDate', event.target.value)} disabled={busy} />
      </FormField>
      <FormField label={t('incidents.detail.responsibleLabel')} htmlFor="inc-responsible" error={getFieldError(fieldErrors, 'responsibleUserId')}>
        <SearchableSelect
          id="inc-responsible"
          value={form.responsibleUserId}
          onChange={(value) => set('responsibleUserId', value)}
          options={withLinked(userOptions, linkedOptions.responsible)}
          disabled={busy}
        />
      </FormField>
      <FormField label={t('incidents.detail.causeLabel')} htmlFor="inc-cause" error={getFieldError(fieldErrors, 'cause')} className="inc-edit-wide">
        <textarea id="inc-cause" value={form.cause} onChange={(event) => set('cause', event.target.value)} rows={2} maxLength={2000} disabled={busy} />
      </FormField>
    </>
  )

  const responsibilityFields = (
    <>
      <FormField label={t('incidents.detail.responsiblePartyLabel')} htmlFor="inc-responsible-party" hint={t('incidents.detail.responsiblePartyHint')}>
        <select id="inc-responsible-party" value={form.responsibleParty} onChange={(event) => set('responsibleParty', event.target.value)} disabled={busy}>
          {Object.entries(RESPONSIBLE_PARTY_LABELS).map(([value, labelKey]) => (
            <option key={value} value={value}>
              {t(labelKey)}
            </option>
          ))}
        </select>
      </FormField>
      <FormField label={t('incidents.detail.responsibilityNotesLabel')} htmlFor="inc-responsibility-notes">
        <input
          id="inc-responsibility-notes"
          value={form.responsibilityNotes}
          onChange={(event) => set('responsibilityNotes', event.target.value)}
          maxLength={2000}
          disabled={busy}
        />
      </FormField>
    </>
  )

  const linkFields = (
    <>
      <FormField label={t('incidents.detail.customerLabel')} htmlFor="inc-customer" error={getFieldError(fieldErrors, 'customerId')}>
        <SearchableSelect
          id="inc-customer"
          value={form.customerId}
          onChange={(value) => set('customerId', value)}
          options={withLinked(customerOptions, linkedOptions.customer)}
          disabled={busy}
        />
      </FormField>
      <FormField label={t('incidents.detail.dossierLabel')} htmlFor="inc-dossier" error={getFieldError(fieldErrors, 'dossierId')}>
        <SearchableSelect
          id="inc-dossier"
          value={form.dossierId}
          onChange={(value) => set('dossierId', value)}
          options={withLinked(dossierOptions, linkedOptions.dossier)}
          disabled={busy}
        />
      </FormField>
      <FormField
        label={t('incidents.detail.orderLabel')}
        htmlFor="inc-order"
        error={getFieldError(fieldErrors, 'transportOrderId')}
        hint={form.transportOrderId && !form.dossierId ? t('incidents.detail.derivedFromOrder') : undefined}
      >
        <SearchableSelect
          id="inc-order"
          value={form.transportOrderId}
          onChange={(value) => set('transportOrderId', value)}
          options={withLinked(orderOptions, linkedOptions.order)}
          disabled={busy}
        />
      </FormField>
      <FormField label={t('incidents.detail.vehicleLabel')} htmlFor="inc-vehicle" error={getFieldError(fieldErrors, 'vehicleId')}>
        <SearchableSelect
          id="inc-vehicle"
          value={form.vehicleId}
          onChange={(value) => set('vehicleId', value)}
          options={withLinked(vehicleOptions, linkedOptions.vehicle)}
          disabled={busy}
        />
      </FormField>
      {!isNew && (incident?.tripNumber || incident?.driverName) && (
        <p className="inc-edit-wide inc-locked-links">
          {t('incidents.detail.executionLinksLocked', {
            trip: incident?.tripNumber ?? '—',
            driver: incident?.driverName ?? '—',
          })}
        </p>
      )}
    </>
  )

  const communicationFields = (
    <FormField
      label={t('incidents.detail.customerMessageLabel')}
      htmlFor="inc-customer-message"
      hint={t('incidents.detail.customerMessageHint')}
      error={getFieldError(fieldErrors, 'customerMessage')}
      className="inc-edit-wide"
    >
      <textarea
        id="inc-customer-message"
        value={form.customerMessage}
        onChange={(event) => set('customerMessage', event.target.value)}
        rows={3}
        maxLength={2000}
        disabled={busy}
      />
    </FormField>
  )

  const impactFields = (
    <>
      <FormField label={t('incidents.detail.customerImpactLabel')} htmlFor="inc-customer-impact" error={getFieldError(fieldErrors, 'customerImpact')} className="inc-edit-wide">
        <textarea id="inc-customer-impact" value={form.customerImpact} onChange={(event) => set('customerImpact', event.target.value)} rows={2} maxLength={1000} disabled={busy} />
      </FormField>
      <FormField label={t('incidents.detail.operationalImpactLabel')} htmlFor="inc-operational-impact" error={getFieldError(fieldErrors, 'operationalImpact')} className="inc-edit-wide">
        <textarea id="inc-operational-impact" value={form.operationalImpact} onChange={(event) => set('operationalImpact', event.target.value)} rows={2} maxLength={1000} disabled={busy} />
      </FormField>
      <FormField label={t('incidents.detail.financialImpactLabel')} htmlFor="inc-financial-impact" error={getFieldError(fieldErrors, 'financialImpact')} className="inc-edit-wide">
        <textarea id="inc-financial-impact" value={form.financialImpact} onChange={(event) => set('financialImpact', event.target.value)} rows={2} maxLength={1000} disabled={busy} />
      </FormField>
      <FormField label={t('incidents.detail.estimatedCostLabel')} htmlFor="inc-estimated" error={getFieldError(fieldErrors, 'estimatedCost')}>
        <input id="inc-estimated" type="number" min={0} step="0.01" value={form.estimatedCost} onChange={(event) => set('estimatedCost', event.target.value)} disabled={busy} />
      </FormField>
      <FormField label={t('incidents.detail.actualCostLabel')} htmlFor="inc-actual" error={getFieldError(fieldErrors, 'actualCost')}>
        <input id="inc-actual" type="number" min={0} step="0.01" value={form.actualCost} onChange={(event) => set('actualCost', event.target.value)} disabled={busy} />
      </FormField>
    </>
  )

  // ---- registration (new incident): the full form, unchanged flow ----
  if (isNew) {
    return (
      <div>
        <Breadcrumbs items={[{ label: t('incidents.list.title'), to: '/incidents' }, { label: t('incidents.detail.newTitle') }]} />
        <PageHeader title={t('incidents.detail.newTitle')} subtitle={t('incidents.detail.newSubtitle')} />
        <form onSubmit={(event) => void submit(event)}>
          <UnsavedChangesGuard when={editable && dirty && !busy} />
          <ValidationSummary message={formError} fieldErrors={fieldErrors} />
          <FormSection title={t('incidents.detail.sectionReport')}>
            {reportFields}
            {responsibilityFields}
          </FormSection>
          <FormSection title={t('incidents.detail.sectionLinks')}>{linkFields}</FormSection>
          <FormSection title={t('incidents.detail.sectionImpact')} collapsible defaultOpen={false}>
            {impactFields}
          </FormSection>
          <FormSection title={t('incidents.detail.sectionCommunication')} collapsible defaultOpen={false}>
            {communicationFields}
          </FormSection>
          {editable && (
            <FormActions>
              <Button type="submit" disabled={busy}>
                {t('incidents.detail.register')}
              </Button>
            </FormActions>
          )}
        </form>
      </div>
    )
  }

  const current = incident!
  const isOpen = OPEN_STATUSES.includes(current.status)
  const overdue = isOpen && current.dueDate !== null && current.dueDate < todayIso()
  const canEditCards = editable && editing === null

  // ---- status actions: one sensible next step, the rest in Meer ▾ ----
  const allowed = canManage ? current.allowedStatusChanges : []
  let nextAction: ReactNode = null
  const menuItems: ActionMenuItem[] = []
  if (allowed.includes('InProgress') && current.status === 'New') {
    nextAction = (
      <Button disabled={busy || editing !== null} onClick={() => void changeStatus('InProgress')}>
        {t('incidents.detail.startAction')}
      </Button>
    )
  } else if (allowed.includes('Resolved') && current.status === 'InProgress') {
    // The resolution is the natural next step but asks for a note: a calm secondary button,
    // never the loudest thing on the page.
    nextAction = (
      <Button variant="secondary" disabled={busy || editing !== null} onClick={openResolve}>
        {t('incidents.detail.resolveAction')}
      </Button>
    )
  }
  if (allowed.includes('Resolved') && current.status !== 'InProgress') {
    menuItems.push({ key: 'resolve', label: t('incidents.detail.resolveAction'), onSelect: openResolve })
  }
  if (allowed.includes('InProgress') && current.status === 'Resolved') {
    menuItems.push({ key: 'reopen', label: t('incidents.detail.reopenAction'), onSelect: () => void changeStatus('InProgress') })
  }
  if (allowed.includes('New') && current.status === 'Cancelled') {
    menuItems.push({ key: 'reopen-new', label: t('incidents.detail.reopenAction'), onSelect: () => void changeStatus('New') })
  }
  if (allowed.includes('Cancelled')) {
    menuItems.push({ key: 'cancel', label: t('incidents.detail.cancelAction'), danger: true, onSelect: () => setConfirmCancel(true) })
  }

  // ---- attention: only what needs someone to act ----
  const attention: AttentionItem[] = []
  if (overdue) {
    attention.push({
      key: 'overdue',
      severity: 'critical',
      text: t('incidents.detail.attention.overdue', { date: formatDate(current.dueDate) }),
    })
  }
  if (isOpen && !current.responsibleUserId) {
    attention.push({
      key: 'responsible',
      severity: 'info',
      text: t('incidents.detail.attention.noResponsible'),
      action: canEditCards ? { label: t('incidents.detail.attention.assign'), onSelect: () => startEdit('report') } : undefined,
    })
  }
  if (current.chargeDecision === 'Proposed') {
    attention.push({
      key: 'charge',
      severity: 'warning',
      text: t('incidents.detail.attention.chargeProposed', { amount: formatCurrency(current.chargeAmount) }),
      action: { label: t('incidents.detail.attention.view'), onSelect: () => scrollToCard('incident-charge') },
    })
  }
  if (current.vehicleSafetyIssue && !current.damageReportId && current.status !== 'Cancelled') {
    attention.push({
      key: 'fleet',
      severity: 'critical',
      text: t('incidents.detail.attention.vehicleSafety', { vehicle: current.vehicleLabel ?? current.trailerLabel ?? '—' }),
      action: canCreateDamage && current.canCreateDamageReport ? { label: t('incidents.fleet.create'), onSelect: openDamage } : undefined,
    })
  }
  if ((current.confirmedDossierNumbers ?? []).length > 0) {
    attention.push({
      key: 'confirmed-dossier',
      severity: 'warning',
      text: t('incidents.detail.attention.afterConfirmation', { dossiers: (current.confirmedDossierNumbers ?? []).join(', ') }),
    })
  }
  if (current.status !== 'Cancelled' && current.redeliverySuggested && !current.linkedRedeliveryOrderId) {
    attention.push({
      key: 'redelivery',
      severity: 'warning',
      text: t('incidents.detail.attention.redelivery'),
      action: { label: t('incidents.detail.attention.view'), onSelect: () => scrollToCard('incident-redelivery') },
    })
  }

  const subtitleParts = [
    incidentTypeLabel(current, t),
    t('incidents.detail.reportedOn', { date: formatDate(current.createdAt) }),
    current.source && current.source !== 'Office' ? t(`incidents.detail.source.${current.source}`) : null,
    current.resolvedAt ? t('incidents.detail.resolvedOn', { date: formatDate(current.resolvedAt) }) : null,
  ].filter(Boolean)

  function editButton(section: EditSection) {
    if (!editable) return undefined
    return (
      <Button variant="ghost" onClick={() => startEdit(section)} disabled={!canEditCards || busy}>
        {t('ui.actions.edit')}
      </Button>
    )
  }

  function editForm(fields: ReactNode) {
    return (
      <form className="inc-edit" onSubmit={(event) => void submit(event)}>
        <ValidationSummary message={formError} fieldErrors={fieldErrors} />
        <div className="inc-edit-grid">{fields}</div>
        <div className="inc-edit-actions">
          <Button variant="secondary" onClick={cancelEdit} disabled={busy}>
            {t('incidents.detail.cancel')}
          </Button>
          <Button type="submit" disabled={busy || !dirty}>
            {t('incidents.detail.saveChanges')}
          </Button>
        </div>
      </form>
    )
  }

  // Numbers (DOS-, ORD-, RIT-, plates) read as identifiers; names stay ordinary links.
  const link = (to: string, label: string | null, identifier = true) =>
    label ? <Link className={identifier ? 'ui-id-link' : undefined} to={to}>{label}</Link> : null
  const impactEmpty =
    !current.customerImpact && !current.operationalImpact && !current.financialImpact && current.estimatedCost === null && current.actualCost === null

  return (
    <div>
      <Breadcrumbs items={[{ label: t('incidents.list.title'), to: '/incidents' }, { label: current.title }]} />
      <PageHeader
        title={current.title}
        badges={
          <>
            <Badge tone={INCIDENT_STATUS_TONE[current.status]}>{t(INCIDENT_STATUS_LABELS[current.status])}</Badge>
            <Badge tone={INCIDENT_SEVERITY_TONE[current.severity]}>{t(INCIDENT_SEVERITY_LABELS[current.severity])}</Badge>
          </>
        }
        subtitle={subtitleParts.join(' · ')}
        action={
          nextAction || menuItems.length > 0 ? (
            <>
              <ActionMenu items={menuItems} disabled={busy || editing !== null} />
              {nextAction}
            </>
          ) : undefined
        }
      />

      <UnsavedChangesGuard when={editing !== null && dirty && !busy} />
      <AttentionList items={attention} />

      {current.status === 'Resolved' && current.resolution && (
        <DetailCard title={t('incidents.detail.resolutionTitle')} id="incident-resolution" className="inc-resolution-card">
          <p className="inc-description">{current.resolution}</p>
          <p className="ui-detail-empty">
            {t('incidents.detail.resolvedBy', {
              name: current.resolvedByName ?? t('incidents.detail.unknownActor'),
              date: formatDate(current.resolvedAt),
            })}
          </p>
        </DetailCard>
      )}

      <DetailGrid>
        <DetailCard title={t('incidents.detail.sectionReport')} id="incident-report" action={editing === 'report' ? undefined : editButton('report')}>
          {editing === 'report' ? (
            editForm(reportFields)
          ) : (
            <>
              <p className="inc-description">{current.description}</p>
              <FactList
                facts={[
                  { key: 'type', label: t('incidents.detail.typeLabel'), value: incidentTypeLabel(current, t) },
                  { key: 'severity', label: t('incidents.detail.severityLabel'), value: t(INCIDENT_SEVERITY_LABELS[current.severity]) },
                  {
                    key: 'due',
                    label: t('incidents.detail.dueLabel'),
                    value: current.dueDate ? (
                      <span className={overdue ? 'inc-overdue' : undefined}>
                        {formatDate(current.dueDate)}
                        {overdue && ` · ${t('incidents.list.overdueSuffix')}`}
                      </span>
                    ) : null,
                  },
                  { key: 'responsible', label: t('incidents.detail.responsibleLabel'), value: current.responsibleName },
                  { key: 'cause', label: t('incidents.detail.causeLabel'), value: current.cause ? <span className="inc-multiline">{current.cause}</span> : null },
                  // Reopened: the earlier resolution stays readable as context.
                  current.status !== 'Resolved' &&
                    !!current.resolution && {
                      key: 'previous-resolution',
                      label: t('incidents.detail.previousResolution'),
                      value: <span className="inc-multiline">{current.resolution}</span>,
                    },
                ]}
              />
            </>
          )}
        </DetailCard>

        <DetailCard title={t('incidents.detail.sectionLinks')} id="incident-links" action={editing === 'links' ? undefined : editButton('links')}>
          {editing === 'links' ? (
            editForm(linkFields)
          ) : (
            <FactList
              facts={[
                { key: 'customer', label: t('incidents.detail.customerLabel'), value: current.customerId ? link(`/customers/${current.customerId}`, current.customerName, false) : null },
                { key: 'dossier', label: t('incidents.detail.dossierLabel'), value: current.dossierId ? link(`/dossiers/${current.dossierId}`, current.dossierNumber) : null },
                {
                  key: 'order',
                  label: t('incidents.detail.orderLabel'),
                  value: current.transportOrderId ? link(`/transport-orders/${current.transportOrderId}`, current.transportOrderNumber) : null,
                },
                !!current.tripId && { key: 'trip', label: t('incidents.detail.tripLabel'), value: link(`/planning/${current.tripId}`, current.tripNumber) },
                { key: 'vehicle', label: t('incidents.detail.vehicleLabel'), value: current.vehicleId ? link(`/vehicles/${current.vehicleId}`, current.vehicleLabel) : null },
                !!current.trailerId && { key: 'trailer', label: t('incidents.detail.trailerLabel'), value: link(`/trailers/${current.trailerId}`, current.trailerLabel) },
                !!current.driverName && { key: 'driver', label: t('incidents.detail.driverLabel'), value: current.driverId ? link(`/drivers/${current.driverId}`, current.driverName, false) : current.driverName },
                !!current.sourceExceptionId && {
                  key: 'source-exception',
                  label: t('incidents.detail.sourceExceptionLabel'),
                  value: link(`/exceptions/${current.sourceExceptionId}`, current.sourceExceptionLabel ?? t('incidents.detail.sourceExceptionFallback'), false),
                },
              ]}
            />
          )}
        </DetailCard>

        <DetailCard
          title={t('incidents.charge.title')}
          id="incident-charge"
          action={editing === 'responsibility' ? undefined : editButton('responsibility')}
        >
          {editing === 'responsibility' ? (
            editForm(responsibilityFields)
          ) : (
            <IncidentChargePanel part="charge" incident={current} onUpdated={(updated) => { setIncident(updated); setForm(toForm(updated)) }} />
          )}
        </DetailCard>

        <DetailCard title={t('incidents.charge.redeliveryTitle')} id="incident-redelivery">
          <IncidentChargePanel part="redelivery" incident={current} onUpdated={(updated) => { setIncident(updated); setForm(toForm(updated)) }} />
        </DetailCard>

        <DetailCard title={t('incidents.detail.sectionImpact')} id="incident-impact" action={editing === 'impact' ? undefined : editButton('impact')}>
          {editing === 'impact' ? (
            editForm(impactFields)
          ) : impactEmpty ? (
            <p className="ui-detail-empty">{t('incidents.detail.noImpact')}</p>
          ) : (
            <FactList
              facts={[
                { key: 'ci', label: t('incidents.detail.customerImpactLabel'), value: current.customerImpact ? <span className="inc-multiline">{current.customerImpact}</span> : null },
                { key: 'oi', label: t('incidents.detail.operationalImpactLabel'), value: current.operationalImpact ? <span className="inc-multiline">{current.operationalImpact}</span> : null },
                { key: 'fi', label: t('incidents.detail.financialImpactLabel'), value: current.financialImpact ? <span className="inc-multiline">{current.financialImpact}</span> : null },
                { key: 'ec', label: t('incidents.detail.estimatedCostShort'), value: current.estimatedCost !== null ? formatCurrency(current.estimatedCost) : null },
                { key: 'ac', label: t('incidents.detail.actualCostShort'), value: current.actualCost !== null ? formatCurrency(current.actualCost) : null },
              ]}
            />
          )}
        </DetailCard>
        <DetailCard
          title={t('incidents.detail.sectionCommunication')}
          id="incident-communication"
          action={editing === 'communication' ? undefined : editButton('communication')}
        >
          {editing === 'communication' ? (
            editForm(communicationFields)
          ) : current.customerMessage ? (
            <p className="inc-description">{current.customerMessage}</p>
          ) : (
            <p className="ui-detail-empty">{t('incidents.detail.noCustomerMessage')}</p>
          )}
        </DetailCard>

        {(current.vehicleId || current.trailerId || current.damageReportId) && (
          <DetailCard title={t('incidents.fleet.title')} id="incident-fleet">
            {current.damageReportId ? (
              <FactList
                facts={[
                  {
                    key: 'damage',
                    label: t('incidents.fleet.damageReport'),
                    value: current.vehicleId
                      ? <Link to={`/vehicles/${current.vehicleId}?tab=schade`}>{t('incidents.fleet.openDamage')}</Link>
                      : t('incidents.fleet.registered'),
                  },
                  { key: 'severity', label: t('incidents.fleet.severity'), value: current.damageReportSeverity ? t(`incidents.fleet.damageSeverity.${current.damageReportSeverity}`) : null },
                  { key: 'status', label: t('incidents.fleet.status'), value: current.damageReportStatus ? t(`incidents.fleet.damageStatus.${current.damageReportStatus}`) : null },
                ]}
              />
            ) : (
              <>
                <p className="ui-detail-empty">{t('incidents.fleet.noDamage')}</p>
                {canCreateDamage && current.canCreateDamageReport && (
                  <Button variant={current.vehicleSafetyIssue ? 'primary' : 'secondary'} onClick={openDamage} disabled={busy || editing !== null}>
                    {t('incidents.fleet.create')}
                  </Button>
                )}
              </>
            )}
          </DetailCard>
        )}
      </DetailGrid>

      <DetailCard title={t('incidents.detail.historyTitle')} id="incident-history">
        {/* Re-read after every in-page change, so the new step appears without a reload. Domain
            history: readable by everyone who may see the incident, financial fields redacted. */}
        <AuditHistoryPanel
          key={historyKey(current)}
          entityType="Incident"
          entityId={current.id}
          valueLabels={incidentAuditLabels}
          historyPath={`/api/incidents/${current.id}/history`}
        />
      </DetailCard>

      {damageDialog && (
        <Modal
          title={t('incidents.fleet.dialogTitle')}
          onClose={() => setDamageDialog(false)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setDamageDialog(false)} disabled={busy}>
                {t('incidents.detail.cancel')}
              </Button>
              <Button disabled={busy} onClick={() => void submitDamage()}>
                {t('incidents.fleet.create')}
              </Button>
            </>
          }
        >
          <ValidationSummary message={damageError} />
          <p>{t('incidents.fleet.dialogIntro', { vehicle: current.vehicleLabel ?? current.trailerLabel ?? '—', trip: current.tripNumber ?? '—' })}</p>
          <FormField label={t('incidents.fleet.severity')} htmlFor="inc-damage-severity" required hint={t('incidents.fleet.severityHint')}>
            <select id="inc-damage-severity" value={damageSeverity} onChange={(event) => setDamageSeverity(event.target.value as LinkedDamageSeverity)}>
              {DAMAGE_SEVERITIES.map((value) => (
                <option key={value} value={value}>{t(`incidents.fleet.damageSeverity.${value}`)}</option>
              ))}
            </select>
          </FormField>
          <FormField label={t('incidents.fleet.location')} htmlFor="inc-damage-location">
            <input id="inc-damage-location" value={damageLocation} onChange={(event) => setDamageLocation(event.target.value)} maxLength={300} />
          </FormField>
        </Modal>
      )}

      {confirmCancel && (
        <ConfirmDialog
          title={t('incidents.detail.cancelTitle')}
          message={t('incidents.detail.cancelMessage', { title: current.title })}
          confirmLabel={t('incidents.detail.cancelConfirm')}
          destructive
          busy={busy}
          onConfirm={() => void changeStatus('Cancelled')}
          onCancel={() => setConfirmCancel(false)}
        />
      )}

      {resolveDialog && (
        <Modal
          title={t('incidents.detail.resolveTitle')}
          onClose={() => setResolveDialog(false)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setResolveDialog(false)} disabled={busy}>
                {t('incidents.detail.cancel')}
              </Button>
              <Button disabled={busy} onClick={submitResolve}>
                {t('incidents.detail.resolve')}
              </Button>
            </>
          }
        >
          <ValidationSummary message={resolveError} />
          <FormField label={t('incidents.detail.resolutionLabel')} htmlFor="inc-resolution" required hint={t('incidents.detail.resolutionHint')}>
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

function historyKey(incident: IncidentDetail): string {
  return stableJson(incident)
}

function scrollToCard(id: string) {
  document.getElementById(id)?.scrollIntoView({ behavior: 'smooth', block: 'start' })
}
