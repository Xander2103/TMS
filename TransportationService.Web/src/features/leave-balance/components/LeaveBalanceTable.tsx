import { useLocale } from '../../../i18n/localeContext'
import type { LeaveBalanceRow } from '../types'

interface LeaveBalanceTableProps {
  rows: LeaveBalanceRow[]
  /** Extra actions column (HR buttons per row). */
  renderActions?: (row: LeaveBalanceRow) => React.ReactNode
}

function fmt(days: number): string {
  return Number.isInteger(days) ? String(days) : days.toFixed(1).replace('.', ',')
}

/** Read-only figures per balance type; shared by the HR tab and the self-service card. */
export function LeaveBalanceTable({ rows, renderActions }: LeaveBalanceTableProps) {
  const { t } = useLocale()
  return (
    <div className="lb-table-wrap">
      <table className="lb-table">
        <thead>
          <tr>
            <th>{t('leave.table.balanceType')}</th>
            <th>{t('leave.table.granted')}</th>
            <th>{t('leave.table.carryOver')}</th>
            <th>{t('leave.table.adjustments')}</th>
            <th>{t('leave.table.approvedUsed')}</th>
            <th>{t('leave.table.pending')}</th>
            <th>{t('leave.table.remaining')}</th>
            {renderActions && <th aria-label={t('leave.table.actions')} />}
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => (
            <tr key={row.balanceTypeId}>
              <td>{row.balanceTypeName}</td>
              <td>{fmt(row.baseEntitlementDays)}</td>
              <td>{fmt(row.carryOverDays)}</td>
              <td>{fmt(row.manualAdjustmentDays)}</td>
              <td>{fmt(row.approvedUsedDays)}</td>
              <td>
                {fmt(row.pendingReservedDays)}
                {row.pendingReservedDays > 0 && (
                  <span className="lb-reserved-note">
                    {' '}
                    {row.pendingReserves ? t('leave.table.reserved') : t('leave.table.notReserved')}
                  </span>
                )}
              </td>
              <td className={row.remainingDays < 0 ? 'lb-remaining lb-negative' : 'lb-remaining'}>{fmt(row.remainingDays)}</td>
              {renderActions && <td className="lb-actions-cell">{renderActions(row)}</td>}
            </tr>
          ))}
          {rows.length === 0 && (
            <tr>
              <td colSpan={renderActions ? 8 : 7} className="placeholder-text">{t('leave.table.empty')}</td>
            </tr>
          )}
        </tbody>
      </table>
    </div>
  )
}
