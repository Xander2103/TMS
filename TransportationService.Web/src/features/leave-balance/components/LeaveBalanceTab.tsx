import { useEffect, useState } from 'react'
import { Button } from '../../../components/ui/Button'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import { describeApiError } from '../../../api/problemDetails'
import {
  addLeaveAdjustment,
  getEmployeeLeaveBalance,
  getLeaveAdjustments,
  setLeaveEntitlement,
} from '../api/leaveBalanceApi'
import { LEAVE_ADJUSTMENT_KIND_LABELS, type EmployeeLeaveBalance, type LeaveAdjustment, type LeaveAdjustmentKind, type LeaveBalanceRow } from '../types'
import { LeaveBalanceTable } from './LeaveBalanceTable'
import { SetEntitlementDialog } from './SetEntitlementDialog'
import { AdjustBalanceDialog } from './AdjustBalanceDialog'
import './leave-balance.css'

interface LeaveBalanceTabProps {
  employeeId: string
}

const CURRENT_YEAR = new Date().getFullYear()
const YEARS = [CURRENT_YEAR + 1, CURRENT_YEAR, CURRENT_YEAR - 1, CURRENT_YEAR - 2]

type Dialog = { kind: 'entitlement' | 'adjust'; row: LeaveBalanceRow } | null

/** HR/Management view of an employee's leave balance: year selector, per-balance figures, and management actions. */
export function LeaveBalanceTab({ employeeId }: LeaveBalanceTabProps) {
  const { hasPermission } = useAuth()
  const { showSuccess, showError } = useToast()
  const canManage = hasPermission('leave_balances.manage')
  const canAdjust = hasPermission('leave_balances.adjust')

  const [year, setYear] = useState(CURRENT_YEAR)
  // Request-key idiom: loading derives from whether the loaded data matches the current request.
  const [state, setState] = useState<{ data: EmployeeLeaveBalance | null; loadedKey: string }>({ data: null, loadedKey: '' })
  const [reloadToken, setReloadToken] = useState(0)
  const [dialog, setDialog] = useState<Dialog>(null)
  const [busy, setBusy] = useState(false)
  const [history, setHistory] = useState<{ balanceTypeId: string; name: string; items: LeaveAdjustment[] } | null>(null)

  const requestKey = `${employeeId}:${year}:${reloadToken}`
  const balance = state.data
  const loading = state.loadedKey !== requestKey

  useEffect(() => {
    let mounted = true
    getEmployeeLeaveBalance(employeeId, year)
      .then((data) => {
        if (mounted) setState({ data, loadedKey: requestKey })
      })
      .catch((err) => {
        if (!mounted) return
        setState((current) => ({ ...current, loadedKey: requestKey }))
        showError(describeApiError(err, 'Het verlofsaldo kon niet worden geladen.').message)
      })
    return () => {
      mounted = false
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [requestKey])

  async function submitEntitlement(base: number, carry: number, reason: string | null) {
    if (!dialog) return
    setBusy(true)
    try {
      await setLeaveEntitlement(employeeId, year, { balanceTypeId: dialog.row.balanceTypeId, baseEntitlementDays: base, carryOverDays: carry, reason })
      showSuccess('Jaarrecht bijgewerkt.')
      setDialog(null)
      setReloadToken((t) => t + 1)
    } catch (err) {
      showError(describeApiError(err, 'Het jaarrecht kon niet worden opgeslagen.').message)
    } finally {
      setBusy(false)
    }
  }

  async function submitAdjustment(days: number, reason: string, kind: LeaveAdjustmentKind) {
    if (!dialog) return
    setBusy(true)
    try {
      await addLeaveAdjustment(employeeId, year, { balanceTypeId: dialog.row.balanceTypeId, days, reason, kind })
      showSuccess('Saldo aangepast.')
      setDialog(null)
      setReloadToken((t) => t + 1)
      if (history?.balanceTypeId === dialog.row.balanceTypeId) await openHistory(dialog.row)
    } catch (err) {
      showError(describeApiError(err, 'De aanpassing kon niet worden opgeslagen.').message)
    } finally {
      setBusy(false)
    }
  }

  async function openHistory(row: LeaveBalanceRow) {
    try {
      const items = await getLeaveAdjustments(employeeId, year, row.balanceTypeId)
      setHistory({ balanceTypeId: row.balanceTypeId, name: row.balanceTypeName, items })
    } catch (err) {
      showError(describeApiError(err, 'De historiek kon niet worden geladen.').message)
    }
  }

  return (
    <div className="leave-balance-tab">
      <div className="lb-toolbar">
        <label>
          Jaar{' '}
          <select value={year} onChange={(e) => setYear(Number(e.target.value))}>
            {YEARS.map((y) => (
              <option key={y} value={y}>{y}</option>
            ))}
          </select>
        </label>
      </div>

      {loading && <p className="portal-empty">Verlofsaldo laden…</p>}

      {!loading && balance && (
        <LeaveBalanceTable
          rows={balance.rows}
          renderActions={
            canManage || canAdjust
              ? (row) => (
                  <div className="lb-row-actions">
                    {canManage && (
                      <Button variant="secondary" onClick={() => setDialog({ kind: 'entitlement', row })}>Jaarrecht instellen</Button>
                    )}
                    {canAdjust && (
                      <Button variant="secondary" onClick={() => setDialog({ kind: 'adjust', row })}>Saldo aanpassen</Button>
                    )}
                    <Button variant="ghost" onClick={() => openHistory(row)}>Historiek</Button>
                  </div>
                )
              : undefined
          }
        />
      )}

      {history && (
        <div className="lb-history">
          <h4>Aanpassingshistoriek — {history.name}</h4>
          {history.items.length === 0 ? (
            <p className="placeholder-text">Geen handmatige aanpassingen.</p>
          ) : (
            <ul>
              {history.items.map((a) => (
                <li key={a.id}>
                  <strong>{a.days > 0 ? `+${a.days}` : a.days}</strong> · {LEAVE_ADJUSTMENT_KIND_LABELS[a.kind]} ·{' '}
                  {new Date(a.createdAt).toLocaleDateString('nl-BE')} — {a.reason}
                </li>
              ))}
            </ul>
          )}
          <Button variant="ghost" onClick={() => setHistory(null)}>Sluiten</Button>
        </div>
      )}

      {dialog?.kind === 'entitlement' && (
        <SetEntitlementDialog row={dialog.row} year={year} busy={busy} onSubmit={submitEntitlement} onClose={() => setDialog(null)} />
      )}
      {dialog?.kind === 'adjust' && (
        <AdjustBalanceDialog row={dialog.row} year={year} busy={busy} onSubmit={submitAdjustment} onClose={() => setDialog(null)} />
      )}
    </div>
  )
}
