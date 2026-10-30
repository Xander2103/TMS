import { useEffect, useState } from 'react'
import { Button } from '../../../components/ui/Button'
import { Modal } from '../../../components/ui/Modal'
import { ValidationSummary } from '../../../components/ui/ValidationSummary'
import { describeApiError } from '../../../api/problemDetails'
import { useLocale } from '../../../i18n/localeContext'
import { deactivateEmployee, getOffboardingImpact, reactivateEmployee, type OffboardingImpact } from '../api/employeesApi'
import { Link } from 'react-router-dom'
import { Badge } from '../../../components/ui/Badge'
import { useAuth } from '../../auth/authContextValue'
import { TRIP_STATUS_LABELS, TRIP_STATUS_TONE } from '../../planning/types'
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
  const [impact, setImpact] = useState<OffboardingImpact | null>(null)
  const [impactFailed, setImpactFailed] = useState(false)
  const { hasPermission } = useAuth()

  useEffect(() => {
    if (!deactivating) return
    let mounted = true
    getOffboardingImpact(employeeId)
      .then((data) => {
        if (mounted) setImpact(data)
      })
      .catch(() => {
        // UX-012: decision support, never a precondition; the failure is said out loud.
        if (mounted) setImpactFailed(true)
      })
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
      {deactivating && (
        <section className="employee-lifecycle-impact" data-testid="offboarding-impact" aria-label={t('employees.offboardingImpact.title')}>
          <h3>{t('employees.offboardingImpact.title')}</h3>
          {impactFailed && <p role="alert">{t('employees.offboardingImpact.failed')}</p>}
          {!impact && !impactFailed && <p>{t('employees.offboardingImpact.loading')}</p>}
          {impact && (
            <>
              <p>
                {impact.activeAccounts > 0
                  ? t('employees.offboardingImpact.account', { count: impact.activeAccounts })
                  : t('employees.offboardingImpact.noAccount')}
              </p>
              {impact.upcomingTripCount === 0 && impact.vehicles.length === 0 && !impact.fixedTrailerNumber && (
                <p>{t('employees.offboardingImpact.none')}</p>
              )}
              {impact.upcomingTripCount > 0 && (
                <>
                  <p><strong>{t('employees.offboardingImpact.tripsTitle', { count: impact.upcomingTripCount })}</strong></p>
                  <ul>
                    {impact.upcomingTrips.map((trip) => (
                      <li key={trip.id}>
                        {hasPermission('planning.view') ? <Link to={`/planning/${trip.id}`}>{trip.tripNumber}</Link> : trip.tripNumber}
                        {' · '}{formatDate(trip.tripDate)}{' '}
                        <Badge tone={TRIP_STATUS_TONE[trip.status]}>{t(TRIP_STATUS_LABELS[trip.status])}</Badge>
                      </li>
                    ))}
                  </ul>
                  {impact.upcomingTripCount > impact.upcomingTrips.length && (
                    <p>{t('employees.offboardingImpact.tripsMore', { count: impact.upcomingTripCount - impact.upcomingTrips.length })}</p>
                  )}
                </>
              )}
              {(impact.vehicles.length > 0 || impact.fixedTrailerNumber) && (
                <>
                  <p><strong>{t('employees.offboardingImpact.vehiclesTitle')}</strong></p>
                  <ul>
                    {impact.vehicles.map((vehicle) => (
                      <li key={vehicle.id}>
                        {vehicle.internalNumber} · {vehicle.licensePlate}
                        {' ('}
                        {[
                          vehicle.isFixedDriver ? t('employees.offboardingImpact.vehicleFixed') : null,
                          vehicle.isCurrentDriver ? t('employees.offboardingImpact.vehicleCurrent') : null,
                        ].filter(Boolean).join(', ')}
                        {')'}
                      </li>
                    ))}
                    {impact.fixedTrailerNumber && (
                      <li>{impact.fixedTrailerNumber} ({t('employees.offboardingImpact.trailerFixed')})</li>
                    )}
                  </ul>
                </>
              )}
            </>
          )}
        </section>
      )}
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
