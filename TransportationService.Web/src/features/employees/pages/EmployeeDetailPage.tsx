import { useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { LoadingState } from '../../../components/feedback/LoadingState'
import { ErrorState } from '../../../components/feedback/ErrorState'
import { EmployeeForm } from '../components/EmployeeForm'
import { QualificationsTab } from '../components/QualificationsTab'
import { AbsencesTab } from '../../absences/components/AbsencesTab'
import { useEmployee } from '../hooks/useEmployee'
import { useEmployeeMutations } from '../hooks/useEmployeeMutations'
import './EmployeeDetailPage.css'

type Tab = 'details' | 'qualifications' | 'absences'

export function EmployeeDetailPage() {
  const { id = '' } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const { employee, isLoading, error, reload } = useEmployee(id)
  const { isSubmitting, deactivate } = useEmployeeMutations()
  const [tab, setTab] = useState<Tab>('details')

  async function handleDeactivate() {
    if (!employee) return
    if (!window.confirm(`Weet u zeker dat u ${employee.firstName} ${employee.lastName} wilt deactiveren?`)) {
      return
    }
    const deactivated = await deactivate(employee.id)
    if (deactivated) {
      reload()
    }
  }

  if (isLoading) {
    return <LoadingState message="Medewerker laden..." />
  }

  if (error || !employee) {
    return <ErrorState message={error ?? 'Medewerker niet gevonden.'} />
  }

  return (
    <>
      <PageHeader title={`${employee.firstName} ${employee.lastName}`} />

      <div className="employee-tabs" role="tablist">
        <button type="button" role="tab" aria-selected={tab === 'details'} className={tab === 'details' ? 'employee-tab active' : 'employee-tab'} onClick={() => setTab('details')}>
          Gegevens
        </button>
        <button type="button" role="tab" aria-selected={tab === 'qualifications'} className={tab === 'qualifications' ? 'employee-tab active' : 'employee-tab'} onClick={() => setTab('qualifications')}>
          Kwalificaties
        </button>
        <button type="button" role="tab" aria-selected={tab === 'absences'} className={tab === 'absences' ? 'employee-tab active' : 'employee-tab'} onClick={() => setTab('absences')}>
          Afwezigheden
        </button>
      </div>

      {tab === 'details' && (
        <>
          <EmployeeForm employee={employee} onSaved={() => reload()} />
          <div className="form-actions employee-detail-section">
            <button type="button" className="primary-button" onClick={handleDeactivate} disabled={isSubmitting || !employee.isActive}>
              {employee.isActive ? 'Deactiveren' : 'Reeds inactief'}
            </button>
          </div>
        </>
      )}

      {tab === 'qualifications' && <QualificationsTab employeeId={employee.id} />}

      {tab === 'absences' && <AbsencesTab employeeId={employee.id} />}

      <button type="button" className="secondary-link employee-detail-section" onClick={() => navigate('/employees')}>
        Terug naar overzicht
      </button>
    </>
  )
}
