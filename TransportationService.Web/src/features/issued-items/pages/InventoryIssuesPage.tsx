import { useEffect, useState } from 'react'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Badge, type BadgeTone } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { DataTable, type Column } from '../../../components/ui/DataTable'
import { FilterBar } from '../../../components/ui/FilterBar'
import { Tabs } from '../../../components/ui/Tabs'
import { useToast } from '../../../components/ui/toastContext'
import { useUrlListState } from '../../../hooks/useUrlListState'
import { useLocale } from '../../../i18n/localeContext'
import { formatDate } from '../../../utils/dates'
import { useAuth } from '../../auth/authContextValue'
import { issuanceOutcomeKey, itemDisplayName, type IssuedItemStatus } from '../issuedItemsApi'
import { listInventoryIssues, returnToWarehouse, type InventoryIssueRow } from '../inventoryApi'
import { ReturnIssuedItemDialog } from '../components/ReturnIssuedItemDialog'
import { WarehouseIssueModal } from '../components/WarehouseIssueModal'
import '../issued-items.css'
import './InventoryIssuesPage.css'

const STATUS_TONE: Record<IssuedItemStatus, BadgeTone> = {
  NotIssued: 'neutral',
  Issued: 'success',
  Returned: 'info',
  Missing: 'danger',
  Damaged: 'warning',
  Consumed: 'neutral',
}

/** A damaged or lost return is not a plain "returned" — its tone says so too (WH-M4). */
function outcomeTone(row: InventoryIssueRow): BadgeTone {
  if (row.status === 'Returned' && (row.returnDisposition === 'lost' || row.returnDisposition === 'disposed')) return 'danger'
  if (row.status === 'Returned' && row.returnDisposition === 'damaged') return 'warning'
  return STATUS_TONE[row.status]
}

/** Defaults are omitted from the URL; '1' = filter on. */
const LIST_DEFAULTS: Record<'scope' | 'q' | 'overdue' | 'inactive' | 'mustReturn' | 'due', string> = {
  scope: 'open', q: '', overdue: '', inactive: '', mustReturn: '', due: '',
}
const LIST_ALLOWED = { scope: ['open', 'history'], overdue: ['', '1'], inactive: ['', '1'], mustReturn: ['', '1'] }

/**
 * WH-H3 / H4 / H5: the warehouse desk. "Openstaande uitgiftes" answers "who has scanner SER-001?"
 * and "what must come back?" (overdue, inactive employees, return due) without opening employees
 * one by one; "Historiek" is the searchable issuance history (item, code, serial, employee).
 * "Uitgeven" and "Retour verwerken" need inventory.issue / inventory.return — no HR right.
 * Search and filters live in the URL (WH-M9): reload and Back keep the view.
 */
export function InventoryIssuesPage() {
  const { t } = useLocale()
  const { showSuccess } = useToast()
  const { hasPermission } = useAuth()
  const canIssue = hasPermission('inventory.issue')
  const canReturn = hasPermission('inventory.return')
  const canOverrideStock = hasPermission('inventory.override_negative_stock')
  const { state, apply, reset, isFiltered } = useUrlListState({ defaults: LIST_DEFAULTS, allowed: LIST_ALLOWED })
  const history = state.scope === 'history'
  const filtersActive = Boolean(state.q || state.overdue || state.inactive || state.mustReturn || state.due)

  const [rows, setRows] = useState<InventoryIssueRow[] | null>(null)
  const [failed, setFailed] = useState(false)
  const [reloadToken, setReloadToken] = useState(0)
  const [issueOpen, setIssueOpen] = useState(false)
  const [returnTarget, setReturnTarget] = useState<InventoryIssueRow | null>(null)

  useEffect(() => {
    let mounted = true
    // Typing in the search box debounces; filter toggles fire almost immediately.
    const timer = window.setTimeout(() => {
      listInventoryIssues({
        scope: history ? 'history' : 'open',
        search: state.q,
        overdueOnly: state.overdue === '1',
        inactiveEmployeesOnly: state.inactive === '1',
        returnRequiredOnly: state.mustReturn === '1',
        dueBefore: state.due || undefined,
      })
        .then((data) => {
          if (!mounted) return
          setRows(data)
          setFailed(false)
        })
        .catch(() => {
          if (mounted) setFailed(true)
        })
    }, 250)
    return () => {
      mounted = false
      window.clearTimeout(timer)
    }
  }, [history, state.q, state.overdue, state.inactive, state.mustReturn, state.due, reloadToken])

  const reload = () => setReloadToken((token) => token + 1)

  const employeeColumn: Column<InventoryIssueRow> = {
    key: 'medewerker',
    header: t('inventoryDesk.col.employee'),
    render: (row) => (
      <span className="inventory-desk-employee">
        <span>{row.employeeName}</span>
        <small className="inventory-desk-muted">{row.employeeNumber}</small>
        {!row.employeeActive && <Badge tone="warning">{t('inventoryDesk.inactiveEmployee')}</Badge>}
      </span>
    ),
  }
  const itemColumn: Column<InventoryIssueRow> = {
    key: 'artikel',
    header: t('inventoryDesk.col.item'),
    render: (row) => (
      <span className="inventory-desk-item">
        {row.itemCode && <code className="inventory-desk-code">{row.itemCode}</code>}
        <span>{row.itemName}</span>
        {row.variantLabel && <small className="inventory-desk-muted">{row.variantLabel}</small>}
      </span>
    ),
  }
  const serialColumn: Column<InventoryIssueRow> = {
    key: 'serie',
    header: t('inventoryDesk.col.serial'),
    render: (row) =>
      row.serialNumber ? (
        <span className="inventory-desk-serial">
          <code>{row.serialNumber}</code>
          {row.duplicateOpenSerial && (
            <Badge tone="danger">{t('inventoryDesk.duplicateSerial')}</Badge>
          )}
        </span>
      ) : (
        '—'
      ),
  }
  const quantityColumn: Column<InventoryIssueRow> = { key: 'aantal', header: t('inventoryDesk.col.quantity'), align: 'right', render: (row) => row.quantity }
  const issuedColumn: Column<InventoryIssueRow> = { key: 'uitgereikt', header: t('inventoryDesk.col.issued'), render: (row) => formatDate(row.issuedDate) || '—' }

  const columns: Column<InventoryIssueRow>[] = history
    ? [
        employeeColumn,
        itemColumn,
        serialColumn,
        quantityColumn,
        {
          // One cell for the period keeps the history readable at 1152px.
          key: 'periode',
          header: t('inventoryDesk.col.period'),
          render: (row) => (
            <span className="inventory-desk-due">
              <span>{formatDate(row.issuedDate) || '—'}</span>
              <small className="inventory-desk-muted">{row.returnedDate ? `→ ${formatDate(row.returnedDate)}` : ''}</small>
            </span>
          ),
        },
        {
          key: 'uitkomst',
          header: t('inventoryDesk.col.outcome'),
          render: (row) => (
            <span className="inventory-desk-by">
              <Badge tone={outcomeTone(row)}>{t(issuanceOutcomeKey(row.status, row.returnDisposition))}</Badge>
              {row.issuedByName && <small className="inventory-desk-muted">{t('inventoryDesk.issuedBy', { name: row.issuedByName })}</small>}
              {row.receivedBackByName && <small className="inventory-desk-muted">{t('inventoryDesk.receivedBy', { name: row.receivedBackByName })}</small>}
            </span>
          ),
        },
      ]
    : [
        employeeColumn,
        itemColumn,
        serialColumn,
        quantityColumn,
        issuedColumn,
        {
          key: 'verwacht',
          header: t('inventoryDesk.col.expectedReturn'),
          render: (row) => (
            <span className="inventory-desk-due">
              {formatDate(row.expectedReturnDate) || (row.returnRequired ? t('inventoryDesk.noDueDate') : '—')}
              {row.isOverdue && <Badge tone="danger">{t('inventoryDesk.overdue')}</Badge>}
            </span>
          ),
        },
        {
          key: 'acties',
          header: <span aria-label={t('issuedItems.tab.colActions')} />,
          render: (row) =>
            canReturn && row.status === 'Issued' ? (
              <button type="button" className="issued-items-link" onClick={() => setReturnTarget(row)}>
                {t('inventoryDesk.processReturn')}
              </button>
            ) : null,
        },
      ]

  return (
    <div>
      <PageHeader
        title={t('inventoryDesk.title')}
        subtitle={t('inventoryDesk.subtitle')}
        action={canIssue ? <Button onClick={() => setIssueOpen(true)}>{t('inventoryDesk.issueAction')}</Button> : undefined}
      />

      <Tabs
        tabs={[
          { id: 'open', label: t('inventoryDesk.tabOpen') },
          { id: 'history', label: t('inventoryDesk.tabHistory') },
        ]}
        activeId={history ? 'history' : 'open'}
        onChange={(id) => apply({ scope: id })}
      />

      <FilterBar
        search={state.q}
        onSearchChange={(value) => apply({ q: value }, { replace: true })}
        searchPlaceholder={t('inventoryDesk.searchPlaceholder')}
        searchLabel={t('inventoryDesk.searchLabel')}
        onClear={isFiltered ? () => reset() : undefined}
      >
        {!history && (
          <>
            <label className="issued-items-checkbox inventory-desk-filter">
              <input type="checkbox" checked={state.overdue === '1'} onChange={(e) => apply({ overdue: e.target.checked ? '1' : '' })} />
              <span>{t('inventoryDesk.filterOverdue')}</span>
            </label>
            <label className="issued-items-checkbox inventory-desk-filter">
              <input type="checkbox" checked={state.mustReturn === '1'} onChange={(e) => apply({ mustReturn: e.target.checked ? '1' : '' })} />
              <span>{t('inventoryDesk.filterMustReturn')}</span>
            </label>
            <label className="issued-items-checkbox inventory-desk-filter">
              <input type="checkbox" checked={state.inactive === '1'} onChange={(e) => apply({ inactive: e.target.checked ? '1' : '' })} />
              <span>{t('inventoryDesk.filterInactive')}</span>
            </label>
            <label className="inventory-desk-filter inventory-desk-due-filter">
              <span>{t('inventoryDesk.filterDueBefore')}</span>
              <input type="date" value={state.due} onChange={(e) => apply({ due: e.target.value })} />
            </label>
          </>
        )}
      </FilterBar>

      <div className="inventory-desk-table">
        <DataTable
          columns={columns}
          rows={rows ?? []}
          rowKey={(row) => row.itemId}
          isLoading={rows === null && !failed}
          error={failed ? t('inventoryDesk.loadFailed') : null}
          emptyMessage={filtersActive ? t('inventoryDesk.emptyFiltered') : history ? t('inventoryDesk.emptyHistory') : t('inventoryDesk.emptyOpen')}
        />
      </div>

      {issueOpen && (
        <WarehouseIssueModal
          canOverrideStock={canOverrideStock}
          onClose={() => setIssueOpen(false)}
          onIssued={(message) => {
            showSuccess(message)
            setIssueOpen(false)
            reload()
          }}
        />
      )}

      {returnTarget && (
        <ReturnIssuedItemDialog
          itemName={itemDisplayName({ code: returnTarget.itemCode, name: returnTarget.itemName })}
          details={t('inventoryDesk.returnDetails', {
            employee: `${returnTarget.employeeName} (${returnTarget.employeeNumber})`,
            serial: returnTarget.serialNumber ?? '—',
          })}
          stockTracked={returnTarget.templateId !== null}
          onClose={() => setReturnTarget(null)}
          onSubmit={async (input) => {
            await returnToWarehouse(returnTarget.itemId, input)
            showSuccess(t('issuedItems.return.success'))
            setReturnTarget(null)
            reload()
          }}
        />
      )}
    </div>
  )
}
