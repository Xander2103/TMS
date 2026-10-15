import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { Paperclip, Truck } from 'lucide-react'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { DataTable, type Column } from '../../../components/ui/DataTable'
import { DensityToggle } from '../../../components/ui/DensityToggle'
import { FilterBar } from '../../../components/ui/FilterBar'
import { Pagination } from '../../../components/ui/Pagination'
import { RowActionMenu } from '../../../components/ui/RowActionMenu'
import { TruncatedText } from '../../../components/ui/TruncatedText'
import { useListDensity } from '../../../components/ui/listDensity'
import { useToast } from '../../../components/ui/toastContext'
import { usePagedQuery } from '../../../hooks/usePagedQuery'
import { tableSortOf, useUrlListState } from '../../../hooks/useUrlListState'
import { useLocale } from '../../../i18n/localeContext'
import { useAuth } from '../../auth/authContextValue'
import { addDays, toIsoDate } from '../../../components/calendar/dateUtils'
import { cancelAbsence, deleteAbsence, searchAbsences } from '../api/absencesApi'
import { ReviewAbsenceDialog } from '../components/ReviewAbsenceDialog'
import {
  ABSENCE_STATUS_TONE,
  ABSENCE_STATUSES,
  ABSENCE_TYPES,
  isReviewable,
  type Absence,
  type AbsenceStatus,
  type AbsenceType,
} from '../types'
import './absences-page.css'
import { formatDate } from '../../../utils/dates'

/** Window shown when the URL names no dates (the server uses the same default). */
const DEFAULT_RANGE_DAYS = 60
const ISO_DATE = /^\d{4}-\d{2}-\d{2}$/

/**
 * B50: the overview's view in the URL. Empty dates mean "today … +60 days", so a clean URL keeps
 * following the calendar; the server filters, sorts (start | end | employee) and pages.
 */
const LIST_STATE = {
  defaults: { search: '', from: '', to: '', type: '', status: '', sort: '', dir: '' },
  allowed: { type: ABSENCE_TYPES, status: ABSENCE_STATUSES, sort: ['start', 'end', 'employee'], dir: ['asc', 'desc'] },
}

function employeeLink(absence: Absence): string {
  return `/employees/${absence.employeeId}?tab=verlof&absenceId=${absence.id}`
}

/** Planning overview of absences across all employees within a date window. */
export function AbsencesPage() {
  const { t } = useLocale()
  const navigate = useNavigate()
  const { hasPermission } = useAuth()
  const { showSuccess, showError } = useToast()
  const canApprove = hasPermission('absences.approve')
  const canEdit = hasPermission('absences.edit')
  const canDelete = hasPermission('absences.delete')
  const [reviewTarget, setReviewTarget] = useState<Absence | null>(null)
  const [cancelTarget, setCancelTarget] = useState<Absence | null>(null)
  const [deleteTarget, setDeleteTarget] = useState<Absence | null>(null)

  const { state, apply, reset, isFiltered } = useUrlListState(LIST_STATE)
  const { search, page } = state
  const typeFilter = state.type as AbsenceType | ''
  const statusFilter = state.status as AbsenceStatus | ''
  // A hand-edited link with a malformed date falls back to the default window.
  const from = ISO_DATE.test(state.from) ? state.from : toIsoDate(new Date())
  const to = ISO_DATE.test(state.to) ? state.to : toIsoDate(addDays(new Date(`${from}T00:00:00`), DEFAULT_RANGE_DAYS))
  const [density, setDensity] = useListDensity('absences')

  const { items, totalCount, pageSize, isLoading, error, reload } = usePagedQuery<Absence>(
    (args) =>
      searchAbsences({
        ...args,
        from,
        to,
        type: typeFilter || undefined,
        status: statusFilter || undefined,
        sort: state.sort,
        dir: state.dir as 'asc' | 'desc' | '',
      }),
    {
      search,
      page,
      errorMessage: t('absences.page.loadFailed'),
      extra: { from, to, type: typeFilter, status: statusFilter, sort: state.sort, dir: state.dir },
    },
  )

  async function handleCancel() {
    if (!cancelTarget) return
    try {
      await cancelAbsence(cancelTarget.id)
      showSuccess(t('absences.tab.cancelled'))
      reload()
    } catch {
      showError(t('absences.tab.cancelFailed'))
    } finally {
      setCancelTarget(null)
    }
  }

  async function handleDelete() {
    if (!deleteTarget) return
    try {
      await deleteAbsence(deleteTarget.id)
      showSuccess(t('absences.tab.deleted'))
      reload()
    } catch {
      showError(t('absences.tab.deleteFailed'))
    } finally {
      setDeleteTarget(null)
    }
  }

  const columns: Column<Absence>[] = [
    {
      key: 'employee',
      header: t('absences.page.colEmployee'),
      sortKey: 'employee',
      render: (row) => (
        <span className="absp-employee" title={row.isDriver ? `${row.employeeName} · ${t('absences.page.driver')}` : undefined}>
          <TruncatedText tooltip={`${row.employeeName} (${row.employeeNumber})`}>
            {row.employeeName} <span className="absp-number">{row.employeeNumber}</span>
          </TruncatedText>
          {/* A named icon, not a badge: the name gets the room (B14). */}
          {row.isDriver && <Truck size={14} className="absp-driver" role="img" aria-label={t('absences.page.driver')} />}
        </span>
      ),
    },
    {
      key: 'type',
      header: t('absences.tab.colType'),
      width: '120px',
      render: (row) => (
        <span className="absp-type">
          <TruncatedText tooltip={t(`absences.type.${row.type}`)}>{t(`absences.type.${row.type}`)}</TruncatedText>
          {row.hasAttachment && (
            <Paperclip size={14} className="absp-attachment" aria-label={t('absences.page.hasAttachment')} role="img" />
          )}
        </span>
      ),
    },
    { key: 'start', header: t('absences.tab.colFrom'), width: '118px', sortKey: 'start', render: (row) => <span className="ui-nowrap">{formatDate(row.startDate)}</span> },
    { key: 'end', header: t('absences.tab.colTo'), width: '118px', sortKey: 'end', render: (row) => <span className="ui-nowrap">{formatDate(row.endDate)}</span> },
    {
      key: 'status',
      header: t('absences.tab.colStatus'),
      width: '150px',
      render: (row) => <Badge tone={ABSENCE_STATUS_TONE[row.status]}>{t(`absences.status.${row.status}`)}</Badge>,
    },
    {
      key: 'reason',
      header: t('absences.tab.colReason'),
      className: 'ui-col-optional',
      render: (row) => (row.reason ? <TruncatedText tooltip={row.reason} className="absp-reason">{row.reason}</TruncatedText> : '—'),
    },
    {
      key: 'actions',
      header: '',
      width: '180px',
      align: 'right',
      // B50: "Beoordelen" only where there is still something to decide; the rest in one "⋯".
      render: (row) => (
        <span className="data-table-actions">
          {canApprove && isReviewable(row) && (
            <Button
              variant="secondary"
              className="absp-review"
              onClick={(event) => {
                event.stopPropagation()
                setReviewTarget(row)
              }}
            >
              {t('absences.page.review')}
            </Button>
          )}
          <RowActionMenu
            label={t('absences.page.rowActions', { name: row.employeeName })}
            items={[
              { key: 'open', label: t('absences.page.openEmployee'), onSelect: () => navigate(employeeLink(row)) },
              ...(canApprove && !isReviewable(row)
                ? [{ key: 'details', label: t('absences.page.details'), onSelect: () => setReviewTarget(row) }]
                : []),
              ...(canEdit && (row.status === 'Requested' || row.status === 'Approved')
                ? [{ key: 'cancel', label: t('absences.page.cancel'), danger: true, onSelect: () => setCancelTarget(row) }]
                : []),
              ...(canDelete ? [{ key: 'delete', label: t('ui.actions.delete'), danger: true, onSelect: () => setDeleteTarget(row) }] : []),
            ]}
          />
        </span>
      ),
    },
  ]

  return (
    <div>
      <PageHeader title={t('absences.page.title')} subtitle={t('absences.page.subtitle')} />

      <FilterBar
        search={search}
        onSearchChange={(value) => apply({ search: value }, { replace: true })}
        searchPlaceholder={t('absences.page.searchPlaceholder')}
        onClear={isFiltered ? reset : undefined}
        trailing={<DensityToggle value={density} onChange={setDensity} />}
      >
        <label className="absp-date">
          {t('absences.page.from')}
          <input type="date" className="ui-filter-select" value={from} onChange={(e) => apply({ from: e.target.value })} />
        </label>
        <label className="absp-date">
          {t('absences.page.to')}
          <input type="date" className="ui-filter-select" value={to} onChange={(e) => apply({ to: e.target.value })} />
        </label>
        <select
          className="ui-filter-select"
          value={typeFilter}
          onChange={(e) => apply({ type: e.target.value })}
          aria-label={t('absences.page.typeFilter')}
        >
          <option value="">{t('absences.page.allTypes')}</option>
          {ABSENCE_TYPES.map((type) => (
            <option key={type} value={type}>
              {t(`absences.type.${type}`)}
            </option>
          ))}
        </select>
        <select
          className="ui-filter-select"
          value={statusFilter}
          onChange={(e) => apply({ status: e.target.value })}
          aria-label={t('absences.page.statusFilter')}
        >
          <option value="">{t('absences.page.allStatuses')}</option>
          {ABSENCE_STATUSES.map((status) => (
            <option key={status} value={status}>
              {t(`absences.status.${status}`)}
            </option>
          ))}
        </select>
      </FilterBar>

      <DataTable
        fixedLayout
        density={density}
        sort={tableSortOf(state.sort, state.dir)}
        onSortChange={(next) => apply({ sort: next.key, dir: next.dir })}
        columns={columns}
        rows={items}
        rowKey={(row) => row.id}
        isLoading={isLoading}
        error={error}
        onRetry={reload}
        emptyMessage={t('absences.page.empty')}
        loadingMessage={t('absences.page.loading')}
        onRowClick={(row) => navigate(employeeLink(row))}
      />
      <Pagination page={page} pageSize={pageSize} totalCount={totalCount} onPageChange={(next) => apply({ page: next })} />

      {reviewTarget && (
        <ReviewAbsenceDialog
          absence={reviewTarget}
          onClose={() => setReviewTarget(null)}
          onChanged={(updated) => {
            setReviewTarget(updated)
            reload()
          }}
        />
      )}

      {cancelTarget && (
        <ConfirmDialog
          title={t('absences.tab.cancelTitle')}
          message={t('absences.tab.cancelMessage', {
            type: t(`absences.type.${cancelTarget.type}`),
            from: formatDate(cancelTarget.startDate),
            to: formatDate(cancelTarget.endDate),
          })}
          confirmLabel={t('absences.tab.cancelConfirm')}
          destructive
          onConfirm={handleCancel}
          onCancel={() => setCancelTarget(null)}
        />
      )}

      {deleteTarget && (
        <ConfirmDialog
          title={t('absences.tab.deleteTitle')}
          message={t('absences.tab.deleteMessage', {
            type: t(`absences.type.${deleteTarget.type}`),
            from: formatDate(deleteTarget.startDate),
            to: formatDate(deleteTarget.endDate),
          })}
          confirmLabel={t('ui.actions.delete')}
          destructive
          onConfirm={handleDelete}
          onCancel={() => setDeleteTarget(null)}
        />
      )}
    </div>
  )
}
