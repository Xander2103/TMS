import { useEffect, useState } from 'react'
import { Button } from '../../../components/ui/Button'
import { Modal } from '../../../components/ui/Modal'
import { ValidationSummary } from '../../../components/ui/ValidationSummary'
import { describeApiError } from '../../../api/problemDetails'
import { useLocale } from '../../../i18n/localeContext'
import { deactivateEmployee, reactivateEmployee } from '../api/employeesApi'
import { getOutstandingEquipment, type OutstandingEquipment } from '../../issued-items/inventoryApi'
import { formatDate } from '../../../utils/dates'

interface EmployeeLifecycleDialogProps {
  mode: 'deactivate' | 'reactivate'
  employeeId: string
  employeeName: string
  onDone: () => void
  onCancel: () => void
}

/**
 * Offboarding / re-onboarding confirmation. Deactivating an employee is a cascade on the server
 * (linked login blocked + sessions revoked, driver no longer plannable, fixed/current vehicle
 * assignment and fixed trailer released; history kept), so the dialog spells those consequences out. A refusal — e.g. the
 * linked account is the last active administrator — stays in the dialog with the server's own
 * (Dutch) message instead of a generic toast. Reactivation deliberately re-enables neither the
 * login nor the driver; the dialog says so up front.
 *
 * WH-H5: before deactivating, the dialog lists the company items the employee still holds and
 * must return (item, variant, serial, issue and expected return date). It is a warning, never a
 * block — ending access must not wait for equipment — and nothing is returned automatically.
 */
export function EmployeeLifecycleDialog({ mode, employeeId, employeeName, onDone, onCancel }: EmployeeLifecycleDialogProps) {
  const { t } = useLocale()
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const deactivating = mode === 'deactivate'
  const [outstanding, setOutstanding] = useState<OutstandingEquipment | null>(null)

  useEffect(() => {
    if (!deactivating) return
    let mounted = true
    getOutstandingEquipment(employeeId)
      .then((data) => {
        if (mounted) setOutstanding(data)
      })
      .catch(() => {
        /* the warning is informative; offboarding never depends on it */
      })
    return () => {
      mounted = false
    }
  }, [deactivating, employeeId])

  async function confirm() {
    setBusy(true)
    setError(null)
    try {
      await (deactivating ? deactivateEmployee(employeeId) : reactivateEmployee(employeeId))
      onDone()
    } catch (err) {
      setError(describeApiError(err, t('employees.errors.actionFailedRetry')).message)
      setBusy(false)
    }
  }

  return (
    <Modal
      title={t(deactivating ? 'employees.detail.deactivateTitle' : 'employees.detail.reactivateTitle')}
      onClose={onCancel}
      busy={busy}
      footer={
        <>
          <Button variant="secondary" onClick={onCancel} disabled={busy}>
            {t('ui.actions.cancel')}
          </Button>
          <Button variant={deactivating ? 'danger' : 'primary'} onClick={confirm} disabled={busy}>
            {busy
              ? t('ui.actions.busy')
              : t(deactivating ? 'employees.detail.deactivateConfirm' : 'employees.detail.reactivate')}
          </Button>
        </>
      }
    >
      <ValidationSummary message={error} />
      <p>{t(deactivating ? 'employees.detail.deactivateMessage' : 'employees.detail.reactivateMessage', { name: employeeName })}</p>
      <p>{t(deactivating ? 'employees.detail.deactivateConsequences' : 'employees.detail.reactivateHint')}</p>
      {deactivating && outstanding && outstanding.count > 0 && (
        <div className="employee-lifecycle-outstanding" role="alert" data-testid="offboarding-outstanding">
          <p>
            <strong>{t('inventoryDesk.offboarding.warning', { count: outstanding.count })}</strong>
          </p>
          <ul>
            {outstanding.items.map((item) => (
              <li key={item.itemId}>
                {[item.itemCode, item.itemName, item.variantLabel].filter(Boolean).join(' · ')}
                {item.serialNumber && ` — ${t('inventoryDesk.offboarding.serial', { serial: item.serialNumber })}`}
                {item.issuedDate && ` — ${t('inventoryDesk.offboarding.issued', { date: formatDate(item.issuedDate) })}`}
                {item.expectedReturnDate && ` — ${t('inventoryDesk.offboarding.due', { date: formatDate(item.expectedReturnDate) })}`}
              </li>
            ))}
          </ul>
          <p>{t('inventoryDesk.offboarding.hint')}</p>
        </div>
      )}
    </Modal>
  )
}
