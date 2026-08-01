import { useState } from 'react'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { EmployeeSelect } from './EmployeePicker'

interface ApplyTemplateDialogProps {
  templateName: string
  busy: boolean
  onSubmit: (employeeId: string, startAt: string | null) => void
  onClose: () => void
}

/** Applies a template to one employee (perm tasks.assign): employee + optional start moment. */
export function ApplyTemplateDialog({ templateName, busy, onSubmit, onClose }: ApplyTemplateDialogProps) {
  const [employeeId, setEmployeeId] = useState<string | null>(null)
  const [startAt, setStartAt] = useState('')
  const [error, setError] = useState<string | undefined>()

  return (
    <Modal
      title={`Sjabloon "${templateName}" toepassen`}
      onClose={onClose}
      busy={busy}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Annuleren
          </Button>
          <Button
            onClick={() => {
              if (!employeeId) {
                setError('Kies een medewerker.')
                return
              }
              onSubmit(employeeId, startAt || null)
            }}
            disabled={busy}
          >
            {busy ? 'Bezig...' : 'Toepassen'}
          </Button>
        </>
      }
    >
      <FormField label="Medewerker" required error={error}>
        <EmployeeSelect
          value={employeeId}
          onChange={(next) => {
            setEmployeeId(next)
            if (error && next) setError(undefined)
          }}
          disabled={busy}
        />
      </FormField>
      <FormField label="Start (optioneel)" htmlFor="apply-start">
        <input
          id="apply-start"
          type="datetime-local"
          value={startAt}
          onChange={(event) => setStartAt(event.target.value)}
          disabled={busy}
        />
      </FormField>
    </Modal>
  )
}
