import { useState } from 'react'
import { Modal } from '../../../components/ui/Modal'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { useLocale } from '../../../i18n/localeContext'
import type { LeaveBalanceType } from '../types'
import type { SaveLeaveBalanceTypeInput } from '../api/leaveBalanceApi'

interface BalanceTypeDialogProps {
  initial: LeaveBalanceType | null
  busy: boolean
  onSubmit: (input: SaveLeaveBalanceTypeInput) => void
  onClose: () => void
}

export function BalanceTypeDialog({ initial, busy, onSubmit, onClose }: BalanceTypeDialogProps) {
  const { t } = useLocale()
  const [code, setCode] = useState(initial?.code ?? '')
  const [name, setName] = useState(initial?.name ?? '')
  const [description, setDescription] = useState(initial?.description ?? '')
  const [isActive, setIsActive] = useState(initial?.isActive ?? true)
  const [sortOrder, setSortOrder] = useState(String(initial?.sortOrder ?? 0))

  return (
    <Modal
      title={initial ? t('leave.balanceTypeDialog.editTitle', { name: initial.name }) : t('leave.balanceTypeDialog.newTitle')}
      onClose={onClose}
      busy={busy}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>{t('ui.actions.cancel')}</Button>
          <Button
            onClick={() => onSubmit({ code: code.trim(), name: name.trim(), description: description.trim() || null, isActive, sortOrder: Number(sortOrder) || 0 })}
            disabled={busy}
          >
            {busy ? t('leave.saving') : t('ui.actions.save')}
          </Button>
        </>
      }
    >
      <FormField label={t('leave.balanceTypeDialog.code')} htmlFor="bt-code" required>
        <input id="bt-code" value={code} onChange={(e) => setCode(e.target.value)} maxLength={30} disabled={!!initial} />
      </FormField>
      <FormField label={t('leave.balanceTypeDialog.name')} htmlFor="bt-name" required>
        <input id="bt-name" value={name} onChange={(e) => setName(e.target.value)} maxLength={100} />
      </FormField>
      <FormField label={t('leave.balanceTypeDialog.description')} htmlFor="bt-desc">
        <input id="bt-desc" value={description} onChange={(e) => setDescription(e.target.value)} maxLength={500} />
      </FormField>
      <FormField label={t('leave.balanceTypeDialog.order')} htmlFor="bt-sort">
        <input id="bt-sort" type="number" value={sortOrder} onChange={(e) => setSortOrder(e.target.value)} />
      </FormField>
      <label className="customer-form-checkbox">
        <input type="checkbox" checked={isActive} onChange={(e) => setIsActive(e.target.checked)} /> {t('leave.balanceTypeDialog.active')}
      </label>
    </Modal>
  )
}
