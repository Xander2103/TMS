import { useState } from 'react'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { useLocale } from '../../../i18n/localeContext'
import { EmployeeSelect } from './EmployeePicker'

interface ApplyTemplateDialogProps {
  templateName: string
  busy: boolean
  onSubmit: (employeeId: string, startAt: string | null) => void
  onClose: () => void
}

/** Applies a template to one employee (perm tasks.assign): employee + optional start moment. */
export function ApplyTemplateDialog({ templateName, busy, onSubmit, onClose }: ApplyTemplateDialogProps) {
  const { t } = useLocale()
  const [employeeId, setEmployeeId] = useState<string | null>(null)
  const [startAt, setStartAt] = useState('')
  const [error, setError] = useState<string | undefined>()

  return (
    <Modal
      title={t('tasks.applyDialog.title', { name: templateName })}
      onClose={onClose}
      busy={busy}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            {t('ui.actions.cancel')}
          </Button>
          <Button
            onClick={() => {
              if (!employeeId) {
                setError(t('tasks.applyDialog.chooseEmployee'))
                return
              }
              onSubmit(employeeId, startAt || null)
            }}
            disabled={busy}
          >
            {busy ? t('ui.actions.busy') : t('tasks.applyDialog.apply')}
          </Button>
        </>
      }
    >
      <FormField label={t('tasks.applyDialog.employee')} required error={error}>
        <EmployeeSelect
          value={employeeId}
          onChange={(next) => {
            setEmployeeId(next)
            if (error && next) setError(undefined)
          }}
          disabled={busy}
        />
      </FormField>
      <FormField label={t('tasks.applyDialog.startOptional')} htmlFor="apply-start">
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
