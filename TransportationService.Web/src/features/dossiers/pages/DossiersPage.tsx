import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { useNavigate } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { DataTable, type Column } from '../../../components/ui/DataTable'
import { EmptyState } from '../../../components/ui/EmptyState'
import { FilterBar } from '../../../components/ui/FilterBar'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { SearchableSelect } from '../../../components/ui/SearchableSelect'
import { ValidationSummary } from '../../../components/ui/ValidationSummary'
import { useToast } from '../../../components/ui/toastContext'
import { describeApiError, getFieldError, type FieldErrors } from '../../../api/problemDetails'
import { useAuth } from '../../auth/authContextValue'
import { searchCustomers } from '../../customers/api/customersApi'
import { createDossier, listDossiers } from '../api/dossiersApi'
import { DOSSIER_STATUS_LABELS, DOSSIER_STATUS_TONE, type DossierListItem, type DossierStatus } from '../types'

/** Transportdossiers: bundels van opdrachten, incidenten en gerelateerde dossiers. */
export function DossiersPage() {
  const navigate = useNavigate()
  const toast = useToast()
  const { hasPermission } = useAuth()
  const canManage = hasPermission('dossiers.manage')

  const [dossiers, setDossiers] = useState<DossierListItem[]>([])
  const [loaded, setLoaded] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [search, setSearch] = useState('')
  const [statusFilter, setStatusFilter] = useState<'' | DossierStatus>('')

  const [showCreate, setShowCreate] = useState(false)
  const [title, setTitle] = useState('')
  const [customerId, setCustomerId] = useState<string | null>(null)
  const [customerOptions, setCustomerOptions] = useState<{ value: string; label: string }[]>([])
  const [fieldErrors, setFieldErrors] = useState<FieldErrors>({})
  const [formError, setFormError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const reload = useCallback(() => {
    listDossiers({ search: search || undefined, status: statusFilter || undefined })
      .then((data) => {
        setDossiers(data)
        setError(null)
        setLoaded(true)
      })
      .catch(() => {
        setError('De dossiers konden niet worden geladen.')
        setLoaded(true)
      })
  }, [search, statusFilter])

  useEffect(() => {
    const timer = window.setTimeout(reload, 250)
    return () => window.clearTimeout(timer)
  }, [reload])

  useEffect(() => {
    if (!showCreate) return
    searchCustomers({ isActive: true, page: 1, pageSize: 200 })
      .then((result) => setCustomerOptions(result.items.map((c) => ({ value: c.id, label: c.name }))))
      .catch(() => setCustomerOptions([]))
  }, [showCreate])

  async function submitCreate(event: FormEvent) {
    event.preventDefault()
    setBusy(true)
    setFormError(null)
    setFieldErrors({})
    try {
      const created = await createDossier({
        title,
        description: null,
        customerId,
        responsibleUserId: null,
        notes: null,
      })
      toast.showSuccess(`Dossier ${created.dossierNumber} aangemaakt.`)
      navigate(`/dossiers/${created.id}`)
    } catch (err) {
      const described = describeApiError(err, 'Het dossier kon niet worden aangemaakt.')
      setFormError(described.message)
      setFieldErrors(described.fieldErrors)
    } finally {
      setBusy(false)
    }
  }

  const columns: Column<DossierListItem>[] = [
    { key: 'number', header: 'Nummer', render: (row) => <code>{row.dossierNumber}</code> },
    { key: 'title', header: 'Titel', render: (row) => row.title },
    { key: 'customer', header: 'Klant', render: (row) => row.customerName ?? '—' },
    { key: 'responsible', header: 'Verantwoordelijke', render: (row) => row.responsibleName ?? '—' },
    { key: 'orders', header: 'Opdrachten', render: (row) => String(row.orderCount) },
    {
      key: 'incidents',
      header: 'Open incidenten',
      render: (row) =>
        row.openIncidentCount > 0 ? <Badge tone="warning">{row.openIncidentCount}</Badge> : '0',
    },
    {
      key: 'status',
      header: 'Status',
      render: (row) => <Badge tone={DOSSIER_STATUS_TONE[row.status]}>{DOSSIER_STATUS_LABELS[row.status]}</Badge>,
    },
  ]

  return (
    <div>
      <PageHeader
        title="Transportdossiers"
        subtitle="Bundel opdrachten, incidenten en gerelateerde dossiers per zaak."
        action={canManage ? <Button onClick={() => setShowCreate(true)}>Nieuw dossier</Button> : undefined}
      />

      <FilterBar search={search} onSearchChange={setSearch} searchPlaceholder="Zoek op nummer of titel...">
        <select
          value={statusFilter}
          onChange={(event) => setStatusFilter(event.target.value as '' | DossierStatus)}
          aria-label="Status"
        >
          <option value="">Alle statussen</option>
          <option value="Open">Open</option>
          <option value="Closed">Gesloten</option>
        </select>
      </FilterBar>

      {error && <p className="placeholder-text">{error}</p>}
      {!error && loaded && dossiers.length === 0 && (
        <EmptyState message="Geen dossiers gevonden. Maak een dossier aan om opdrachten en incidenten te bundelen." />
      )}
      {!error && dossiers.length > 0 && (
        <DataTable columns={columns} rows={dossiers} rowKey={(row) => row.id} onRowClick={(row) => navigate(`/dossiers/${row.id}`)} />
      )}

      {showCreate && (
        <Modal
          title="Nieuw dossier"
          onClose={() => setShowCreate(false)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setShowCreate(false)} disabled={busy}>
                Annuleren
              </Button>
              <Button type="submit" form="new-dossier-form" disabled={busy}>
                Aanmaken
              </Button>
            </>
          }
        >
          <form id="new-dossier-form" onSubmit={(event) => void submitCreate(event)}>
            <ValidationSummary message={formError} fieldErrors={fieldErrors} />
            <FormField label="Titel" htmlFor="dossier-title" required error={getFieldError(fieldErrors, 'title')}>
              <input
                id="dossier-title"
                value={title}
                onChange={(event) => setTitle(event.target.value)}
                maxLength={200}
                autoFocus
              />
            </FormField>
            <FormField label="Klant" htmlFor="dossier-customer" error={getFieldError(fieldErrors, 'customerId')}>
              <SearchableSelect
                id="dossier-customer"
                value={customerId}
                onChange={setCustomerId}
                options={customerOptions}
                placeholder="— Geen klant —"
              />
            </FormField>
          </form>
        </Modal>
      )}
    </div>
  )
}
