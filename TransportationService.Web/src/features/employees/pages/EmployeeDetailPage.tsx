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
import { EmployeeForm } from '../components/EmployeeForm'
import { QualificationsTab } from '../components/QualificationsTab'
import { useEmployee } from '../hooks/useEmployee'
import { useEmployeeMutations } from '../hooks/useEmployeeMutations'
import { EMPLOYMENT_STATUS_LABELS, EMPLOYMENT_STATUS_TONES } from '../types/employee'
import './EmployeeDetailPage.css'

const TAB_IDS = ['profiel', 'kwalificaties', 'afwezigheden', 'historiek'] as const
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

  const requestedTab = searchParams.get('tab')
  const tab: TabId = TAB_IDS.includes(requestedTab as TabId) ? (requestedTab as TabId) : 'profiel'

  const canEdit = hasPermission('employees.edit')
  const canDeactivate = hasPermission('employees.deactivate')

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
        {employee.driverId && (
          <Link to={`/drivers/${employee.driverId}`} className="employee-driver-link">
            Chauffeursprofiel bekijken →
          </Link>
        )}
      </div>

      <Tabs
        tabs={[
          { id: 'profiel', label: 'Profiel' },
          { id: 'kwalificaties', label: 'Kwalificaties' },
          { id: 'afwezigheden', label: 'Afwezigheden' },
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
              onCancel={() => navigate('/employees')}
              onSubmit={async (values) => {
                const updated = await mutations.update(employee.id, values)
                if (updated) {
                  toast.showSuccess('Medewerker bijgewerkt.')
                  reload()
                }
              }}
            />
          ) : (
            <p className="placeholder-text">Je hebt alleen leesrechten voor dit profiel.</p>
          )}
        </TabPanel>
      )}

      {tab === 'kwalificaties' && (
        <TabPanel tabId="kwalificaties">
          <QualificationsTab employeeId={employee.id} />
        </TabPanel>
      )}

      {tab === 'afwezigheden' && (
        <TabPanel tabId="afwezigheden">
          <AbsencesTab employeeId={employee.id} />
        </TabPanel>
      )}

      {tab === 'historiek' && (
        <TabPanel tabId="historiek">
          <AuditHistoryPanel entityType="Employee" entityId={employee.id} />
        </TabPanel>
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
