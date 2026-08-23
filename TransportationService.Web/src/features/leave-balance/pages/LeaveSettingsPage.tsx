import { useEffect, useState } from 'react'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { FormField } from '../../../components/ui/FormField'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import { useLocale } from '../../../i18n/localeContext'
import { describeApiError } from '../../../api/problemDetails'
import {
  deleteLeaveBalanceType,
  deleteLeaveType,
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
  const { t } = useLocale()
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
  const [deleteTarget, setDeleteTarget] = useState<{ kind: 'balance' | 'leave'; id: string; name: string } | null>(null)

  useEffect(() => {
    let mounted = true
    Promise.all([getLeaveSettings(), getLeaveBalanceTypes(), getLeaveTypes()])
      .then(([s, bt, lt]) => {
        if (!mounted) return
        setSettings(s)
        setBalanceTypes(bt)
        setLeaveTypes(lt)
      })
      .catch((err) => showError(describeApiError(err, t('leave.settings.loadFailed')).message))
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
      showSuccess(t('leave.settings.settingsSaved'))
    } catch (err) {
      showError(describeApiError(err, t('leave.settings.saveFailed')).message)
    } finally {
      setBusy(false)
    }
  }

  async function submitBalanceType(input: SaveLeaveBalanceTypeInput) {
    setBusy(true)
    try {
      await saveLeaveBalanceType(balanceDialog?.initial?.id ?? null, input)
      showSuccess(t('leave.settings.balanceTypeSaved'))
      setBalanceDialog(null)
      setReload((n) => n + 1)
    } catch (err) {
      showError(describeApiError(err, t('leave.settings.saveFailed')).message)
    } finally {
      setBusy(false)
    }
  }

  async function submitLeaveType(input: SaveLeaveTypeInput) {
    setBusy(true)
    try {
      await saveLeaveType(leaveDialog?.initial?.id ?? null, input)
      showSuccess(t('leave.settings.leaveTypeSaved'))
      setLeaveDialog(null)
      setReload((n) => n + 1)
    } catch (err) {
      showError(describeApiError(err, t('leave.settings.saveFailed')).message)
    } finally {
      setBusy(false)
    }
  }

  async function handleDelete() {
    if (!deleteTarget) return
    const target = deleteTarget
    setDeleteTarget(null)
    try {
      await (target.kind === 'balance' ? deleteLeaveBalanceType(target.id) : deleteLeaveType(target.id))
      showSuccess(t('leave.settings.deleted', { name: target.name }))
      setReload((n) => n + 1)
    } catch (err) {
      // A used category is refused server-side with the exact deactivate-instead message.
      showError(describeApiError(err, t('leave.settings.deleteFailed')).message)
    }
  }

  const balanceName = (id: string | null) => balanceTypes.find((b) => b.id === id)?.name ?? '—'
  const yesNo = (value: boolean) => (value ? t('leave.settings.yes') : t('leave.settings.no'))

  return (
    <div>
      <Breadcrumbs items={[{ label: t('leave.settings.breadcrumbSettings'), to: '/settings' }, { label: t('leave.settings.breadcrumb') }]} />
      <PageHeader title={t('leave.settings.title')} subtitle={t('leave.settings.subtitle')} />

      {settings && (
        <section className="leave-balance-card">
          <h3>{t('leave.settings.generalRules')}</h3>
          <FormField label={t('leave.settings.defaultEntitlement')} htmlFor="ls-default">
            <input id="ls-default" type="number" step="0.5" value={settings.defaultAnnualEntitlementDays}
              onChange={(e) => setSettings({ ...settings, defaultAnnualEntitlementDays: Number(e.target.value) || 0 })} disabled={!canManage} />
          </FormField>
          <FormField label={t('leave.settings.maxCarry')} htmlFor="ls-maxcarry">
            <input id="ls-maxcarry" type="number" step="0.5" value={settings.maxCarryOverDays ?? ''}
              onChange={(e) => setSettings({ ...settings, maxCarryOverDays: e.target.value === '' ? null : Number(e.target.value) })} disabled={!canManage} />
          </FormField>
          <div className="lb-checkbox-grid">
            <label className="customer-form-checkbox">
              <input type="checkbox" checked={settings.pendingReservesBalance} disabled={!canManage}
                onChange={(e) => setSettings({ ...settings, pendingReservesBalance: e.target.checked })} /> {t('leave.settings.pendingReserves')}
            </label>
            <label className="customer-form-checkbox">
              <input type="checkbox" checked={settings.allowNegativeBalance} disabled={!canManage}
                onChange={(e) => setSettings({ ...settings, allowNegativeBalance: e.target.checked })} /> {t('leave.settings.allowNegative')}
            </label>
            <label className="customer-form-checkbox">
              <input type="checkbox" checked={settings.carryOverEnabled} disabled={!canManage}
                onChange={(e) => setSettings({ ...settings, carryOverEnabled: e.target.checked })} /> {t('leave.settings.carryOverEnabled')}
            </label>
          </div>
          {canManage && <Button onClick={saveSettings} disabled={busy}>{t('leave.settings.saveSettings')}</Button>}
        </section>
      )}

      <section className="leave-balance-card">
        <div className="lb-section-head">
          <h3>{t('leave.settings.balanceTypes')}</h3>
          {canManage && <Button variant="secondary" onClick={() => setBalanceDialog({ initial: null })}>{t('leave.settings.addBalanceType')}</Button>}
        </div>
        <LeaveTable
          headers={[t('leave.settings.colCode'), t('leave.settings.colName'), t('leave.settings.colActive'), t('leave.settings.colOrder')]}
          rows={balanceTypes.map((b) => [b.code, b.name, yesNo(b.isActive), String(b.sortOrder)])}
          onEdit={canManage ? (i) => setBalanceDialog({ initial: balanceTypes[i] }) : undefined}
          onDelete={canManage ? (i) => setDeleteTarget({ kind: 'balance', id: balanceTypes[i].id, name: balanceTypes[i].name }) : undefined}
        />
      </section>

      <section className="leave-balance-card">
        <div className="lb-section-head">
          <h3>{t('leave.settings.leaveTypes')}</h3>
          {canManage && <Button variant="secondary" onClick={() => setLeaveDialog({ initial: null })}>{t('leave.settings.addLeaveType')}</Button>}
        </div>
        <LeaveTable
          headers={[
            t('leave.settings.colCode'),
            t('leave.settings.colName'),
            t('leave.settings.colDeducts'),
            t('leave.settings.colBalanceType'),
            t('leave.settings.colSelfService'),
            t('leave.settings.colActive'),
          ]}
          rows={leaveTypes.map((lt) => [
            lt.code,
            lt.name,
            yesNo(lt.deductsFromBalance),
            lt.deductsFromBalance ? balanceName(lt.balanceTypeId) : '—',
            yesNo(lt.visibleInSelfService),
            yesNo(lt.isActive),
          ])}
          onEdit={canManage ? (i) => setLeaveDialog({ initial: leaveTypes[i] }) : undefined}
          onDelete={canManage ? (i) => setDeleteTarget({ kind: 'leave', id: leaveTypes[i].id, name: leaveTypes[i].name }) : undefined}
        />
      </section>

      {balanceDialog && (
        <BalanceTypeDialog initial={balanceDialog.initial} busy={busy} onSubmit={submitBalanceType} onClose={() => setBalanceDialog(null)} />
      )}
      {leaveDialog && (
        <LeaveTypeDialog initial={leaveDialog.initial} balanceTypes={balanceTypes} busy={busy} onSubmit={submitLeaveType} onClose={() => setLeaveDialog(null)} />
      )}
      {deleteTarget && (
        <ConfirmDialog
          title={t('leave.settings.deleteTitle')}
          message={t('leave.settings.deleteMessage', { name: deleteTarget.name })}
          confirmLabel={t('ui.actions.delete')}
          destructive
          onConfirm={() => void handleDelete()}
          onCancel={() => setDeleteTarget(null)}
        />
      )}
    </div>
  )
}

function LeaveTable({
  headers,
  rows,
  onEdit,
  onDelete,
}: {
  headers: string[]
  rows: string[][]
  onEdit?: (index: number) => void
  onDelete?: (index: number) => void
}) {
  const { t } = useLocale()
  const hasActions = onEdit !== undefined || onDelete !== undefined
  return (
    <div className="lb-table-wrap">
      <table className="lb-table">
        <thead>
          <tr>
            {headers.map((h) => <th key={h}>{h}</th>)}
            {hasActions && <th aria-label={t('leave.table.actions')} />}
          </tr>
        </thead>
        <tbody>
          {rows.map((row, i) => (
            <tr key={i}>
              {row.map((cell, j) => <td key={j}>{cell}</td>)}
              {hasActions && (
                <td className="lb-actions-cell">
                  {onEdit && <Button variant="ghost" onClick={() => onEdit(i)}>{t('ui.actions.edit')}</Button>}
                  {onDelete && <Button variant="ghost" onClick={() => onDelete(i)}>{t('ui.actions.delete')}</Button>}
                </td>
              )}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}
