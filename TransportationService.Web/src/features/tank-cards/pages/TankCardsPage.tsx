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
import { searchDrivers } from '../../drivers/api/driversApi'
import type { DriverListItem } from '../../drivers/types'
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
  driverId: string
  validFrom: string
  validUntil: string
  notes: string
}

const EMPTY_FORM: CardForm = {
  cardNumber: '',
  provider: '',
  vehicleId: '',
  driverId: '',
  validFrom: '',
  validUntil: '',
  notes: '',
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
  const [drivers, setDrivers] = useState<DriverListItem[]>([])

  useEffect(() => {
    let mounted = true
    getVehicleOptions()
      .then((data) => {
        if (mounted) setVehicles(data)
      })
      .catch(() => {})
    searchDrivers({ isActive: true, page: 1, pageSize: 200 })
      .then((data) => {
        if (mounted) setDrivers(data.items)
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
      driverId: card.driverId ?? '',
      validFrom: card.validFrom ?? '',
      validUntil: card.validUntil ?? '',
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
      setFormError('Provider is verplicht.')
      return
    }
    const input: TankCardInput = {
      cardNumber: form.cardNumber.trim(),
      provider: form.provider.trim(),
      vehicleId: form.vehicleId || null,
      driverId: form.driverId || null,
      validFrom: form.validFrom || null,
      validUntil: form.validUntil || null,
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
    { key: 'provider', header: 'Provider', width: '130px', render: (row) => row.provider },
    {
      key: 'vehicle',
      header: 'Voertuig',
      render: (row) =>
        row.vehicleInternalNumber ? `${row.vehicleInternalNumber} (${row.vehicleLicensePlate})` : '—',
    },
    { key: 'driver', header: 'Chauffeur', render: (row) => row.driverName ?? '—' },
    { key: 'validUntil', header: 'Geldig tot', width: '120px', render: (row) => row.validUntil ?? '—' },
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
          searchPlaceholder="Zoeken op kaartnummer, provider of voertuig..."
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
              <FormField label="Provider" htmlFor="tc-provider" required>
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
              <FormField label="Chauffeur" htmlFor="tc-driver">
                <select
                  id="tc-driver"
                  value={form.driverId}
                  onChange={(e) => set('driverId', e.target.value)}
                  disabled={saving}
                >
                  <option value="">Geen</option>
                  {drivers.map((driver) => (
                    <option key={driver.id} value={driver.id}>
                      {driver.fullName} ({driver.driverNumber})
                    </option>
                  ))}
                </select>
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
