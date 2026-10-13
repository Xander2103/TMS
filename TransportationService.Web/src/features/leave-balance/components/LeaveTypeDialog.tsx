import { useState } from 'react'
import { Modal } from '../../../components/ui/Modal'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { useLocale } from '../../../i18n/localeContext'
import type { AbsenceTypeCode, LeaveBalanceType, LeaveType } from '../types'
import type { SaveLeaveTypeInput } from '../api/leaveBalanceApi'

/** Vertaalsleutels per afwezigheidssoort; renderen als t(key). */
const ABSENCE_TYPES: { value: AbsenceTypeCode; labelKey: string }[] = [
  { value: 'Vacation', labelKey: 'leave.absenceType.Vacation' },
  { value: 'PersonalLeave', labelKey: 'leave.absenceType.PersonalLeave' },
  { value: 'Sick', labelKey: 'leave.absenceType.Sick' },
  { value: 'Unpaid', labelKey: 'leave.absenceType.Unpaid' },
  { value: 'Training', labelKey: 'leave.absenceType.Training' },
  { value: 'Other', labelKey: 'leave.absenceType.Other' },
]

interface LeaveTypeDialogProps {
  initial: LeaveType | null
  balanceTypes: LeaveBalanceType[]
  busy: boolean
  onSubmit: (input: SaveLeaveTypeInput) => void
  onClose: () => void
}

export function LeaveTypeDialog({ initial, balanceTypes, busy, onSubmit, onClose }: LeaveTypeDialogProps) {
  const { t } = useLocale()
  const [f, setF] = useState<SaveLeaveTypeInput>({
    code: initial?.code ?? '',
    name: initial?.name ?? '',
    description: initial?.description ?? null,
    isActive: initial?.isActive ?? true,
    isPaid: initial?.isPaid ?? true,
    deductsFromBalance: initial?.deductsFromBalance ?? false,
    balanceTypeId: initial?.balanceTypeId ?? null,
    absenceType: initial?.absenceType ?? 'Vacation',
    requiresApproval: initial?.requiresApproval ?? true,
    allowsHalfDays: initial?.allowsHalfDays ?? true,
    requiresReason: initial?.requiresReason ?? false,
    requiresAttachment: initial?.requiresAttachment ?? false,
    visibleInSelfService: initial?.visibleInSelfService ?? true,
    colour: initial?.colour ?? '#2563eb',
    sortOrder: initial?.sortOrder ?? 0,
  })
  const set = <K extends keyof SaveLeaveTypeInput>(key: K, value: SaveLeaveTypeInput[K]) => setF((prev) => ({ ...prev, [key]: value }))

  const check = (label: string, key: keyof SaveLeaveTypeInput) => (
    <label className="customer-form-checkbox">
      <input type="checkbox" checked={f[key] as boolean} onChange={(e) => set(key, e.target.checked as never)} /> {label}
    </label>
  )

  return (
    <Modal
      title={initial ? t('leave.leaveTypeDialog.editTitle', { name: initial.name }) : t('leave.leaveTypeDialog.newTitle')}
      onClose={onClose}
      busy={busy}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>{t('ui.actions.cancel')}</Button>
          <Button
            onClick={() => onSubmit({ ...f, code: f.code.trim(), name: f.name.trim(), balanceTypeId: f.deductsFromBalance ? f.balanceTypeId : null })}
            disabled={busy}
          >
            {busy ? t('leave.saving') : t('ui.actions.save')}
          </Button>
        </>
      }
    >
      <FormField label={t('leave.leaveTypeDialog.code')} htmlFor="lt-code" required>
        <input id="lt-code" value={f.code} onChange={(e) => set('code', e.target.value)} maxLength={30} disabled={!!initial} />
      </FormField>
      <FormField label={t('leave.leaveTypeDialog.name')} htmlFor="lt-name" required>
        <input id="lt-name" value={f.name} onChange={(e) => set('name', e.target.value)} maxLength={100} />
      </FormField>
      <FormField label={t('leave.leaveTypeDialog.absenceType')} htmlFor="lt-absencetype">
        <select id="lt-absencetype" value={f.absenceType} onChange={(e) => set('absenceType', e.target.value as AbsenceTypeCode)}>
          {ABSENCE_TYPES.map((type) => <option key={type.value} value={type.value}>{t(type.labelKey)}</option>)}
        </select>
      </FormField>
      {check(t('leave.leaveTypeDialog.deducts'), 'deductsFromBalance')}
      {f.deductsFromBalance && (
        <FormField label={t('leave.leaveTypeDialog.balanceType')} htmlFor="lt-balance" required>
          <select id="lt-balance" value={f.balanceTypeId ?? ''} onChange={(e) => set('balanceTypeId', e.target.value || null)}>
            <option value="">{t('leave.leaveTypeDialog.choose')}</option>
            {balanceTypes.map((b) => <option key={b.id} value={b.id}>{b.name}</option>)}
          </select>
        </FormField>
      )}
      <FormField label={t('leave.leaveTypeDialog.colour')} htmlFor="lt-colour">
        <input id="lt-colour" type="color" value={f.colour ?? '#2563eb'} onChange={(e) => set('colour', e.target.value)} />
      </FormField>
      <FormField label={t('leave.leaveTypeDialog.order')} htmlFor="lt-sort">
        <input id="lt-sort" type="number" value={f.sortOrder} onChange={(e) => set('sortOrder', Number(e.target.value) || 0)} />
      </FormField>
      <div className="lb-checkbox-grid">
        {check(t('leave.leaveTypeDialog.active'), 'isActive')}
        {check(t('leave.leaveTypeDialog.paid'), 'isPaid')}
        {check(t('leave.leaveTypeDialog.requiresApproval'), 'requiresApproval')}
        {check(t('leave.leaveTypeDialog.allowsHalfDays'), 'allowsHalfDays')}
        {check(t('leave.leaveTypeDialog.requiresReason'), 'requiresReason')}
        {check(t('leave.leaveTypeDialog.requiresAttachment'), 'requiresAttachment')}
        {check(t('leave.leaveTypeDialog.selfService'), 'visibleInSelfService')}
      </div>
    </Modal>
  )
}
