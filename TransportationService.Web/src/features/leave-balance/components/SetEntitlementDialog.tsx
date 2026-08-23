import { useState } from 'react'
import { Modal } from '../../../components/ui/Modal'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { useLocale } from '../../../i18n/localeContext'
import type { LeaveBalanceRow } from '../types'

interface SetEntitlementDialogProps {
  row: LeaveBalanceRow
  year: number
  busy: boolean
  onSubmit: (baseEntitlementDays: number, carryOverDays: number, reason: string | null) => void
  onClose: () => void
}

function parse(value: string): number {
  const n = Number(value.replace(',', '.'))
  return Number.isFinite(n) ? n : 0
}

export function SetEntitlementDialog({ row, year, busy, onSubmit, onClose }: SetEntitlementDialogProps) {
  const { t } = useLocale()
  const [base, setBase] = useState(String(row.baseEntitlementDays))
  const [carry, setCarry] = useState(String(row.carryOverDays))
  const [reason, setReason] = useState('')

  return (
    <Modal
      title={t('leave.entitlementDialog.title', { name: row.balanceTypeName, year })}
      onClose={onClose}
      busy={busy}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>{t('ui.actions.cancel')}</Button>
          <Button onClick={() => onSubmit(parse(base), parse(carry), reason.trim() || null)} disabled={busy}>
            {busy ? t('leave.saving') : t('ui.actions.save')}
          </Button>
        </>
      }
    >
      <FormField label={t('leave.entitlementDialog.base')} htmlFor="lb-base">
        <input id="lb-base" value={base} onChange={(e) => setBase(e.target.value)} inputMode="decimal" />
      </FormField>
      <FormField label={t('leave.entitlementDialog.carry')} htmlFor="lb-carry" hint={t('leave.entitlementDialog.carryHint')}>
        <input id="lb-carry" value={carry} onChange={(e) => setCarry(e.target.value)} inputMode="decimal" />
      </FormField>
      <FormField label={t('leave.entitlementDialog.reason')} htmlFor="lb-reason" hint={t('leave.entitlementDialog.reasonHint')}>
        <input id="lb-reason" value={reason} onChange={(e) => setReason(e.target.value)} maxLength={500} />
      </FormField>
    </Modal>
  )
}
