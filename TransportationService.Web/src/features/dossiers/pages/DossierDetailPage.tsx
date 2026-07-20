import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { LoadingState } from '../../../components/feedback/LoadingState'
import { ErrorState } from '../../../components/feedback/ErrorState'
import { BackButton } from '../../../components/ui/BackButton'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { FormField } from '../../../components/ui/FormField'
import { FormSection } from '../../../components/ui/FormSection'
import { Modal } from '../../../components/ui/Modal'
import { SearchableSelect, type SearchableSelectOption } from '../../../components/ui/SearchableSelect'
import { ValidationSummary } from '../../../components/ui/ValidationSummary'
import { useToast } from '../../../components/ui/toastContext'
import { describeApiError, getFieldError, type FieldErrors } from '../../../api/problemDetails'
import { addRecentItem } from '../../../hooks/recentItems'
import { useAuth } from '../../auth/authContextValue'
import { euro } from '../../invoices/types'
import { searchCustomers } from '../../customers/api/customersApi'
import { searchTransportOrders } from '../../transport-orders/api/transportOrdersApi'
import { getUsers } from '../../users/api/usersApi'
import {
  INCIDENT_SEVERITY_LABELS,
  INCIDENT_SEVERITY_TONE,
  INCIDENT_STATUS_LABELS,
  INCIDENT_STATUS_TONE,
  INCIDENT_TYPE_LABELS,
  type IncidentSeverity,
  type IncidentStatus,
  type IncidentType,
} from '../../incidents/types'
import {
  addDossierRelation,
  closeDossier,
  getDossier,
  linkDossierOrder,
  listDossiers,
  removeDossierRelation,
  reopenDossier,
  unlinkDossierOrder,
  updateDossier,
} from '../api/dossiersApi'
import {
  DOSSIER_RELATION_LABELS,
  DOSSIER_STATUS_LABELS,
  DOSSIER_STATUS_TONE,
  type DossierDetail,
  type DossierRelationType,
} from '../types'
import '../../dashboard/pages/dashboard.css'
import './dossiers.css'

/** Detail van één transportdossier: gegevens, opdrachten, relaties, incidenten en financieel. */
export function DossierDetailPage() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const toast = useToast()
  const { hasPermission } = useAuth()
  const canManage = hasPermission('dossiers.manage')

  const [dossier, setDossier] = useState<DossierDetail | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)

  // Edit form
  const [editing, setEditing] = useState(false)
  const [title, setTitle] = useState('')
  const [description, setDescription] = useState('')
  const [notes, setNotes] = useState('')
  const [customerId, setCustomerId] = useState<string | null>(null)
  const [responsibleUserId, setResponsibleUserId] = useState<string | null>(null)
  const [customerOptions, setCustomerOptions] = useState<SearchableSelectOption[]>([])
  const [userOptions, setUserOptions] = useState<SearchableSelectOption[]>([])
  const [fieldErrors, setFieldErrors] = useState<FieldErrors>({})
  const [formError, setFormError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  // Link dialogs
  const [showLinkOrder, setShowLinkOrder] = useState(false)
  const [orderOptions, setOrderOptions] = useState<SearchableSelectOption[]>([])
  const [orderToLink, setOrderToLink] = useState<string | null>(null)
  const [showAddRelation, setShowAddRelation] = useState(false)
  const [relationTarget, setRelationTarget] = useState<string | null>(null)
  const [relationType, setRelationType] = useState<DossierRelationType>('FollowUp')
  const [relationNotes, setRelationNotes] = useState('')
  const [dossierOptions, setDossierOptions] = useState<SearchableSelectOption[]>([])
  const [dialogError, setDialogError] = useState<string | null>(null)
  const [confirmClose, setConfirmClose] = useState(false)

  const load = useCallback(() => {
    if (!id) return
    getDossier(id)
      .then((data) => {
        setDossier(data)
        setLoadError(null)
        addRecentItem({
          category: 'Dossiers',
          title: `${data.dossierNumber} · ${data.title}`,
          route: `/dossiers/${data.id}`,
        })
      })
      .catch(() => setLoadError('Het dossier kon niet worden geladen.'))
  }, [id])

  useEffect(() => {
    load()
  }, [load])

  useEffect(() => {
    if (!editing) return
    searchCustomers({ isActive: true, page: 1, pageSize: 200 })
      .then((result) => setCustomerOptions(result.items.map((c) => ({ value: c.id, label: c.name }))))
      .catch(() => setCustomerOptions([]))
    getUsers()
      .then((users) =>
        setUserOptions(
          users
            .filter((u) => u.isActive)
            .map((u) => ({ value: u.id, label: `${u.firstName} ${u.lastName}` })),
        ),
      )
      .catch(() => setUserOptions([]))
  }, [editing])

  useEffect(() => {
    if (!showLinkOrder || !dossier) return
    searchTransportOrders({ page: 1, pageSize: 100, customerId: dossier.customerId ?? undefined })
      .then((result) => {
        const linked = new Set(dossier.orders.map((o) => o.orderId))
        setOrderOptions(
          result.items
            .filter((o) => !linked.has(o.id))
            .map((o) => ({ value: o.id, label: o.orderNumber, description: o.customerName })),
        )
      })
      .catch(() => setOrderOptions([]))
  }, [showLinkOrder, dossier])

  useEffect(() => {
    if (!showAddRelation || !dossier) return
    listDossiers()
      .then((all) =>
        setDossierOptions(
          all
            .filter((d) => d.id !== dossier.id)
            .map((d) => ({ value: d.id, label: d.dossierNumber, description: d.title })),
        ),
      )
      .catch(() => setDossierOptions([]))
  }, [showAddRelation, dossier])

  if (loadError) return <ErrorState message={loadError} />
  if (!dossier || !id) return <LoadingState message="Dossier laden..." />

  const isOpen = dossier.status === 'Open'

  function startEdit() {
    if (!dossier) return
    setTitle(dossier.title)
    setDescription(dossier.description ?? '')
    setNotes(dossier.notes ?? '')
    setCustomerId(dossier.customerId)
    setResponsibleUserId(dossier.responsibleUserId)
    setFieldErrors({})
    setFormError(null)
    setEditing(true)
  }

  async function run(action: () => Promise<DossierDetail>, successMessage: string) {
    setBusy(true)
    setDialogError(null)
    try {
      const updated = await action()
      setDossier(updated)
      toast.showSuccess(successMessage)
      return true
    } catch (err) {
      const described = describeApiError(err, 'De actie is niet gelukt.')
      setDialogError(described.message)
      toast.showError(described.message)
      return false
    } finally {
      setBusy(false)
    }
  }

  async function submitEdit(event: FormEvent) {
    event.preventDefault()
    setBusy(true)
    setFormError(null)
    setFieldErrors({})
    try {
      const updated = await updateDossier(id!, {
        title,
        description: description || null,
        customerId,
        responsibleUserId,
        notes: notes || null,
      })
      setDossier(updated)
      setEditing(false)
      toast.showSuccess('Dossier bijgewerkt.')
    } catch (err) {
      const described = describeApiError(err, 'Het dossier kon niet worden bijgewerkt.')
      setFormError(described.message)
      setFieldErrors(described.fieldErrors)
    } finally {
      setBusy(false)
    }
  }

  return (
    <div>
      <BackButton to="/dossiers" label="Terug naar dossiers" />
      <PageHeader
        title={`${dossier.dossierNumber} · ${dossier.title}`}
        subtitle={dossier.customerName ?? 'Geen klant gekoppeld'}
        action={
          canManage ? (
            <span style={{ display: 'inline-flex', gap: '0.5rem' }}>
              {isOpen && (
                <Button variant="secondary" onClick={startEdit}>
                  Bewerken
                </Button>
              )}
              {isOpen ? (
                <Button variant="secondary" onClick={() => setConfirmClose(true)}>
                  Dossier sluiten
                </Button>
              ) : (
                <Button variant="secondary" onClick={() => void run(() => reopenDossier(id), 'Dossier heropend.')}>
                  Heropenen
                </Button>
              )}
            </span>
          ) : undefined
        }
      />

      <p>
        <Badge tone={DOSSIER_STATUS_TONE[dossier.status]}>{DOSSIER_STATUS_LABELS[dossier.status]}</Badge>{' '}
        {dossier.responsibleName && <>Verantwoordelijke: {dossier.responsibleName} · </>}
        Aangemaakt op {dossier.createdAt.slice(0, 10)}
        {dossier.closedAt && <> · Gesloten op {dossier.closedAt.slice(0, 10)}</>}
      </p>
      {dossier.description && <p>{dossier.description}</p>}
      {dossier.notes && <p className="placeholder-text">Notities: {dossier.notes}</p>}

      <FormSection title="Financieel overzicht">
        <div className="db-kpis">
          <div className="db-kpi">
            <span className="db-kpi-label">Afgesproken omzet</span>
            <span className="db-kpi-value">{euro(dossier.financials.agreedOrderTotal)}</span>
          </div>
          <div className="db-kpi">
            <span className="db-kpi-label">Gefactureerd</span>
            <span className="db-kpi-value">{euro(dossier.financials.invoicedTotal)}</span>
          </div>
          <div className="db-kpi">
            <span className="db-kpi-label">Geschatte incidentkost</span>
            <span className="db-kpi-value">{euro(dossier.financials.estimatedIncidentCost)}</span>
          </div>
          <div className="db-kpi">
            <span className="db-kpi-label">Werkelijke incidentkost</span>
            <span className="db-kpi-value">{euro(dossier.financials.actualIncidentCost)}</span>
          </div>
        </div>
      </FormSection>

      <FormSection title={`Gekoppelde opdrachten (${dossier.orders.length})`}>
        {canManage && isOpen && (
          <p>
            <Button variant="secondary" onClick={() => { setOrderToLink(null); setDialogError(null); setShowLinkOrder(true) }}>
              Opdracht koppelen
            </Button>
          </p>
        )}
        {dossier.orders.length === 0 && <p className="placeholder-text">Nog geen opdrachten gekoppeld.</p>}
        {dossier.orders.length > 0 && (
          <ul className="db-list">
            {dossier.orders.map((order) => (
              <li key={order.linkId}>
                <span className="db-row">
                  <button type="button" className="link-button" onClick={() => navigate(`/transport-orders/${order.orderId}`)}>
                    <code>{order.orderNumber}</code>
                  </button>
                  <span className="db-row-main" title={order.goodsDescription ?? undefined}>
                    {order.orderDate} · {order.status}
                    {order.agreedPrice !== null && <> · {euro(order.agreedPrice)}</>}
                  </span>
                  {canManage && isOpen && (
                    <Button
                      variant="secondary"
                      onClick={() => void run(() => unlinkDossierOrder(id, order.orderId), 'Opdracht ontkoppeld.')}
                      disabled={busy}
                    >
                      Ontkoppelen
                    </Button>
                  )}
                </span>
              </li>
            ))}
          </ul>
        )}
      </FormSection>

      <FormSection title={`Gerelateerde dossiers (${dossier.relations.length})`}>
        {canManage && (
          <p>
            <Button
              variant="secondary"
              onClick={() => { setRelationTarget(null); setRelationNotes(''); setDialogError(null); setShowAddRelation(true) }}
            >
              Relatie toevoegen
            </Button>
          </p>
        )}
        {dossier.relations.length === 0 && <p className="placeholder-text">Geen gerelateerde dossiers.</p>}
        {dossier.relations.length > 0 && (
          <ul className="db-list">
            {dossier.relations.map((relation) => (
              <li key={relation.id}>
                <span className="db-row">
                  <Badge tone="info">
                    {DOSSIER_RELATION_LABELS[relation.relationType]}
                    {relation.isOutgoing ? ' →' : ' ←'}
                  </Badge>
                  <button
                    type="button"
                    className="link-button"
                    onClick={() => navigate(`/dossiers/${relation.otherDossierId}`)}
                  >
                    <code>{relation.otherDossierNumber}</code> {relation.otherDossierTitle}
                  </button>
                  <span className="db-row-main">{relation.notes ?? ''}</span>
                  {canManage && (
                    <Button
                      variant="secondary"
                      onClick={() => void run(() => removeDossierRelation(id, relation.id), 'Relatie verwijderd.')}
                      disabled={busy}
                    >
                      Verwijderen
                    </Button>
                  )}
                </span>
              </li>
            ))}
          </ul>
        )}
      </FormSection>

      <FormSection title={`Incidenten (${dossier.incidents.length})`}>
        {hasPermission('incidents.manage') && (
          <p>
            <Button variant="secondary" onClick={() => navigate(`/incidents/new?dossierId=${dossier.id}`)}>
              Incident registreren
            </Button>
          </p>
        )}
        {dossier.incidents.length === 0 && <p className="placeholder-text">Geen incidenten in dit dossier.</p>}
        {dossier.incidents.length > 0 && (
          <ul className="db-list">
            {dossier.incidents.map((incident) => (
              <li key={incident.id}>
                <button type="button" className="db-row" onClick={() => navigate(`/incidents/${incident.id}`)}>
                  <span className="db-row-main">{incident.title}</span>
                  <span className="db-row-meta">
                    {INCIDENT_TYPE_LABELS[incident.incidentType as IncidentType] ?? incident.incidentType}
                    <Badge tone={INCIDENT_SEVERITY_TONE[incident.severity as IncidentSeverity] ?? 'neutral'}>
                      {INCIDENT_SEVERITY_LABELS[incident.severity as IncidentSeverity] ?? incident.severity}
                    </Badge>
                    <Badge tone={INCIDENT_STATUS_TONE[incident.status as IncidentStatus] ?? 'neutral'}>
                      {INCIDENT_STATUS_LABELS[incident.status as IncidentStatus] ?? incident.status}
                    </Badge>
                  </span>
                </button>
              </li>
            ))}
          </ul>
        )}
      </FormSection>

      {editing && (
        <Modal
          title="Dossier bewerken"
          onClose={() => setEditing(false)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setEditing(false)} disabled={busy}>
                Annuleren
              </Button>
              <Button type="submit" form="edit-dossier-form" disabled={busy}>
                Opslaan
              </Button>
            </>
          }
        >
          <form id="edit-dossier-form" onSubmit={(event) => void submitEdit(event)}>
            <ValidationSummary message={formError} fieldErrors={fieldErrors} />
            <FormField label="Titel" htmlFor="edit-title" required error={getFieldError(fieldErrors, 'title')}>
              <input id="edit-title" value={title} onChange={(event) => setTitle(event.target.value)} maxLength={200} />
            </FormField>
            <FormField label="Omschrijving" htmlFor="edit-description" error={getFieldError(fieldErrors, 'description')}>
              <textarea
                id="edit-description"
                value={description}
                onChange={(event) => setDescription(event.target.value)}
                rows={3}
                maxLength={2000}
              />
            </FormField>
            <FormField label="Klant" htmlFor="edit-customer" error={getFieldError(fieldErrors, 'customerId')}>
              <SearchableSelect id="edit-customer" value={customerId} onChange={setCustomerId} options={customerOptions} />
            </FormField>
            <FormField
              label="Verantwoordelijke"
              htmlFor="edit-responsible"
              error={getFieldError(fieldErrors, 'responsibleUserId')}
            >
              <SearchableSelect
                id="edit-responsible"
                value={responsibleUserId}
                onChange={setResponsibleUserId}
                options={userOptions}
              />
            </FormField>
            <FormField label="Notities" htmlFor="edit-notes" error={getFieldError(fieldErrors, 'notes')}>
              <textarea id="edit-notes" value={notes} onChange={(event) => setNotes(event.target.value)} rows={3} maxLength={4000} />
            </FormField>
          </form>
        </Modal>
      )}

      {showLinkOrder && (
        <Modal
          title="Opdracht koppelen"
          onClose={() => setShowLinkOrder(false)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setShowLinkOrder(false)} disabled={busy}>
                Annuleren
              </Button>
              <Button
                disabled={busy || !orderToLink}
                onClick={() =>
                  void run(() => linkDossierOrder(id, orderToLink!), 'Opdracht gekoppeld.').then((ok) => {
                    if (ok) setShowLinkOrder(false)
                  })
                }
              >
                Koppelen
              </Button>
            </>
          }
        >
          <ValidationSummary message={dialogError} />
          <FormField label="Transportopdracht" htmlFor="link-order" required>
            <SearchableSelect
              id="link-order"
              value={orderToLink}
              onChange={setOrderToLink}
              options={orderOptions}
              emptyMessage="Geen (verdere) opdrachten gevonden"
            />
          </FormField>
          {dossier.customerId && (
            <p className="placeholder-text">Gefilterd op de klant van dit dossier.</p>
          )}
        </Modal>
      )}

      {showAddRelation && (
        <Modal
          title="Dossierrelatie toevoegen"
          onClose={() => setShowAddRelation(false)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setShowAddRelation(false)} disabled={busy}>
                Annuleren
              </Button>
              <Button
                disabled={busy || !relationTarget}
                onClick={() =>
                  void run(
                    () => addDossierRelation(id, { targetDossierId: relationTarget!, relationType, notes: relationNotes || null }),
                    'Relatie toegevoegd.',
                  ).then((ok) => {
                    if (ok) setShowAddRelation(false)
                  })
                }
              >
                Toevoegen
              </Button>
            </>
          }
        >
          <ValidationSummary message={dialogError} />
          <FormField label="Dossier" htmlFor="relation-target" required>
            <SearchableSelect
              id="relation-target"
              value={relationTarget}
              onChange={setRelationTarget}
              options={dossierOptions}
            />
          </FormField>
          <FormField label="Relatietype" htmlFor="relation-type" required>
            <select
              id="relation-type"
              value={relationType}
              onChange={(event) => setRelationType(event.target.value as DossierRelationType)}
            >
              {Object.entries(DOSSIER_RELATION_LABELS).map(([value, label]) => (
                <option key={value} value={value}>
                  {label}
                </option>
              ))}
            </select>
          </FormField>
          <FormField label="Notitie" htmlFor="relation-notes">
            <input
              id="relation-notes"
              value={relationNotes}
              onChange={(event) => setRelationNotes(event.target.value)}
              maxLength={1000}
            />
          </FormField>
        </Modal>
      )}

      {confirmClose && (
        <ConfirmDialog
          title="Dossier sluiten"
          message="Weet je zeker dat je dit dossier wil sluiten? Gekoppelde gegevens blijven bewaard; open incidenten blokkeren het sluiten."
          confirmLabel="Sluiten"
          busy={busy}
          onCancel={() => setConfirmClose(false)}
          onConfirm={() =>
            void run(() => closeDossier(id), 'Dossier gesloten.').then((ok) => {
              if (ok) setConfirmClose(false)
            })
          }
        />
      )}
    </div>
  )
}
