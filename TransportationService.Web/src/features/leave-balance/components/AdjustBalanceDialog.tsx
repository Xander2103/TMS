import { useState } from 'react'
import { Modal } from '../../../components/ui/Modal'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { useLocale } from '../../../i18n/localeContext'
import { LEAVE_ADJUSTMENT_KIND_LABELS, type LeaveAdjustmentKind, type LeaveBalanceRow } from '../types'

interface AdjustBalanceDialogProps {
  row: LeaveBalanceRow
  year: number
  busy: boolean
  onSubmit: (days: number, reason: string, kind: LeaveAdjustmentKind) => void
  onClose: () => void
}

function parse(value: string): number {
  const n = Number(value.replace(',', '.'))
  return Number.isFinite(n) ? n : 0
}

export function AdjustBalanceDialog({ row, year, busy, onSubmit, onClose }: AdjustBalanceDialogProps) {
  const { t } = useLocale()
  const [days, setDays] = useState('')
  const [reason, setReason] = useState('')
  const [kind, setKind] = useState<LeaveAdjustmentKind>('Grant')
  // Vertaalsleutel in state; vertaling gebeurt pas bij render.
  const [errorKey, setErrorKey] = useState<string | undefined>(undefined)

  function submit() {
    const value = parse(days)
    if (value === 0) {
      setErrorKey('leave.adjustDialog.daysRequired')
      return
    }
    if (!reason.trim()) {
      setErrorKey('leave.adjustDialog.reasonRequired')
      return
    }
    onSubmit(value, reason.trim(), kind)
  }

  return (
    <Modal
      title={t('leave.adjustDialog.title', { name: row.balanceTypeName, year })}
      onClose={onClose}
      busy={busy}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>{t('ui.actions.cancel')}</Button>
          <Button onClick={submit} disabled={busy}>{busy ? t('leave.saving') : t('leave.adjustDialog.submit')}</Button>
        </>
      }
    >
      <FormField label={t('leave.adjustDialog.days')} htmlFor="lb-adj-days" hint={t('leave.adjustDialog.daysHint')} error={errorKey ? t(errorKey) : undefined}>
        <input id="lb-adj-days" value={days} onChange={(e) => setDays(e.target.value)} inputMode="decimal" />
      </FormField>
      <FormField label={t('leave.adjustDialog.kind')} htmlFor="lb-adj-kind">
        <select id="lb-adj-kind" value={kind} onChange={(e) => setKind(e.target.value as LeaveAdjustmentKind)}>
          {(Object.keys(LEAVE_ADJUSTMENT_KIND_LABELS) as LeaveAdjustmentKind[]).map((k) => (
            <option key={k} value={k}>{t(LEAVE_ADJUSTMENT_KIND_LABELS[k])}</option>
          ))}
        </select>
      </FormField>
      <FormField label={t('leave.adjustDialog.reason')} htmlFor="lb-adj-reason" required>
        <textarea id="lb-adj-reason" value={reason} onChange={(e) => setReason(e.target.value)} rows={2} maxLength={500} />
      </FormField>
    </Modal>
  )
}
