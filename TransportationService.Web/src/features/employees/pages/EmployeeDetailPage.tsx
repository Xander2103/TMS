import { useState } from 'react'
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { LoadingState } from '../../../components/feedback/LoadingState'
import { ErrorState } from '../../../components/feedback/ErrorState'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { StatusBadges } from '../../../components/ui/StatusBadges'
import { TabPanel, Tabs } from '../../../components/ui/Tabs'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import { AbsencesTab } from '../../absences/components/AbsencesTab'
import { AuditHistoryPanel } from '../../auditing/components/AuditHistoryPanel'
import { getDriver, updateDriver } from '../../drivers/api/driversApi'
import { EmployeeForm } from '../components/EmployeeForm'
import { EmployeePlanningTab } from '../components/EmployeePlanningTab'
import { EmployeeTripsTab } from '../components/EmployeeTripsTab'
import { QualificationsTab } from '../components/QualificationsTab'
import { useEmployee } from '../hooks/useEmployee'
import { useEmployeeMutations } from '../hooks/useEmployeeMutations'
import { EMPLOYMENT_STATUS_LABELS, EMPLOYMENT_STATUS_TONES } from '../types/employee'
import './EmployeeDetailPage.css'

const TAB_IDS = ['profiel', 'planning', 'kwalificaties', 'afwezigheden', 'ritten', 'historiek'] as const
type TabId = (typeof TAB_IDS)[number]

export function EmployeeDetailPage() {
  const { id = '' } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const toast = useToast()
  const { hasPermission } = useAuth()
  const [searchParams, setSearchParams] = useSearchParams()
  const { employee, isLoading, error, reload } = useEmployee(id)
  const mutations = useEmployeeMutations()
  const [confirmLifecycle, setConfirmLifecycle] = useState<'deactivate' | 'reactivate' | null>(null)
  // Codes of the functions currently chosen in the edit form (null until the user touches them).
  const [editedFunctionCodes, setEditedFunctionCodes] = useState<string[] | null>(null)
  const [offerDriverDeactivation, setOfferDriverDeactivation] = useState(false)
  const [driverBusy, setDriverBusy] = useState(false)

  const requestedTab = searchParams.get('tab')
  const tab: TabId = TAB_IDS.includes(requestedTab as TabId) ? (requestedTab as TabId) : 'profiel'

  const canEdit = hasPermission('employees.edit')
  const canDeactivate = hasPermission('employees.deactivate')
  const canViewPlanning = hasPermission('employee_planning.view') || hasPermission('employee_planning.manage')
  const canViewTrips = hasPermission('planning.view')

  if (isLoading) return <LoadingState message="Medewerker laden..." />
  if (error || !employee) return <ErrorState message={error ?? 'Medewerker niet gevonden.'} />

  function setTab(next: string) {
    setSearchParams(next === 'profiel' ? {} : { tab: next }, { replace: true })
  }

  return (
    <div>
      <Breadcrumbs items={[{ label: 'Personeel', to: '/employees' }, { label: `${employee.firstName} ${employee.lastName}` }]} />
      <PageHeader
        title={`${employee.firstName} ${employee.lastName}`}
        subtitle={`${employee.employeeNumber}${employee.functionNames.length > 0 ? ` · ${employee.functionNames.join(', ')}` : ''}`}
        action={
          canDeactivate && (
            <Button
              variant={employee.isActive ? 'danger' : 'secondary'}
              onClick={() => setConfirmLifecycle(employee.isActive ? 'deactivate' : 'reactivate')}
              disabled={mutations.isSubmitting}
            >
              {employee.isActive ? 'Deactiveren' : 'Heractiveren'}
            </Button>
          )
        }
      />

      <div className="employee-detail-status">
        <StatusBadges
          active={employee.isActive}
          operational={{
            label: EMPLOYMENT_STATUS_LABELS[employee.employmentStatus],
            tone: EMPLOYMENT_STATUS_TONES[employee.employmentStatus],
          }}
        />
        {employee.driverId ? (
          <Link to={`/drivers/${employee.driverId}`} className="employee-driver-link">
            Chauffeursprofiel bekijken →
          </Link>
        ) : (
          hasPermission('drivers.create') &&
          employee.isActive && (
            <Link to={`/drivers/new?employeeId=${employee.id}`} className="employee-driver-link">
              Chauffeursprofiel aanmaken →
            </Link>
          )
        )}
      </div>

      <Tabs
        tabs={[
          { id: 'profiel', label: 'Profiel' },
          ...(canViewPlanning ? [{ id: 'planning', label: 'Planning' }] : []),
          { id: 'kwalificaties', label: 'Kwalificaties' },
          { id: 'afwezigheden', label: 'Afwezigheden' },
          ...(employee.driverId && canViewTrips ? [{ id: 'ritten', label: 'Ritten' }] : []),
          { id: 'historiek', label: 'Historiek' },
        ]}
        activeId={tab}
        onChange={setTab}
      />

      {tab === 'profiel' && (
        <TabPanel tabId="profiel">
          {canEdit ? (
            <EmployeeForm
              mode="edit"
              initial={employee}
              isSubmitting={mutations.isSubmitting}
              submitError={mutations.error}
              serverFieldErrors={mutations.fieldErrors}
              onCancel={() => navigate('/employees')}
              onFunctionsChanged={setEditedFunctionCodes}
              onSubmit={async (values) => {
                const updated = await mutations.update(employee.id, values)
                if (updated) {
                  toast.showSuccess('Medewerker bijgewerkt.')
                  // Driver functions removed while a driver profile exists → offer (never force)
                  // deactivating that profile. Historical driver data is always preserved.
                  const removedDriverFunctions =
                    employee.driverId !== null &&
                    editedFunctionCodes !== null &&
                    !editedFunctionCodes.some((code) => code.toUpperCase().startsWith('CHAUF'))
                  if (removedDriverFunctions) {
                    setOfferDriverDeactivation(true)
                  }
                  reload()
                }
              }}
            />
          ) : (
            <p className="placeholder-text">Je hebt alleen leesrechten voor dit profiel.</p>
          )}
        </TabPanel>
      )}

      {tab === 'planning' && canViewPlanning && (
        <TabPanel tabId="planning">
          <EmployeePlanningTab employeeId={employee.id} />
        </TabPanel>
      )}

      {tab === 'kwalificaties' && (
        <TabPanel tabId="kwalificaties">
          <QualificationsTab employeeId={employee.id} />
        </TabPanel>
      )}

      {tab === 'afwezigheden' && (
        <TabPanel tabId="afwezigheden">
          <AbsencesTab employeeId={employee.id} highlightAbsenceId={searchParams.get('absenceId')} />
        </TabPanel>
      )}

      {tab === 'ritten' && employee.driverId && canViewTrips && (
        <TabPanel tabId="ritten">
          <EmployeeTripsTab driverId={employee.driverId} />
        </TabPanel>
      )}

      {tab === 'historiek' && (
        <TabPanel tabId="historiek">
          <AuditHistoryPanel entityType="Employee" entityId={employee.id} />
        </TabPanel>
      )}

      {offerDriverDeactivation && employee.driverId && (
        <ConfirmDialog
          title="Chauffeursprofiel deactiveren?"
          message="De chauffeursfuncties zijn verwijderd. Wil je het gekoppelde chauffeursprofiel deactiveren? De historiek en kwalificaties blijven bewaard."
          confirmLabel="Profiel deactiveren"
          cancelLabel="Profiel actief laten"
          busy={driverBusy}
          onConfirm={async () => {
            setDriverBusy(true)
            try {
              const driver = await getDriver(employee.driverId!)
              await updateDriver(employee.driverId!, {
                driverCategoryId: driver.categoryId,
                availabilityStatus: driver.availabilityStatus,
                isActive: false,
                fixedTrailerId: driver.fixedTrailerId,
                notes: driver.notes,
              })
              toast.showSuccess('Chauffeursprofiel gedeactiveerd.')
            } catch {
              toast.showError('Chauffeursprofiel kon niet worden gedeactiveerd.')
            } finally {
              setDriverBusy(false)
              setOfferDriverDeactivation(false)
            }
          }}
          onCancel={() => setOfferDriverDeactivation(false)}
        />
      )}

      {confirmLifecycle === 'deactivate' && (
        <ConfirmDialog
          title="Medewerker deactiveren"
          message={`${employee.firstName} ${employee.lastName} deactiveren? Het dienstverband wordt op beëindigd gezet; historiek blijft bewaard.`}
          confirmLabel="Deactiveren"
          destructive
          busy={mutations.isSubmitting}
          onConfirm={async () => {
            const ok = await mutations.deactivate(employee.id)
            if (ok) {
              toast.showSuccess('Medewerker gedeactiveerd.')
              setConfirmLifecycle(null)
              reload()
            }
          }}
          onCancel={() => setConfirmLifecycle(null)}
        />
      )}

      {confirmLifecycle === 'reactivate' && (
        <ConfirmDialog
          title="Medewerker heractiveren"
          message={`${employee.firstName} ${employee.lastName} opnieuw activeren?`}
          confirmLabel="Heractiveren"
          busy={mutations.isSubmitting}
          onConfirm={async () => {
            const ok = await mutations.reactivate(employee.id)
            if (ok) {
              toast.showSuccess('Medewerker geheractiveerd.')
              setConfirmLifecycle(null)
              reload()
            }
          }}
          onCancel={() => setConfirmLifecycle(null)}
        />
      )}
    </div>
  )
}
