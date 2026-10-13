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
import { useLocale } from '../../../i18n/localeContext'
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
  const { t } = useLocale()
  const { hasPermission } = useAuth()
  const { showSuccess, showError } = useToast()

  const [search, setSearch] = useState('')
  const [statusFilter, setStatusFilter] = useState<TankCardStatus | ''>('')
  const [page, setPage] = useState(1)

  const { items, totalCount, pageSize, isLoading, error, reload } = usePagedQuery<TankCard>(
    (args) => searchTankCards({ ...args, status: statusFilter || undefined }),
    { search, page, errorMessage: t('tankCards.page.loadFailed') },
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
  // Vertaalsleutels in state; vertaling gebeurt pas bij render.
  const [formErrorKey, setFormErrorKey] = useState<string | null>(null)
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
    setFormErrorKey(null)
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
    setFormErrorKey(null)
    setEditorOpen(true)
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setFormErrorKey(null)
    if (!form.cardNumber.trim()) {
      setFormErrorKey('tankCards.form.numberRequired')
      return
    }
    if (!form.provider.trim()) {
      setFormErrorKey('tankCards.form.providerRequired')
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
        showSuccess(t('tankCards.form.updated'))
      } else {
        await createTankCard(input)
        showSuccess(t('tankCards.form.created'))
      }
      setEditorOpen(false)
      reload()
    } catch (err) {
      setFormErrorKey(
        err instanceof ApiError && err.status === 409 ? 'tankCards.form.duplicate' : 'tankCards.form.saveFailed',
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
      showSuccess(willBlock ? t('tankCards.block.blocked') : t('tankCards.block.unblocked'))
      setBlockTarget(null)
      setBlockReason('')
      reload()
    } catch {
      showError(t('tankCards.block.failed'))
    } finally {
      setSaving(false)
    }
  }

  async function handleDelete() {
    if (!deleteTarget) return
    try {
      await deleteTankCard(deleteTarget.id)
      showSuccess(t('tankCards.delete.deleted'))
      setDeleteTarget(null)
      reload()
    } catch {
      showError(t('tankCards.delete.failed'))
      setDeleteTarget(null)
    }
  }

  const canEdit = hasPermission('tank_cards.edit')
  const canBlock = hasPermission('tank_cards.block')
  const canDelete = hasPermission('tank_cards.delete')

  const columns: Column<TankCard>[] = [
    { key: 'number', header: t('tankCards.page.colNumber'), width: '150px', render: (row) => <code>{maskCardNumber(row.cardNumber)}</code> },
    { key: 'internalName', header: t('tankCards.page.colInternalName'), width: '140px', render: (row) => row.internalName ?? '—' },
    { key: 'provider', header: t('tankCards.page.colProvider'), width: '130px', render: (row) => row.provider },
    {
      key: 'vehicle',
      header: t('tankCards.page.colVehicle'),
      render: (row) =>
        row.vehicleInternalNumber ? `${row.vehicleInternalNumber} (${row.vehicleLicensePlate})` : '—',
    },
    { key: 'employee', header: t('tankCards.page.colEmployee'), render: (row) => row.employeeName ?? row.driverName ?? '—' },
    { key: 'validUntil', header: t('tankCards.page.colValidUntil'), width: '120px', render: (row) => formatDate(row.validUntil) || '—' },
    {
      key: 'status',
      header: t('tankCards.page.colStatus'),
      width: '160px',
      render: (row) => <Badge tone={STATUS_TONE[row.status]}>{t(`tankCards.status.${row.status}`)}</Badge>,
    },
    {
      key: 'actions',
      header: '',
      width: '210px',
      render: (row) => (
        <span className="tc-actions">
          {canEdit && (
            <button type="button" className="tc-link" onClick={() => openEdit(row)}>
              {t('ui.actions.edit')}
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
              {row.isBlocked ? t('tankCards.page.unblock') : t('tankCards.page.block')}
            </button>
          )}
          {canDelete && (
            <button type="button" className="tc-link tc-link-danger" onClick={() => setDeleteTarget(row)}>
              {t('ui.actions.delete')}
            </button>
          )}
        </span>
      ),
    },
  ]

  return (
    <div>
      <Breadcrumbs items={[{ label: t('tankCards.page.breadcrumb') }]} />
      <PageHeader
        title={t('tankCards.page.title')}
        action={
          hasPermission('tank_cards.create') ? <Button onClick={openCreate}>{t('tankCards.page.new')}</Button> : undefined
        }
      />
      <div className="tc-filters">
        <FilterBar
          search={search}
          onSearchChange={(value) => {
            setSearch(value)
            setPage(1)
          }}
          searchPlaceholder={t('tankCards.page.searchPlaceholder')}
        />
        <select
          value={statusFilter}
          onChange={(e) => {
            setStatusFilter(e.target.value as TankCardStatus | '')
            setPage(1)
          }}
          className="tc-status-filter"
          aria-label={t('tankCards.page.statusFilter')}
        >
          <option value="">{t('tankCards.page.allStatuses')}</option>
          {TANK_CARD_STATUSES.map((status) => (
            <option key={status} value={status}>
              {t(`tankCards.status.${status}`)}
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
        emptyMessage={t('tankCards.page.empty')}
        loadingMessage={t('tankCards.page.loading')}
      />
      <Pagination page={page} pageSize={pageSize} totalCount={totalCount} onPageChange={setPage} />

      {editorOpen && (
        <Modal
          title={editing ? t('tankCards.form.editTitle') : t('tankCards.form.newTitle')}
          onClose={() => setEditorOpen(false)}
          busy={saving}
          footer={
            <>
              <Button variant="secondary" onClick={() => setEditorOpen(false)} disabled={saving}>
                {t('ui.actions.cancel')}
              </Button>
              <Button type="submit" form="tc-form" disabled={saving}>
                {saving ? t('tankCards.form.saving') : t('ui.actions.save')}
              </Button>
            </>
          }
        >
          <form id="tc-form" className="tc-form" onSubmit={handleSubmit} noValidate>
            {formErrorKey && (
              <div className="tc-form-error" role="alert">
                {t(formErrorKey)}
              </div>
            )}
            <div className="tc-form-row">
              <FormField label={t('tankCards.form.cardNumber')} htmlFor="tc-number" required>
                <input
                  id="tc-number"
                  value={form.cardNumber}
                  onChange={(e) => set('cardNumber', e.target.value)}
                  disabled={saving}
                  maxLength={50}
                />
              </FormField>
              <FormField label={t('tankCards.form.provider')} htmlFor="tc-provider" required>
                <input
                  id="tc-provider"
                  value={form.provider}
                  onChange={(e) => set('provider', e.target.value)}
                  disabled={saving}
                  maxLength={100}
                  placeholder={t('tankCards.form.providerPlaceholder')}
                />
              </FormField>
            </div>
            <div className="tc-form-row">
              <FormField label={t('tankCards.form.internalName')} htmlFor="tc-internal-name">
                <input
                  id="tc-internal-name"
                  value={form.internalName}
                  onChange={(e) => set('internalName', e.target.value)}
                  disabled={saving}
                  maxLength={200}
                />
              </FormField>
              <FormField label={t('tankCards.form.employee')} htmlFor="tc-employee">
                <EmployeeSelect
                  id="tc-employee"
                  value={form.employeeId || null}
                  onChange={(value) => set('employeeId', value ?? '')}
                  disabled={saving}
                  ariaLabel={t('tankCards.form.employee')}
                />
              </FormField>
            </div>
            <div className="tc-form-row">
              <FormField label={t('tankCards.form.vehicle')} htmlFor="tc-vehicle">
                <select
                  id="tc-vehicle"
                  value={form.vehicleId}
                  onChange={(e) => set('vehicleId', e.target.value)}
                  disabled={saving}
                >
                  <option value="">{t('tankCards.form.noVehicle')}</option>
                  {vehicles.map((vehicle) => (
                    <option key={vehicle.id} value={vehicle.id}>
                      {vehicle.internalNumber} ({vehicle.licensePlate})
                    </option>
                  ))}
                </select>
              </FormField>
              <FormField label={t('tankCards.form.fuelType')} htmlFor="tc-fuel-type">
                <input
                  id="tc-fuel-type"
                  value={form.fuelType}
                  onChange={(e) => set('fuelType', e.target.value)}
                  disabled={saving}
                  maxLength={50}
                  placeholder={t('tankCards.form.fuelTypePlaceholder')}
                />
              </FormField>
            </div>
            <div className="tc-form-row">
              <FormField label={t('tankCards.form.dailyLimit')} htmlFor="tc-daily-limit">
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
              <FormField label={t('tankCards.form.weeklyLimit')} htmlFor="tc-weekly-limit">
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
              <FormField label={t('tankCards.form.monthlyLimit')} htmlFor="tc-monthly-limit">
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
              <FormField label={t('tankCards.form.costCenter')} htmlFor="tc-cost-center">
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
              <FormField label={t('tankCards.form.validFrom')} htmlFor="tc-from">
                <input
                  id="tc-from"
                  type="date"
                  value={form.validFrom}
                  onChange={(e) => set('validFrom', e.target.value)}
                  disabled={saving}
                />
              </FormField>
              <FormField label={t('tankCards.form.validUntil')} htmlFor="tc-until">
                <input
                  id="tc-until"
                  type="date"
                  value={form.validUntil}
                  onChange={(e) => set('validUntil', e.target.value)}
                  disabled={saving}
                />
              </FormField>
            </div>
            <FormField label={t('tankCards.form.notes')} htmlFor="tc-notes">
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
          title={blockTarget.isBlocked ? t('tankCards.block.unblockTitle') : t('tankCards.block.blockTitle')}
          onClose={() => setBlockTarget(null)}
          busy={saving}
          footer={
            <>
              <Button variant="secondary" onClick={() => setBlockTarget(null)} disabled={saving}>
                {t('ui.actions.cancel')}
              </Button>
              <Button type="submit" form="tc-block-form" disabled={saving}>
                {saving ? t('tankCards.block.busy') : blockTarget.isBlocked ? t('tankCards.block.unblock') : t('tankCards.block.block')}
              </Button>
            </>
          }
        >
          <form id="tc-block-form" className="tc-form" onSubmit={handleBlockConfirm} noValidate>
            <p className="tc-block-text">
              {blockTarget.isBlocked
                ? t('tankCards.block.unblockText', { number: maskCardNumber(blockTarget.cardNumber) })
                : t('tankCards.block.blockText', { number: maskCardNumber(blockTarget.cardNumber) })}
            </p>
            {!blockTarget.isBlocked && (
              <FormField label={t('tankCards.block.reason')} htmlFor="tc-block-reason">
                <input
                  id="tc-block-reason"
                  value={blockReason}
                  onChange={(e) => setBlockReason(e.target.value)}
                  disabled={saving}
                  maxLength={500}
                  placeholder={t('tankCards.block.reasonPlaceholder')}
                />
              </FormField>
            )}
          </form>
        </Modal>
      )}

      {deleteTarget && (
        <ConfirmDialog
          title={t('tankCards.delete.title')}
          message={t('tankCards.delete.message', { number: maskCardNumber(deleteTarget.cardNumber), provider: deleteTarget.provider })}
          confirmLabel={t('ui.actions.delete')}
          destructive
          onConfirm={handleDelete}
          onCancel={() => setDeleteTarget(null)}
        />
      )}
    </div>
  )
}
