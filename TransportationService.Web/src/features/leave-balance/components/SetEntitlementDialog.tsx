import { useState } from 'react'
import { Modal } from '../../../components/ui/Modal'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import type { LeaveBalanceRow } from '../types'

interface SetEntitlementDialogProps {
  row: LeaveBalanceRow
  year: number
  busy: boolean
  onSubmit: (baseEntitlementDays: number, carryOverDays: number) => void
  onClose: () => void
}

function parse(value: string): number {
  const n = Number(value.replace(',', '.'))
  return Number.isFinite(n) ? n : 0
}

export function SetEntitlementDialog({ row, year, busy, onSubmit, onClose }: SetEntitlementDialogProps) {
  const [base, setBase] = useState(String(row.baseEntitlementDays))
  const [carry, setCarry] = useState(String(row.carryOverDays))

  return (
    <Modal
      title={`Jaarrecht instellen — ${row.balanceTypeName} (${year})`}
      onClose={onClose}
      busy={busy}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>Annuleren</Button>
          <Button onClick={() => onSubmit(parse(base), parse(carry))} disabled={busy}>{busy ? 'Opslaan…' : 'Opslaan'}</Button>
        </>
      }
    >
      <FormField label="Toegekend (basisrecht in dagen)" htmlFor="lb-base">
        <input id="lb-base" value={base} onChange={(e) => setBase(e.target.value)} inputMode="decimal" />
      </FormField>
      <FormField label="Overdracht (dagen)" htmlFor="lb-carry" hint="Handmatige overdracht van het vorige jaar.">
        <input id="lb-carry" value={carry} onChange={(e) => setCarry(e.target.value)} inputMode="decimal" />
      </FormField>
    </Modal>
  )
}
