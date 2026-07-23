import { useState } from 'react'
import { Modal } from '../../../components/ui/Modal'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
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
  const [days, setDays] = useState('')
  const [reason, setReason] = useState('')
  const [kind, setKind] = useState<LeaveAdjustmentKind>('Grant')
  const [error, setError] = useState<string | undefined>(undefined)

  function submit() {
    const value = parse(days)
    if (value === 0) {
      setError('Geef een aantal dagen op (positief of negatief).')
      return
    }
    if (!reason.trim()) {
      setError('Een reden is verplicht.')
      return
    }
    onSubmit(value, reason.trim(), kind)
  }

  return (
    <Modal
      title={`Saldo aanpassen — ${row.balanceTypeName} (${year})`}
      onClose={onClose}
      busy={busy}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>Annuleren</Button>
          <Button onClick={submit} disabled={busy}>{busy ? 'Opslaan…' : 'Aanpassing toevoegen'}</Button>
        </>
      }
    >
      <FormField label="Aantal dagen" htmlFor="lb-adj-days" hint="Bv. 2 of -0,5." error={error}>
        <input id="lb-adj-days" value={days} onChange={(e) => setDays(e.target.value)} inputMode="decimal" />
      </FormField>
      <FormField label="Soort" htmlFor="lb-adj-kind">
        <select id="lb-adj-kind" value={kind} onChange={(e) => setKind(e.target.value as LeaveAdjustmentKind)}>
          {(Object.keys(LEAVE_ADJUSTMENT_KIND_LABELS) as LeaveAdjustmentKind[]).map((k) => (
            <option key={k} value={k}>{LEAVE_ADJUSTMENT_KIND_LABELS[k]}</option>
          ))}
        </select>
      </FormField>
      <FormField label="Reden" htmlFor="lb-adj-reason" required>
        <textarea id="lb-adj-reason" value={reason} onChange={(e) => setReason(e.target.value)} rows={2} maxLength={500} />
      </FormField>
    </Modal>
  )
}
