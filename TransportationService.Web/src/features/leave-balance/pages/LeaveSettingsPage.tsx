import { useEffect, useState } from 'react'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import { describeApiError } from '../../../api/problemDetails'
import {
  getLeaveBalanceTypes,
  getLeaveSettings,
  getLeaveTypes,
  saveLeaveBalanceType,
  saveLeaveType,
  updateLeaveSettings,
  type SaveLeaveBalanceTypeInput,
  type SaveLeaveTypeInput,
} from '../api/leaveBalanceApi'
import type { LeaveBalanceType, LeaveSettings, LeaveType } from '../types'
import { BalanceTypeDialog } from '../components/BalanceTypeDialog'
import { LeaveTypeDialog } from '../components/LeaveTypeDialog'
import '../components/leave-balance.css'

export function LeaveSettingsPage() {
  const { hasPermission } = useAuth()
  const { showSuccess, showError } = useToast()
  const canManage = hasPermission('leave_types.manage')

  const [settings, setSettings] = useState<LeaveSettings | null>(null)
  const [balanceTypes, setBalanceTypes] = useState<LeaveBalanceType[]>([])
  const [leaveTypes, setLeaveTypes] = useState<LeaveType[]>([])
  const [reload, setReload] = useState(0)
  const [busy, setBusy] = useState(false)
  const [balanceDialog, setBalanceDialog] = useState<{ initial: LeaveBalanceType | null } | null>(null)
  const [leaveDialog, setLeaveDialog] = useState<{ initial: LeaveType | null } | null>(null)

  useEffect(() => {
    let mounted = true
    Promise.all([getLeaveSettings(), getLeaveBalanceTypes(), getLeaveTypes()])
      .then(([s, bt, lt]) => {
        if (!mounted) return
        setSettings(s)
        setBalanceTypes(bt)
        setLeaveTypes(lt)
      })
      .catch((err) => showError(describeApiError(err, 'De verlofinstellingen konden niet worden geladen.').message))
    return () => {
      mounted = false
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [reload])

  async function saveSettings() {
    if (!settings) return
    setBusy(true)
    try {
      await updateLeaveSettings(settings)
      showSuccess('Instellingen opgeslagen.')
    } catch (err) {
      showError(describeApiError(err, 'Opslaan is mislukt.').message)
    } finally {
      setBusy(false)
    }
  }

  async function submitBalanceType(input: SaveLeaveBalanceTypeInput) {
    setBusy(true)
    try {
      await saveLeaveBalanceType(balanceDialog?.initial?.id ?? null, input)
      showSuccess('Saldotype opgeslagen.')
      setBalanceDialog(null)
      setReload((n) => n + 1)
    } catch (err) {
      showError(describeApiError(err, 'Opslaan is mislukt.').message)
    } finally {
      setBusy(false)
    }
  }

  async function submitLeaveType(input: SaveLeaveTypeInput) {
    setBusy(true)
    try {
      await saveLeaveType(leaveDialog?.initial?.id ?? null, input)
      showSuccess('Verloftype opgeslagen.')
      setLeaveDialog(null)
      setReload((n) => n + 1)
    } catch (err) {
      showError(describeApiError(err, 'Opslaan is mislukt.').message)
    } finally {
      setBusy(false)
    }
  }

  const balanceName = (id: string | null) => balanceTypes.find((b) => b.id === id)?.name ?? '—'

  return (
    <div>
      <Breadcrumbs items={[{ label: 'Instellingen', to: '/settings' }, { label: 'Verlof' }]} />
      <PageHeader title="Verlof — types & saldi" subtitle="Configureer verloftypes, saldotypes en de standaardregels." />

      {settings && (
        <section className="leave-balance-card">
          <h3>Algemene regels</h3>
          <FormField label="Standaard jaarrecht (wettelijk verlof, dagen)" htmlFor="ls-default">
            <input id="ls-default" type="number" step="0.5" value={settings.defaultAnnualEntitlementDays}
              onChange={(e) => setSettings({ ...settings, defaultAnnualEntitlementDays: Number(e.target.value) || 0 })} disabled={!canManage} />
          </FormField>
          <FormField label="Max. overdracht (dagen, leeg = geen limiet)" htmlFor="ls-maxcarry">
            <input id="ls-maxcarry" type="number" step="0.5" value={settings.maxCarryOverDays ?? ''}
              onChange={(e) => setSettings({ ...settings, maxCarryOverDays: e.target.value === '' ? null : Number(e.target.value) })} disabled={!canManage} />
          </FormField>
          <div className="lb-checkbox-grid">
            <label className="customer-form-checkbox">
              <input type="checkbox" checked={settings.pendingReservesBalance} disabled={!canManage}
                onChange={(e) => setSettings({ ...settings, pendingReservesBalance: e.target.checked })} /> Aanvragen reserveren saldo
            </label>
            <label className="customer-form-checkbox">
              <input type="checkbox" checked={settings.allowNegativeBalance} disabled={!canManage}
                onChange={(e) => setSettings({ ...settings, allowNegativeBalance: e.target.checked })} /> Negatief saldo toestaan
            </label>
            <label className="customer-form-checkbox">
              <input type="checkbox" checked={settings.carryOverEnabled} disabled={!canManage}
                onChange={(e) => setSettings({ ...settings, carryOverEnabled: e.target.checked })} /> Overdracht inschakelen
            </label>
          </div>
          {canManage && <Button onClick={saveSettings} disabled={busy}>Instellingen opslaan</Button>}
        </section>
      )}

      <section className="leave-balance-card">
        <div className="lb-section-head">
          <h3>Saldotypes</h3>
          {canManage && <Button variant="secondary" onClick={() => setBalanceDialog({ initial: null })}>+ Saldotype</Button>}
        </div>
        <LeaveTable
          headers={['Code', 'Naam', 'Actief', 'Volgorde']}
          rows={balanceTypes.map((b) => [b.code, b.name, b.isActive ? 'Ja' : 'Nee', String(b.sortOrder)])}
          onEdit={canManage ? (i) => setBalanceDialog({ initial: balanceTypes[i] }) : undefined}
        />
      </section>

      <section className="leave-balance-card">
        <div className="lb-section-head">
          <h3>Verloftypes</h3>
          {canManage && <Button variant="secondary" onClick={() => setLeaveDialog({ initial: null })}>+ Verloftype</Button>}
        </div>
        <LeaveTable
          headers={['Code', 'Naam', 'Boekt af', 'Saldotype', 'Self-service', 'Actief']}
          rows={leaveTypes.map((t) => [t.code, t.name, t.deductsFromBalance ? 'Ja' : 'Nee', t.deductsFromBalance ? balanceName(t.balanceTypeId) : '—', t.visibleInSelfService ? 'Ja' : 'Nee', t.isActive ? 'Ja' : 'Nee'])}
          onEdit={canManage ? (i) => setLeaveDialog({ initial: leaveTypes[i] }) : undefined}
        />
      </section>

      {balanceDialog && (
        <BalanceTypeDialog initial={balanceDialog.initial} busy={busy} onSubmit={submitBalanceType} onClose={() => setBalanceDialog(null)} />
      )}
      {leaveDialog && (
        <LeaveTypeDialog initial={leaveDialog.initial} balanceTypes={balanceTypes} busy={busy} onSubmit={submitLeaveType} onClose={() => setLeaveDialog(null)} />
      )}
    </div>
  )
}

function LeaveTable({ headers, rows, onEdit }: { headers: string[]; rows: string[][]; onEdit?: (index: number) => void }) {
  return (
    <div className="lb-table-wrap">
      <table className="lb-table">
        <thead>
          <tr>
            {headers.map((h) => <th key={h}>{h}</th>)}
            {onEdit && <th aria-label="Acties" />}
          </tr>
        </thead>
        <tbody>
          {rows.map((row, i) => (
            <tr key={i}>
              {row.map((cell, j) => <td key={j}>{cell}</td>)}
              {onEdit && <td className="lb-actions-cell"><Button variant="ghost" onClick={() => onEdit(i)}>Bewerken</Button></td>}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}
