import { useEffect, useState } from 'react'
import { useAuth } from '../../auth/authContextValue'
import { getMyLeaveBalance } from '../api/leaveBalanceApi'
import type { EmployeeLeaveBalance } from '../types'
import { LeaveBalanceTable } from './LeaveBalanceTable'
import './leave-balance.css'

const CURRENT_YEAR = new Date().getFullYear()
const YEARS = [CURRENT_YEAR + 1, CURRENT_YEAR, CURRENT_YEAR - 1]

/**
 * Read-only self-service leave balance for the signed-in employee/driver. Renders nothing
 * without the view-own permission. Employees can never edit their balance here.
 */
export function MyLeaveBalanceCard() {
  const { hasPermission } = useAuth()
  const canView = hasPermission('leave_balances.view_own')

  const [year, setYear] = useState(CURRENT_YEAR)
  const [state, setState] = useState<{ data: EmployeeLeaveBalance | null; loadedKey: string }>({ data: null, loadedKey: '' })

  const requestKey = String(year)
  const loading = canView && state.loadedKey !== requestKey

  useEffect(() => {
    if (!canView) return
    let mounted = true
    getMyLeaveBalance(year)
      .then((data) => {
        if (mounted) setState({ data, loadedKey: requestKey })
      })
      .catch(() => {
        if (mounted) setState((current) => ({ ...current, loadedKey: requestKey }))
      })
    return () => {
      mounted = false
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [requestKey, canView])

  if (!canView) return null

  return (
    <section className="leave-balance-card">
      <h3>Mijn verlofsaldo</h3>
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
      {!loading && state.data && <LeaveBalanceTable rows={state.data.rows} />}
    </section>
  )
}
