import { useState } from 'react'
import { LoadingState } from '../../../components/feedback/LoadingState'
import { ErrorState } from '../../../components/feedback/ErrorState'
import { useEmployeeQualifications } from '../hooks/useEmployeeQualifications'
import { useQualificationMutations } from '../hooks/useQualificationMutations'
import { QualificationStatusBadge } from './QualificationStatusBadge'
import { QualificationDialog } from './QualificationDialog'
import './QualificationsTab.css'

interface QualificationsTabProps {
  employeeId: string
}

export function QualificationsTab({ employeeId }: QualificationsTabProps) {
  const { qualifications, isLoading, error, reload } = useEmployeeQualifications(employeeId)
  const { isSubmitting, verify, suspend } = useQualificationMutations()
  const [isDialogOpen, setIsDialogOpen] = useState(false)

  async function handleVerify(id: string) {
    const saved = await verify(employeeId, id)
    if (saved) reload()
  }

  async function handleSuspend(id: string) {
    if (!window.confirm('Weet u zeker dat u deze kwalificatie wilt schorsen?')) return
    const saved = await suspend(employeeId, id)
    if (saved) reload()
  }

  if (isLoading) {
    return <LoadingState message="Kwalificaties laden..." />
  }

  if (error) {
    return <ErrorState message={error} />
  }

  return (
    <div className="qualifications-tab">
      <button type="button" className="primary-button" onClick={() => setIsDialogOpen(true)}>
        Toevoegen
      </button>

      {qualifications.length === 0 ? (
        <p>Er zijn nog geen kwalificaties geregistreerd.</p>
      ) : (
        <table className="qualifications-table">
          <thead>
            <tr>
              <th scope="col">Type</th>
              <th scope="col">Behaald</th>
              <th scope="col">Vervalt</th>
              <th scope="col">Status</th>
              <th scope="col">Acties</th>
            </tr>
          </thead>
          <tbody>
            {qualifications.map((qualification) => (
              <tr key={qualification.id}>
                <td>{qualification.qualificationTypeName}</td>
                <td>{qualification.obtainedDate}</td>
                <td>{qualification.expiryDate ?? '—'}</td>
                <td>
                  <QualificationStatusBadge status={qualification.effectiveStatus} />
                </td>
                <td>
                  <div className="qualification-actions">
                    <button type="button" onClick={() => handleVerify(qualification.id)} disabled={isSubmitting}>
                      Verifiëren
                    </button>
                    <button type="button" onClick={() => handleSuspend(qualification.id)} disabled={isSubmitting}>
                      Schorsen
                    </button>
                  </div>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      {isDialogOpen && (
        <QualificationDialog
          employeeId={employeeId}
          onSaved={() => {
            setIsDialogOpen(false)
            reload()
          }}
          onCancel={() => setIsDialogOpen(false)}
        />
      )}
    </div>
  )
}
