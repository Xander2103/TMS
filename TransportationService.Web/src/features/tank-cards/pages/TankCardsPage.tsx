import { useEffect, useState, type FormEvent } from 'react'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { DataTable, type Column } from '../../../components/ui/DataTable'
import { FilterBar } from '../../../components/ui/FilterBar'
import { Pagination } from '../../../components/ui/Pagination'
import { Badge, type BadgeTone } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { useToast } from '../../../components/ui/toastContext'
import { ApiError } from '../../../api/apiClient'
import { usePagedQuery } from '../../../hooks/usePagedQuery'
import { useAuth } from '../../auth/authContextValue'
import { EmployeeSelect } from '../../tasks/components/EmployeePicker'
import { getVehicleOptions } from '../../vehicles/api/vehiclesApi'
import type { VehicleOption } from '../../vehicles/types'
import {
  createTankCard,
  deleteTankCard,
  searchTankCards,
  setTankCardBlocked,
  updateTankCard,
} from '../api/tankCardsApi'
import {
  maskCardNumber,
  TANK_CARD_STATUS_LABELS,
  TANK_CARD_STATUSES,
  type TankCard,
  type TankCardInput,
  type TankCardStatus,
} from '../types'
import { formatDate } from '../../../utils/dates'
import './tank-cards.css'

const STATUS_TONE: Record<TankCardStatus, BadgeTone> = {
  Active: 'success',
  ExpiringSoon: 'warning',
  Expired: 'danger',
  Blocked: 'danger',
}

interface CardForm {
  cardNumber: string
  provider: string
  vehicleId: string
  employeeId: string
  validFrom: string
  validUntil: string
  internalName: string
  fuelType: string
  dailyLimit: string
  weeklyLimit: string
  monthlyLimit: string
  costCenter: string
  notes: string
}

const EMPTY_FORM: CardForm = {
  cardNumber: '',
  provider: '',
  vehicleId: '',
  employeeId: '',
  validFrom: '',
  validUntil: '',
  internalName: '',
  fuelType: '',
  dailyLimit: '',
  weeklyLimit: '',
  monthlyLimit: '',
  costCenter: '',
  notes: '',
}

/** Empty-string form field -> null; otherwise the parsed number. Used for the optional limits. */
function parseOptionalNumber(value: string): number | null {
  const trimmed = value.trim()
  if (trimmed === '') return null
  const n = Number(trimmed)
  return Number.isFinite(n) ? n : null
}

export function TankCardsPage() {
  const { hasPermission } = useAuth()
  const { showSuccess, showError } = useToast()

  const [search, setSearch] = useState('')
  const [statusFilter, setStatusFilter] = useState<TankCardStatus | ''>('')
  const [page, setPage] = useState(1)

  const { items, totalCount, pageSize, isLoading, error, reload } = usePagedQuery<TankCard>(
    (args) => searchTankCards({ ...args, status: statusFilter || undefined }),
    { search, page, errorMessage: 'Tankkaarten konden niet worden geladen.' },
  )

  // The status filter isn't part of usePagedQuery's own dependency key, so trigger a reload
  // explicitly whenever it changes.
  useEffect(() => {
    reload()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [statusFilter])

  const [vehicles, setVehicles] = useState<VehicleOption[]>([])

  useEffect(() => {
    let mounted = true
    getVehicleOptions()
      .then((data) => {
        if (mounted) setVehicles(data)
      })
      .catch(() => {})
    return () => {
      mounted = false
    }
  }, [])

  const [editorOpen, setEditorOpen] = useState(false)
  const [editing, setEditing] = useState<TankCard | null>(null)
  const [form, setForm] = useState<CardForm>(EMPTY_FORM)
  const [formError, setFormError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)

  const [blockTarget, setBlockTarget] = useState<TankCard | null>(null)
  const [blockReason, setBlockReason] = useState('')
  const [deleteTarget, setDeleteTarget] = useState<TankCard | null>(null)

  function set<K extends keyof CardForm>(key: K, value: CardForm[K]) {
    setForm((f) => ({ ...f, [key]: value }))
  }

  function openCreate() {
    setEditing(null)
    setForm(EMPTY_FORM)
    setFormError(null)
    setEditorOpen(true)
  }

  function openEdit(card: TankCard) {
    setEditing(card)
    setForm({
      cardNumber: card.cardNumber,
      provider: card.provider,
      vehicleId: card.vehicleId ?? '',
      employeeId: card.employeeId ?? '',
      validFrom: card.validFrom ?? '',
      validUntil: card.validUntil ?? '',
      internalName: card.internalName ?? '',
      fuelType: card.fuelType ?? '',
      dailyLimit: card.dailyLimit != null ? String(card.dailyLimit) : '',
      weeklyLimit: card.weeklyLimit != null ? String(card.weeklyLimit) : '',
      monthlyLimit: card.monthlyLimit != null ? String(card.monthlyLimit) : '',
      costCenter: card.costCenter ?? '',
      notes: card.notes ?? '',
    })
    setFormError(null)
    setEditorOpen(true)
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setFormError(null)
    if (!form.cardNumber.trim()) {
      setFormError('Kaartnummer is verplicht.')
      return
    }
    if (!form.provider.trim()) {
      setFormError('Leverancier is verplicht.')
      return
    }
    const input: TankCardInput = {
      cardNumber: form.cardNumber.trim(),
      provider: form.provider.trim(),
      vehicleId: form.vehicleId || null,
      employeeId: form.employeeId || null,
      validFrom: form.validFrom || null,
      validUntil: form.validUntil || null,
      internalName: form.internalName.trim() || null,
      fuelType: form.fuelType.trim() || null,
      dailyLimit: parseOptionalNumber(form.dailyLimit),
      weeklyLimit: parseOptionalNumber(form.weeklyLimit),
      monthlyLimit: parseOptionalNumber(form.monthlyLimit),
      costCenter: form.costCenter.trim() || null,
      notes: form.notes.trim() || null,
    }
    setSaving(true)
    try {
      if (editing) {
        await updateTankCard(editing.id, input)
        showSuccess('Tankkaart bijgewerkt.')
      } else {
        await createTankCard(input)
        showSuccess('Tankkaart aangemaakt.')
      }
      setEditorOpen(false)
      reload()
    } catch (err) {
      setFormError(
        err instanceof ApiError && err.status === 409
          ? 'Er bestaat al een tankkaart met dit kaartnummer.'
          : 'De tankkaart kon niet worden opgeslagen.',
      )
    } finally {
      setSaving(false)
    }
  }

  async function handleBlockConfirm(event: FormEvent) {
    event.preventDefault()
    if (!blockTarget) return
    setSaving(true)
    try {
      const willBlock = !blockTarget.isBlocked
      await setTankCardBlocked(blockTarget.id, willBlock, willBlock ? blockReason.trim() || null : null)
      showSuccess(willBlock ? 'Tankkaart geblokkeerd.' : 'Tankkaart gedeblokkeerd.')
      setBlockTarget(null)
      setBlockReason('')
      reload()
    } catch {
      showError('De blokkering kon niet worden aangepast.')
    } finally {
      setSaving(false)
    }
  }

  async function handleDelete() {
    if (!deleteTarget) return
    try {
      await deleteTankCard(deleteTarget.id)
      showSuccess('Tankkaart verwijderd.')
      setDeleteTarget(null)
      reload()
    } catch {
      showError('De tankkaart kon niet worden verwijderd.')
      setDeleteTarget(null)
    }
  }

  const canEdit = hasPermission('tank_cards.edit')
  const canBlock = hasPermission('tank_cards.block')
  const canDelete = hasPermission('tank_cards.delete')

  const columns: Column<TankCard>[] = [
    { key: 'number', header: 'Kaartnummer', width: '150px', render: (row) => <code>{maskCardNumber(row.cardNumber)}</code> },
    { key: 'internalName', header: 'Interne naam', width: '140px', render: (row) => row.internalName ?? '—' },
    { key: 'provider', header: 'Leverancier', width: '130px', render: (row) => row.provider },
    {
      key: 'vehicle',
      header: 'Voertuig',
      render: (row) =>
        row.vehicleInternalNumber ? `${row.vehicleInternalNumber} (${row.vehicleLicensePlate})` : '—',
    },
    { key: 'employee', header: 'Medewerker', render: (row) => row.employeeName ?? row.driverName ?? '—' },
    { key: 'validUntil', header: 'Geldig tot', width: '120px', render: (row) => formatDate(row.validUntil) || '—' },
    {
      key: 'status',
      header: 'Status',
      width: '160px',
      render: (row) => <Badge tone={STATUS_TONE[row.status]}>{TANK_CARD_STATUS_LABELS[row.status]}</Badge>,
    },
    {
      key: 'actions',
      header: '',
      width: '210px',
      render: (row) => (
        <span className="tc-actions">
          {canEdit && (
            <button type="button" className="tc-link" onClick={() => openEdit(row)}>
              Bewerken
            </button>
          )}
          {canBlock && (
            <button
              type="button"
              className="tc-link"
              onClick={() => {
                setBlockTarget(row)
                setBlockReason('')
              }}
            >
              {row.isBlocked ? 'Deblokkeren' : 'Blokkeren'}
            </button>
          )}
          {canDelete && (
            <button type="button" className="tc-link tc-link-danger" onClick={() => setDeleteTarget(row)}>
              Verwijderen
            </button>
          )}
        </span>
      ),
    },
  ]

  return (
    <div>
      <Breadcrumbs items={[{ label: 'Tankkaarten' }]} />
      <PageHeader
        title="Tankkaarten"
        action={
          hasPermission('tank_cards.create') ? <Button onClick={openCreate}>Nieuwe tankkaart</Button> : undefined
        }
      />
      <div className="tc-filters">
        <FilterBar
          search={search}
          onSearchChange={(value) => {
            setSearch(value)
            setPage(1)
          }}
          searchPlaceholder="Zoeken op kaartnummer, leverancier, voertuig of medewerker..."
        />
        <select
          value={statusFilter}
          onChange={(e) => {
            setStatusFilter(e.target.value as TankCardStatus | '')
            setPage(1)
          }}
          className="tc-status-filter"
          aria-label="Statusfilter"
        >
          <option value="">Alle statussen</option>
          {TANK_CARD_STATUSES.map((status) => (
            <option key={status} value={status}>
              {TANK_CARD_STATUS_LABELS[status]}
            </option>
          ))}
        </select>
      </div>
      <DataTable
        columns={columns}
        rows={items}
        rowKey={(row) => row.id}
        isLoading={isLoading}
        error={error}
        emptyMessage="Nog geen tankkaarten."
        loadingMessage="Tankkaarten laden..."
      />
      <Pagination page={page} pageSize={pageSize} totalCount={totalCount} onPageChange={setPage} />

      {editorOpen && (
        <Modal
          title={editing ? 'Tankkaart bewerken' : 'Nieuwe tankkaart'}
          onClose={() => setEditorOpen(false)}
          busy={saving}
          footer={
            <>
              <Button variant="secondary" onClick={() => setEditorOpen(false)} disabled={saving}>
                Annuleren
              </Button>
              <Button type="submit" form="tc-form" disabled={saving}>
                {saving ? 'Opslaan…' : 'Opslaan'}
              </Button>
            </>
          }
        >
          <form id="tc-form" className="tc-form" onSubmit={handleSubmit} noValidate>
            {formError && (
              <div className="tc-form-error" role="alert">
                {formError}
              </div>
            )}
            <div className="tc-form-row">
              <FormField label="Kaartnummer" htmlFor="tc-number" required>
                <input
                  id="tc-number"
                  value={form.cardNumber}
                  onChange={(e) => set('cardNumber', e.target.value)}
                  disabled={saving}
                  maxLength={50}
                />
              </FormField>
              <FormField label="Leverancier" htmlFor="tc-provider" required>
                <input
                  id="tc-provider"
                  value={form.provider}
                  onChange={(e) => set('provider', e.target.value)}
                  disabled={saving}
                  maxLength={100}
                  placeholder="bv. DKV, Shell, Total"
                />
              </FormField>
            </div>
            <div className="tc-form-row">
              <FormField label="Interne naam" htmlFor="tc-internal-name">
                <input
                  id="tc-internal-name"
                  value={form.internalName}
                  onChange={(e) => set('internalName', e.target.value)}
                  disabled={saving}
                  maxLength={200}
                />
              </FormField>
              <FormField label="Medewerker" htmlFor="tc-employee">
                <EmployeeSelect
                  id="tc-employee"
                  value={form.employeeId || null}
                  onChange={(value) => set('employeeId', value ?? '')}
                  disabled={saving}
                  ariaLabel="Medewerker"
                />
              </FormField>
            </div>
            <div className="tc-form-row">
              <FormField label="Voertuig" htmlFor="tc-vehicle">
                <select
                  id="tc-vehicle"
                  value={form.vehicleId}
                  onChange={(e) => set('vehicleId', e.target.value)}
                  disabled={saving}
                >
                  <option value="">Geen</option>
                  {vehicles.map((vehicle) => (
                    <option key={vehicle.id} value={vehicle.id}>
                      {vehicle.internalNumber} ({vehicle.licensePlate})
                    </option>
                  ))}
                </select>
              </FormField>
              <FormField label="Brandstoftype" htmlFor="tc-fuel-type">
                <input
                  id="tc-fuel-type"
                  value={form.fuelType}
                  onChange={(e) => set('fuelType', e.target.value)}
                  disabled={saving}
                  maxLength={50}
                  placeholder="bv. Diesel, AdBlue"
                />
              </FormField>
            </div>
            <div className="tc-form-row">
              <FormField label="Limiet per dag (€)" htmlFor="tc-daily-limit">
                <input
                  id="tc-daily-limit"
                  type="number"
                  min={0}
                  step={0.01}
                  value={form.dailyLimit}
                  onChange={(e) => set('dailyLimit', e.target.value)}
                  disabled={saving}
                />
              </FormField>
              <FormField label="Limiet per week (€)" htmlFor="tc-weekly-limit">
                <input
                  id="tc-weekly-limit"
                  type="number"
                  min={0}
                  step={0.01}
                  value={form.weeklyLimit}
                  onChange={(e) => set('weeklyLimit', e.target.value)}
                  disabled={saving}
                />
              </FormField>
            </div>
            <div className="tc-form-row">
              <FormField label="Limiet per maand (€)" htmlFor="tc-monthly-limit">
                <input
                  id="tc-monthly-limit"
                  type="number"
                  min={0}
                  step={0.01}
                  value={form.monthlyLimit}
                  onChange={(e) => set('monthlyLimit', e.target.value)}
                  disabled={saving}
                />
              </FormField>
              <FormField label="Kostenplaats" htmlFor="tc-cost-center">
                <input
                  id="tc-cost-center"
                  value={form.costCenter}
                  onChange={(e) => set('costCenter', e.target.value)}
                  disabled={saving}
                  maxLength={100}
                />
              </FormField>
            </div>
            <div className="tc-form-row">
              <FormField label="Geldig van" htmlFor="tc-from">
                <input
                  id="tc-from"
                  type="date"
                  value={form.validFrom}
                  onChange={(e) => set('validFrom', e.target.value)}
                  disabled={saving}
                />
              </FormField>
              <FormField label="Geldig tot" htmlFor="tc-until">
                <input
                  id="tc-until"
                  type="date"
                  value={form.validUntil}
                  onChange={(e) => set('validUntil', e.target.value)}
                  disabled={saving}
                />
              </FormField>
            </div>
            <FormField label="Notities" htmlFor="tc-notes">
              <textarea
                id="tc-notes"
                rows={2}
                value={form.notes}
                onChange={(e) => set('notes', e.target.value)}
                disabled={saving}
              />
            </FormField>
          </form>
        </Modal>
      )}

      {blockTarget && (
        <Modal
          title={blockTarget.isBlocked ? 'Tankkaart deblokkeren' : 'Tankkaart blokkeren'}
          onClose={() => setBlockTarget(null)}
          busy={saving}
          footer={
            <>
              <Button variant="secondary" onClick={() => setBlockTarget(null)} disabled={saving}>
                Annuleren
              </Button>
              <Button type="submit" form="tc-block-form" disabled={saving}>
                {saving ? 'Bezig…' : blockTarget.isBlocked ? 'Deblokkeren' : 'Blokkeren'}
              </Button>
            </>
          }
        >
          <form id="tc-block-form" className="tc-form" onSubmit={handleBlockConfirm} noValidate>
            <p className="tc-block-text">
              {blockTarget.isBlocked
                ? `Kaart ${maskCardNumber(blockTarget.cardNumber)} opnieuw activeren?`
                : `Kaart ${maskCardNumber(blockTarget.cardNumber)} blokkeren? Registreer dit ook bij de provider — dit systeem blokkeert de kaart niet extern.`}
            </p>
            {!blockTarget.isBlocked && (
              <FormField label="Reden" htmlFor="tc-block-reason">
                <input
                  id="tc-block-reason"
                  value={blockReason}
                  onChange={(e) => setBlockReason(e.target.value)}
                  disabled={saving}
                  maxLength={500}
                  placeholder="bv. kaart verloren of gestolen"
                />
              </FormField>
            )}
          </form>
        </Modal>
      )}

      {deleteTarget && (
        <ConfirmDialog
          title="Tankkaart verwijderen"
          message={`Weet je zeker dat je kaart ${maskCardNumber(deleteTarget.cardNumber)} (${deleteTarget.provider}) wilt verwijderen?`}
          confirmLabel="Verwijderen"
          destructive
          onConfirm={handleDelete}
          onCancel={() => setDeleteTarget(null)}
        />
      )}
    </div>
  )
}
