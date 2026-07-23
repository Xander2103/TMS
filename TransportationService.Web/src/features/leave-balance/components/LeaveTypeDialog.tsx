import { useState } from 'react'
import { Modal } from '../../../components/ui/Modal'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import type { AbsenceTypeCode, LeaveBalanceType, LeaveType } from '../types'
import type { SaveLeaveTypeInput } from '../api/leaveBalanceApi'

const ABSENCE_TYPES: { value: AbsenceTypeCode; label: string }[] = [
  { value: 'Vacation', label: 'Verlof' },
  { value: 'PersonalLeave', label: 'Persoonlijk verlof' },
  { value: 'Sick', label: 'Ziekte' },
  { value: 'Unpaid', label: 'Onbetaald' },
  { value: 'Training', label: 'Opleiding' },
  { value: 'Other', label: 'Andere' },
]

interface LeaveTypeDialogProps {
  initial: LeaveType | null
  balanceTypes: LeaveBalanceType[]
  busy: boolean
  onSubmit: (input: SaveLeaveTypeInput) => void
  onClose: () => void
}

export function LeaveTypeDialog({ initial, balanceTypes, busy, onSubmit, onClose }: LeaveTypeDialogProps) {
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
      title={initial ? `Verloftype bewerken — ${initial.name}` : 'Nieuw verloftype'}
      onClose={onClose}
      busy={busy}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>Annuleren</Button>
          <Button
            onClick={() => onSubmit({ ...f, code: f.code.trim(), name: f.name.trim(), balanceTypeId: f.deductsFromBalance ? f.balanceTypeId : null })}
            disabled={busy}
          >
            {busy ? 'Opslaan…' : 'Opslaan'}
          </Button>
        </>
      }
    >
      <FormField label="Code" htmlFor="lt-code" required>
        <input id="lt-code" value={f.code} onChange={(e) => set('code', e.target.value)} maxLength={30} disabled={!!initial} />
      </FormField>
      <FormField label="Naam" htmlFor="lt-name" required>
        <input id="lt-name" value={f.name} onChange={(e) => set('name', e.target.value)} maxLength={100} />
      </FormField>
      <FormField label="Afwezigheidssoort (planning/historiek)" htmlFor="lt-absencetype">
        <select id="lt-absencetype" value={f.absenceType} onChange={(e) => set('absenceType', e.target.value as AbsenceTypeCode)}>
          {ABSENCE_TYPES.map((t) => <option key={t.value} value={t.value}>{t.label}</option>)}
        </select>
      </FormField>
      {check('Boekt saldo af', 'deductsFromBalance')}
      {f.deductsFromBalance && (
        <FormField label="Saldotype" htmlFor="lt-balance" required>
          <select id="lt-balance" value={f.balanceTypeId ?? ''} onChange={(e) => set('balanceTypeId', e.target.value || null)}>
            <option value="">— Kies —</option>
            {balanceTypes.map((b) => <option key={b.id} value={b.id}>{b.name}</option>)}
          </select>
        </FormField>
      )}
      <FormField label="Kleur (planning)" htmlFor="lt-colour">
        <input id="lt-colour" type="color" value={f.colour ?? '#2563eb'} onChange={(e) => set('colour', e.target.value)} />
      </FormField>
      <FormField label="Volgorde" htmlFor="lt-sort">
        <input id="lt-sort" type="number" value={f.sortOrder} onChange={(e) => set('sortOrder', Number(e.target.value) || 0)} />
      </FormField>
      <div className="lb-checkbox-grid">
        {check('Actief', 'isActive')}
        {check('Betaald', 'isPaid')}
        {check('Vereist goedkeuring', 'requiresApproval')}
        {check('Halve dagen toegestaan', 'allowsHalfDays')}
        {check('Reden verplicht', 'requiresReason')}
        {check('Bijlage verplicht', 'requiresAttachment')}
        {check('Zichtbaar in self-service', 'visibleInSelfService')}
      </div>
    </Modal>
  )
}
